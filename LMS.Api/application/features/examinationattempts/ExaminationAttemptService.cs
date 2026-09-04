using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
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

        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            var examination = await GetExaminationAsync(examinationId, true, transactionCancellationToken);
            _ = await _courses.GetByIdAsync(examination.CourseId, false, transactionCancellationToken)
                ?? throw new NotFoundException("Course was not found.");
            await EnsureAcceptedEnrollmentAsync(studentId, examination.CourseId, transactionCancellationToken);
            EnsureAvailable(examination, _clock.UtcNow);

            if (examination.Questions.Count == 0)
            {
                throw new ConflictException("The examination has no questions.");
            }

            if (await _attempts.GetActiveAsync(examination.Id, studentId, transactionCancellationToken) is not null)
            {
                throw new ConflictException("An active examination attempt already exists.");
            }

            createdAttempt = new ExaminationAttempt(
                examination.Id,
                studentId,
                _clock.UtcNow,
                examination.Questions.Count);
            _attempts.Add(createdAttempt);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        return Map(createdAttempt!);
    }

    public async Task<IReadOnlyList<StudentExamQuestionDto>> GetQuestionsAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await GetOwnedAttemptAsync(attemptId, false, cancellationToken);
        var examination = await GetExaminationAsync(attempt.ExaminationId, true, cancellationToken);
        return examination.Questions
            .OrderBy(question => question.Order)
            .Select(question => new StudentExamQuestionDto(
                question.Id,
                question.Content,
                question.Type,
                question.Order,
                question.MultipleChoiceOptions
                    .OrderBy(option => option.Order)
                    .Select(option => new StudentExamOptionDto(option.Id, option.Content, option.Order))
                    .ToArray()))
            .ToArray();
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
        var attempt = await GetOwnedAttemptAsync(attemptId, true, cancellationToken);
        var examination = await GetExaminationAsync(attempt.ExaminationId, true, cancellationToken);
        EnsureStatus(attempt, ExaminationAttemptStatus.InProgress);
        var remainingSeconds = GetRemainingSeconds(attempt, examination, _clock.UtcNow);

        if (remainingSeconds == 0)
        {
            CompleteAndGrade(attempt, examination, ExaminationSubmitReason.DurationExpired, true);
        }
        else
        {
            attempt.Disconnect(_clock.UtcNow, remainingSeconds);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(attempt);
    }

    public async Task<ExaminationAttemptDto> ReconnectAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        var attempt = await GetOwnedAttemptAsync(attemptId, true, cancellationToken);
        var examination = await GetExaminationAsync(attempt.ExaminationId, true, cancellationToken);
        EnsureStatus(attempt, ExaminationAttemptStatus.Disconnected);
        var now = _clock.UtcNow;
        var disconnectDeadline = attempt.DisconnectedAt!.Value.AddMinutes(examination.MaxDisconnectMinutes);

        if (now >= disconnectDeadline)
        {
            CompleteAndGrade(attempt, examination, ExaminationSubmitReason.DisconnectTimeout, true);
        }
        else if (GetRemainingSeconds(attempt, examination, now) == 0)
        {
            CompleteAndGrade(attempt, examination, ExaminationSubmitReason.DurationExpired, true);
        }
        else
        {
            attempt.Reconnect(now);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(attempt);
    }

    public async Task<ExaminationAttemptDto> RecordViolationAsync(
        Guid attemptId,
        ExaminationViolationType violationType,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(violationType))
        {
            throw new BadRequestException("Examination violation type is invalid.");
        }

        var attempt = await GetOwnedAttemptAsync(attemptId, true, cancellationToken);
        var examination = await GetExaminationAsync(attempt.ExaminationId, true, cancellationToken);
        EnsureActive(attempt);
        var violation = new ExaminationViolation(attempt.Id, violationType, _clock.UtcNow);
        _attempts.AddViolation(violation);
        attempt.AddViolation();

        if (examination.MaxViolations > 0 && attempt.ViolationCount >= examination.MaxViolations)
        {
            CompleteAndGrade(attempt, examination, ExaminationSubmitReason.MaxViolationsExceeded, true);
        }
        else
        {
            attempt.TouchActivity(_clock.UtcNow);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(attempt);
    }

    public async Task<ExaminationAttemptDto> SubmitAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        ExaminationAttempt? submittedAttempt = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            submittedAttempt = await GetOwnedAttemptAsync(attemptId, true, transactionCancellationToken);
            var examination = await GetExaminationAsync(
                submittedAttempt.ExaminationId,
                true,
                transactionCancellationToken);
            EnsureActive(submittedAttempt);
            var expired = GetRemainingSeconds(submittedAttempt, examination, _clock.UtcNow) == 0;
            CompleteAndGrade(
                submittedAttempt,
                examination,
                expired ? ExaminationSubmitReason.DurationExpired : ExaminationSubmitReason.StudentSubmitted,
                expired);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);
        return Map(submittedAttempt!);
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
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            submittedAttempt = await _attempts.GetByIdAsync(attemptId, true, transactionCancellationToken)
                ?? throw new NotFoundException("Examination attempt was not found.");
            var examination = await GetExaminationAsync(
                submittedAttempt.ExaminationId,
                true,
                transactionCancellationToken);
            EnsureActive(submittedAttempt);
            CompleteAndGrade(submittedAttempt, examination, reason, true);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);
        return Map(submittedAttempt!);
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
        var durationDeadline = attempt.StartedAt.AddMinutes(examination.DurationMinutes);
        var deadline = durationDeadline < examination.DueAt ? durationDeadline : examination.DueAt;
        return Math.Max(0, (int)Math.Ceiling((deadline - now).TotalSeconds));
    }

    private static void CompleteAndGrade(
        ExaminationAttempt attempt,
        Examination examination,
        ExaminationSubmitReason reason,
        bool autoSubmit)
    {
        var answersByQuestion = attempt.Answers.ToDictionary(answer => answer.ExaminationQuestionId);
        var correctAnswers = examination.Questions.Count(question =>
            IsCorrect(question, answersByQuestion.GetValueOrDefault(question.Id)));
        attempt.SetResult(correctAnswers, examination.Questions.Count);

        if (autoSubmit)
        {
            attempt.AutoSubmit(reason);
        }
        else
        {
            attempt.Submit(reason);
        }
    }

    private static bool IsCorrect(ExaminationQuestion question, StudentAnswer? answer)
    {
        if (answer is null)
        {
            return false;
        }

        if (question.Type == QuestionType.MultipleChoice)
        {
            return question.CorrectOptionId is not null && answer.SelectedOptionId == question.CorrectOptionId;
        }

        var expectedAnswers = question.FillInBlankAnswerKeys
            .GroupBy(key => key.BlankOrder)
            .ToDictionary(
                group => group.Key,
                group => group.Select(key => key.ExpectedAnswer.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var studentAnswers = answer.FillInBlankAnswers.ToDictionary(
            item => item.BlankOrder,
            item => item.AnswerText.Trim());
        return expectedAnswers.Count > 0 &&
               expectedAnswers.All(expected =>
                   studentAnswers.TryGetValue(expected.Key, out var studentAnswer) &&
                   expected.Value.Contains(studentAnswer));
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

    private static ExaminationAttemptDto Map(ExaminationAttempt attempt) => new(
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
        attempt.CorrectAnswers,
        attempt.TotalQuestions,
        attempt.Score);
}
