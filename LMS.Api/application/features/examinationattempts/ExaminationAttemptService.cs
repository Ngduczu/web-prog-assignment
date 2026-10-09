using System.Security.Cryptography;
using System.Text;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.common;
using LMS.Api.application.features.courses;
using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.Enums.Questions;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.examinationattempts;

public sealed class ExaminationAttemptService : IExaminationAttemptService
{
    private readonly IExaminationAttemptRepository _attempts;
    private readonly IExaminationRepository _examinations;
    private readonly ICourseRepository _courses;
    private readonly IEnrollmentRepository _enrollments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionManager _transactionManager;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public ExaminationAttemptService(
        IExaminationAttemptRepository attempts,
        IExaminationRepository examinations,
        ICourseRepository courses,
        IEnrollmentRepository enrollments,
        IUnitOfWork unitOfWork,
        ITransactionManager transactionManager,
        ICurrentUser currentUser,
        IClock clock)
    {
        _attempts = attempts;
        _examinations = examinations;
        _courses = courses;
        _enrollments = enrollments;
        _unitOfWork = unitOfWork;
        _transactionManager = transactionManager;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<ExaminationAttemptDto> StartAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationId, "Examination id");
        var studentId = EnsureStudent();
        ExaminationAttempt? createdAttempt = null;
        Examination? examination = null;

        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            examination = await GetExaminationAsync(examinationId, true, transactionCancellationToken);
            _ = await _courses.GetByIdAsync(examination.CourseId, false, transactionCancellationToken)
                ?? throw new NotFoundException("Course was not found.");
            await EnsureAcceptedEnrollmentAsync(studentId, examination.CourseId, transactionCancellationToken);
            EnsureAvailable(examination, _clock.UtcNow);

            if (examination.Questions.Count == 0)
            {
                throw new ConflictException("The examination has no questions.");
            }

            if (await _attempts.GetLatestAsync(
                    examination.Id,
                    studentId,
                    false,
                    transactionCancellationToken) is not null)
            {
                throw new ConflictException("A student can attempt an examination only once.");
            }

            createdAttempt = new ExaminationAttempt(
                examination.Id,
                studentId,
                _clock.UtcNow,
                examination.Questions.Count);
            _attempts.Add(createdAttempt);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        return Map(createdAttempt!, examination!);
    }

    public async Task<ExaminationAttemptDto> GetMineAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationId, "Examination id");
        var studentId = EnsureStudent();
        var examination = await GetExaminationAsync(examinationId, false, cancellationToken);
        _ = await _courses.GetByIdAsync(examination.CourseId, false, cancellationToken)
            ?? throw new NotFoundException("Course was not found.");
        await EnsureAcceptedEnrollmentAsync(studentId, examination.CourseId, cancellationToken);

        var attempt = await _attempts.GetLatestAsync(
            examinationId,
            studentId,
            true,
            cancellationToken) ?? throw new NotFoundException("Examination attempt was not found.");

        return Map(attempt, examination);
    }

    public async Task<PagedResult<ExaminationAttemptResultDto>> ListResultsAsync(
        Guid examinationId,
        ExaminationAttemptQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationId, "Examination id");
        if (query.Page <= 0 || query.PageSize is < 1 or > 100)
            throw new BadRequestException("Invalid pagination.");

        var examination = await GetExaminationAsync(examinationId, false, cancellationToken);
        var course = await _courses.GetByIdAsync(examination.CourseId, false, cancellationToken)
            ?? throw new NotFoundException("Course was not found.");
        CourseAuthorization.EnsureCanManage(_currentUser, course);

        var total = await _attempts.CountByExaminationAsync(examinationId, cancellationToken);
        var attempts = await _attempts.ListByExaminationAsync(
            examinationId,
            (query.Page - 1) * query.PageSize,
            query.PageSize,
            cancellationToken);
        var items = attempts.Select(item =>
        {
            var active = item.Attempt.Status is ExaminationAttemptStatus.InProgress or ExaminationAttemptStatus.Disconnected;
            return new ExaminationAttemptResultDto(
                item.Attempt.Id,
                item.Attempt.ExaminationId,
                item.Attempt.StudentId,
                item.Student.FullName,
                item.Student.Email,
                item.Attempt.Status,
                item.Attempt.SubmitReason,
                item.Attempt.StartedAt,
                item.Attempt.SubmittedAt,
                item.Attempt.ViolationCount,
                active ? null : item.Attempt.CorrectAnswers,
                item.Attempt.TotalQuestions,
                active ? null : item.Attempt.Score);
        }).ToArray();
        return new PagedResult<ExaminationAttemptResultDto>(
            items,
            query.Page,
            query.PageSize,
            total);
    }

    public async Task<IReadOnlyList<StudentExamQuestionDto>> GetQuestionsAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await GetOwnedAttemptAsync(attemptId, false, cancellationToken);
        var examination = await GetExaminationAsync(attempt.ExaminationId, true, cancellationToken);
        await EnsureAcceptedEnrollmentAsync(attempt.StudentId, examination.CourseId, cancellationToken);
        return BuildStudentQuestions(examination, attempt.Id);
    }

    public async Task<StudentAnswerDto> SaveMultipleChoiceAnswerAsync(
        Guid attemptId,
        Guid examinationQuestionId,
        Guid selectedOptionId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationQuestionId, "Examination question id");
        ValidateId(selectedOptionId, "Selected option id");
        var (attempt, examination, question) = await GetAnswerContextAsync(
            attemptId,
            examinationQuestionId,
            cancellationToken);

        if (question.Type != QuestionType.MultipleChoice)
        {
            throw new BadRequestException("The selected question is not multiple choice.");
        }

        if (question.MultipleChoiceOptions.All(option => option.Id != selectedOptionId))
        {
            throw new BadRequestException("The selected option does not belong to this question.");
        }

        var answer = await _attempts.GetAnswerAsync(attempt.Id, question.Id, false, cancellationToken);
        if (answer is null)
        {
            answer = new StudentAnswer(attempt.Id, question.Id);
            _attempts.AddAnswer(answer);
        }

        answer.SelectOption(selectedOptionId);
        attempt.TouchActivity(_clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(answer, examination);
    }

    public async Task<StudentAnswerDto> SaveFillInBlankAnswerAsync(
        Guid attemptId,
        Guid examinationQuestionId,
        IReadOnlyList<StudentFillInBlankAnswerInput> answers,
        CancellationToken cancellationToken = default)
    {
        ValidateFillInBlankAnswers(answers);
        var (attempt, examination, question) = await GetAnswerContextAsync(
            attemptId,
            examinationQuestionId,
            cancellationToken);

        if (question.Type != QuestionType.FillInBlank)
        {
            throw new BadRequestException("The selected question is not fill in blank.");
        }

        var validBlankOrders = question.FillInBlankAnswerKeys.Select(key => key.BlankOrder).ToHashSet();
        if (answers.Any(answer => !validBlankOrders.Contains(answer.BlankOrder)))
        {
            throw new BadRequestException("An answer references a blank that does not exist in this question.");
        }

        var studentAnswer = await _attempts.GetAnswerAsync(attempt.Id, question.Id, true, cancellationToken);
        if (studentAnswer is null)
        {
            studentAnswer = new StudentAnswer(attempt.Id, question.Id);
            _attempts.AddAnswer(studentAnswer);
        }

        studentAnswer.SelectOption(null);
        var inputsByOrder = answers.ToDictionary(answer => answer.BlankOrder);
        foreach (var existingAnswer in studentAnswer.FillInBlankAnswers.ToArray())
        {
            if (inputsByOrder.Remove(existingAnswer.BlankOrder, out var input))
            {
                existingAnswer.SetAnswerText(input.AnswerText.Trim());
            }
            else
            {
                _attempts.RemoveFillInBlankAnswer(existingAnswer);
                studentAnswer.FillInBlankAnswers.Remove(existingAnswer);
            }
        }

        foreach (var input in inputsByOrder.Values)
        {
            var fillInBlankAnswer = new StudentFillInBlankAnswer(
                studentAnswer.Id,
                input.BlankOrder,
                input.AnswerText.Trim());
            studentAnswer.FillInBlankAnswers.Add(fillInBlankAnswer);
            _attempts.AddFillInBlankAnswer(fillInBlankAnswer);
        }

        attempt.TouchActivity(_clock.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(studentAnswer, examination);
    }

    public async Task<ExaminationAttemptDto> DisconnectAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        ExaminationAttempt? attempt = null;
        Examination? examination = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            attempt = await GetOwnedAttemptAsync(attemptId, true, transactionCancellationToken);
            examination = await GetExaminationAsync(attempt.ExaminationId, true, transactionCancellationToken);
            await EnsureAcceptedEnrollmentAsync(
                attempt.StudentId,
                examination.CourseId,
                transactionCancellationToken);
            EnsureStatus(attempt, ExaminationAttemptStatus.InProgress);
            var remainingSeconds = GetRemainingSeconds(attempt, examination, _clock.UtcNow);

            if (remainingSeconds == 0)
            {
                AttemptGrader.CompleteAndGrade(
                    attempt,
                    examination,
                    ExaminationSubmitReason.DurationExpired,
                    true);
            }
            else
            {
                attempt.Disconnect(_clock.UtcNow, remainingSeconds);
            }

            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);
        return Map(attempt!, examination!);
    }

    public async Task<ExaminationAttemptDto> ReconnectAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        ExaminationAttempt? attempt = null;
        Examination? examination = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            attempt = await GetOwnedAttemptAsync(attemptId, true, transactionCancellationToken);
            examination = await GetExaminationAsync(attempt.ExaminationId, true, transactionCancellationToken);
            await EnsureAcceptedEnrollmentAsync(
                attempt.StudentId,
                examination.CourseId,
                transactionCancellationToken);
            EnsureStatus(attempt, ExaminationAttemptStatus.Disconnected);
            var now = _clock.UtcNow;
            var disconnectDeadline = attempt.DisconnectedAt!.Value.AddMinutes(examination.MaxDisconnectMinutes);
            var examinationDeadline = AttemptDeadline.GetExaminationDeadline(attempt, examination);

            if (now >= examinationDeadline || now >= disconnectDeadline)
            {
                AttemptGrader.CompleteAndGrade(
                    attempt,
                    examination,
                    AttemptDeadline.GetExpiredReason(attempt, examination, now),
                    true);
            }
            else
            {
                attempt.Reconnect(now);
            }

            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);
        return Map(attempt!, examination!);
    }

    public async Task<ExaminationAttemptDto> RecordViolationAsync(
        Guid attemptId,
        ExaminationViolationType violationType,
        int? durationSeconds = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(violationType))
        {
            throw new BadRequestException("Examination violation type is invalid.");
        }

        ExaminationAttempt? attempt = null;
        Examination? examination = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            attempt = await GetOwnedAttemptAsync(attemptId, true, transactionCancellationToken);
            examination = await GetExaminationAsync(attempt.ExaminationId, true, transactionCancellationToken);
            await EnsureAcceptedEnrollmentAsync(
                attempt.StudentId,
                examination.CourseId,
                transactionCancellationToken);
            EnsureActive(attempt);

            if (!IsViolationEnabled(examination, violationType))
            {
                throw new BadRequestException("This violation type is not enabled for the examination.");
            }

            if (violationType == ExaminationViolationType.ExitFullscreen)
            {
                if (durationSeconds is null || durationSeconds < 0)
                {
                    throw new BadRequestException("Fullscreen exit duration is required and cannot be negative.");
                }

                if (durationSeconds <= examination.FullscreenGraceSeconds)
                {
                    attempt.TouchActivity(_clock.UtcNow);
                    await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
                    return;
                }
            }

            var violation = new ExaminationViolation(attempt.Id, violationType, _clock.UtcNow);
            _attempts.AddViolation(violation);
            attempt.AddViolation();

            if (examination.MaxViolations > 0 && attempt.ViolationCount >= examination.MaxViolations)
            {
                AttemptGrader.CompleteAndGrade(
                    attempt,
                    examination,
                    ExaminationSubmitReason.MaxViolationsExceeded,
                    true);
            }
            else
            {
                attempt.TouchActivity(_clock.UtcNow);
            }

            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);
        return Map(attempt!, examination!);
    }

    private static bool IsViolationEnabled(
        Examination examination,
        ExaminationViolationType violationType) => violationType switch
    {
        ExaminationViolationType.ExitFullscreen => examination.RequireFullscreen,
        ExaminationViolationType.TabChanged or ExaminationViolationType.WindowBlurred => examination.DetectTabChange,
        ExaminationViolationType.CopyAttempt or ExaminationViolationType.PasteAttempt => examination.BlockCopyPaste,
        ExaminationViolationType.RightClickAttempt => examination.BlockRightClick,
        ExaminationViolationType.DevToolsSuspected => examination.DetectDevTools,
        _ => false
    };

    public async Task<ExaminationAttemptDto> SubmitAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        ExaminationAttempt? submittedAttempt = null;
        Examination? examination = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            submittedAttempt = await GetOwnedAttemptAsync(attemptId, true, transactionCancellationToken);
            examination = await GetExaminationAsync(
                submittedAttempt.ExaminationId,
                true,
                transactionCancellationToken);
            await EnsureAcceptedEnrollmentAsync(
                submittedAttempt.StudentId,
                examination.CourseId,
                transactionCancellationToken);
            EnsureActive(submittedAttempt);
            var expired = GetRemainingSeconds(submittedAttempt, examination, _clock.UtcNow) == 0;
            AttemptGrader.CompleteAndGrade(
                submittedAttempt,
                examination,
                expired ? ExaminationSubmitReason.DurationExpired : ExaminationSubmitReason.StudentSubmitted,
                expired);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);
        return Map(submittedAttempt!, examination!);
    }

    public async Task<ExaminationAttemptDto> AutoSubmitAsync(
        Guid attemptId,
        ExaminationSubmitReason reason,
        CancellationToken cancellationToken = default)
    {
        ValidateId(attemptId, "Examination attempt id");

        if (reason == ExaminationSubmitReason.StudentSubmitted || !Enum.IsDefined(reason))
        {
            throw new BadRequestException("Auto-submit reason is invalid.");
        }

        if (_currentUser.GetRequiredRole() != Role.Admin)
        {
            throw new ForbiddenException("Only administrators can trigger auto-submit.");
        }

        ExaminationAttempt? submittedAttempt = null;
        Examination? examination = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            submittedAttempt = await _attempts.GetByIdAsync(attemptId, true, transactionCancellationToken)
                ?? throw new NotFoundException("Examination attempt was not found.");
            examination = await GetExaminationAsync(
                submittedAttempt.ExaminationId,
                true,
                transactionCancellationToken);
            EnsureActive(submittedAttempt);
            EnsureAutoSubmitReasonApplies(submittedAttempt, examination, reason);
            AttemptGrader.CompleteAndGrade(submittedAttempt, examination, reason, true);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);
        return Map(submittedAttempt!, examination!);
    }

    private void EnsureAutoSubmitReasonApplies(
        ExaminationAttempt attempt,
        Examination examination,
        ExaminationSubmitReason reason)
    {
        var now = _clock.UtcNow;
        var examinationDeadline = AttemptDeadline.GetExaminationDeadline(attempt, examination);
        var applies = reason switch
        {
            ExaminationSubmitReason.DurationExpired => now >= examinationDeadline,
            ExaminationSubmitReason.DisconnectTimeout =>
                attempt.Status == ExaminationAttemptStatus.Disconnected &&
                attempt.DisconnectedAt is not null &&
                attempt.DisconnectedAt.Value.AddMinutes(examination.MaxDisconnectMinutes) < examinationDeadline &&
                now >= attempt.DisconnectedAt.Value.AddMinutes(examination.MaxDisconnectMinutes),
            ExaminationSubmitReason.MaxViolationsExceeded =>
                examination.MaxViolations > 0 &&
                attempt.ViolationCount >= examination.MaxViolations,
            ExaminationSubmitReason.ExaminationClosed => examination.Status == ExaminationStatus.Closed,
            _ => false
        };

        if (!applies)
        {
            throw new ConflictException("The requested auto-submit reason does not apply to this attempt.");
        }
    }

    private async Task<(ExaminationAttempt Attempt, Examination Examination, ExaminationQuestion Question)>
        GetAnswerContextAsync(
            Guid attemptId,
            Guid examinationQuestionId,
            CancellationToken cancellationToken)
    {
        var attempt = await GetOwnedAttemptAsync(attemptId, false, cancellationToken);
        EnsureStatus(attempt, ExaminationAttemptStatus.InProgress);
        var examination = await GetExaminationAsync(attempt.ExaminationId, true, cancellationToken);
        await EnsureAcceptedEnrollmentAsync(attempt.StudentId, examination.CourseId, cancellationToken);

        if (GetRemainingSeconds(attempt, examination, _clock.UtcNow) == 0)
        {
            throw new ConflictException("The examination attempt has expired.");
        }

        var question = examination.Questions.SingleOrDefault(item => item.Id == examinationQuestionId)
            ?? throw new NotFoundException("Examination question was not found.");
        return (attempt, examination, question);
    }

    private async Task<ExaminationAttempt> GetOwnedAttemptAsync(
        Guid attemptId,
        bool includeAnswers,
        CancellationToken cancellationToken)
    {
        ValidateId(attemptId, "Examination attempt id");
        var studentId = EnsureStudent();
        var attempt = await _attempts.GetByIdAsync(attemptId, includeAnswers, cancellationToken)
            ?? throw new NotFoundException("Examination attempt was not found.");

        if (attempt.StudentId != studentId)
        {
            throw new ForbiddenException("Students can only access their own examination attempts.");
        }

        return attempt;
    }

    private Guid EnsureStudent()
    {
        if (_currentUser.GetRequiredRole() != Role.Student)
        {
            throw new ForbiddenException("Only students can access examination attempts.");
        }

        return _currentUser.GetRequiredUserId();
    }

    private async Task EnsureAcceptedEnrollmentAsync(
        Guid studentId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var enrollment = await _enrollments.GetByStudentAndCourseAsync(studentId, courseId, cancellationToken);
        if (enrollment?.Status != EnrollmentStatus.Accepted)
        {
            throw new ForbiddenException("An accepted course enrollment is required to start this examination.");
        }
    }

    private async Task<Examination> GetExaminationAsync(
        Guid examinationId,
        bool includeDefinition,
        CancellationToken cancellationToken) =>
        await _examinations.GetByIdAsync(examinationId, includeDefinition, cancellationToken)
        ?? throw new NotFoundException("Examination was not found.");

    private static void EnsureAvailable(Examination examination, DateTime now)
    {
        if (examination.Status != ExaminationStatus.Published)
        {
            throw new ConflictException("Only published examinations can be started.");
        }

        if (now < examination.StartAt || now >= examination.DueAt)
        {
            throw new ConflictException("The examination is not currently available.");
        }
    }

    private static void EnsureActive(ExaminationAttempt attempt)
    {
        if (attempt.Status is not ExaminationAttemptStatus.InProgress and not ExaminationAttemptStatus.Disconnected)
        {
            throw new ConflictException("The examination attempt is no longer active.");
        }
    }

    private static void EnsureStatus(ExaminationAttempt attempt, ExaminationAttemptStatus status)
    {
        if (attempt.Status != status)
        {
            throw new ConflictException($"The examination attempt must be {status}.");
        }
    }

    private static int GetRemainingSeconds(
        ExaminationAttempt attempt,
        Examination examination,
        DateTime now)
    {
        var deadline = AttemptDeadline.GetExaminationDeadline(attempt, examination);
        return Math.Max(0, (int)Math.Ceiling((deadline - now).TotalSeconds));
    }

    private static IReadOnlyList<StudentExamQuestionDto> BuildStudentQuestions(
        Examination examination,
        Guid attemptId)
    {
        var questions = examination.Questions.ToList();
        if (examination.ShuffleQuestions)
        {
            ShuffleInPlace(questions, CreateSeed(attemptId, "questions"));
        }
        else
        {
            questions = questions.OrderBy(question => question.Order).ToList();
        }

        return questions
            .Select((question, questionIndex) =>
            {
                IReadOnlyList<StudentExamOptionDto> options = [];
                if (question.Type == QuestionType.MultipleChoice)
                {
                    var optionItems = question.MultipleChoiceOptions.ToList();
                    if (question.ShuffleOptions)
                    {
                        ShuffleInPlace(optionItems, CreateSeed(attemptId, $"options:{question.Id:N}"));
                    }
                    else
                    {
                        optionItems = optionItems.OrderBy(option => option.Order).ToList();
                    }

                    options = optionItems
                        .Select((option, optionIndex) =>
                            new StudentExamOptionDto(option.Id, option.Content, optionIndex))
                        .ToArray();
                }

                return new StudentExamQuestionDto(
                    question.Id,
                    question.Content,
                    question.Type,
                    questionIndex,
                    options);
            })
            .ToArray();
    }

    private static int CreateSeed(Guid attemptId, string salt)
    {
        var bytes = Encoding.UTF8.GetBytes($"{attemptId:N}:{salt}");
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToInt32(hash, 0);
    }

    private static void ShuffleInPlace<T>(IList<T> items, int seed)
    {
        var random = new Random(seed);
        for (var index = items.Count - 1; index > 0; index--)
        {
            var replacementIndex = random.Next(index + 1);
            (items[index], items[replacementIndex]) = (items[replacementIndex], items[index]);
        }
    }

    private static void ValidateFillInBlankAnswers(IReadOnlyList<StudentFillInBlankAnswerInput> answers)
    {
        if (answers.Any(answer =>
                answer.BlankOrder < 0 ||
                answer.AnswerText is null ||
                answer.AnswerText.Length > 1000))
        {
            throw new BadRequestException("Fill-in-blank answers must have a valid order and contain at most 1000 characters.");
        }

        if (answers.GroupBy(answer => answer.BlankOrder).Any(group => group.Count() > 1))
        {
            throw new ConflictException("Only one student answer is allowed for each blank.");
        }
    }

    private static void ValidateId(Guid id, string fieldName)
    {
        if (id == Guid.Empty)
        {
            throw new BadRequestException($"{fieldName} is required.");
        }
    }

    private static StudentAnswerDto Map(StudentAnswer answer, Examination examination)
    {
        var questionType = examination.Questions
            .Single(question => question.Id == answer.ExaminationQuestionId)
            .Type;
        return new StudentAnswerDto(
            answer.Id,
            answer.ExaminationQuestionId,
            questionType,
            answer.SelectedOptionId,
            answer.FillInBlankAnswers
                .OrderBy(item => item.BlankOrder)
                .Select(item => new StudentFillInBlankAnswerInput(item.BlankOrder, item.AnswerText))
                .ToArray(),
            answer.ModifiedAt);
    }

    private ExaminationAttemptDto Map(ExaminationAttempt attempt, Examination examination)
    {
        var revealScore = ShouldRevealScore(attempt, examination);
        return new(
            attempt.Id,
            attempt.ExaminationId,
            attempt.StudentId,
            attempt.Status,
            attempt.SubmitReason,
            attempt.StartedAt,
            attempt.SubmittedAt,
            attempt.DisconnectedAt,
            attempt.RemainingSeconds,
            attempt.LastActivityAt,
            attempt.ViolationCount,
            revealScore ? attempt.CorrectAnswers : null,
            revealScore ? attempt.TotalQuestions : null,
            revealScore ? attempt.Score : null);
    }

    private bool ShouldRevealScore(ExaminationAttempt attempt, Examination examination)
    {
        if (attempt.Status is ExaminationAttemptStatus.InProgress or ExaminationAttemptStatus.Disconnected)
        {
            return false;
        }

        if (examination.ShowScoreImmediately)
        {
            return true;
        }

        return examination.Status == ExaminationStatus.Closed || _clock.UtcNow >= examination.DueAt;
    }
}
