using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.courses;
using LMS.Api.application.features.examinationattempts;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.Enums.Questions;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.examinations;

public sealed class ExaminationService : IExaminationService
{
    private readonly IExaminationRepository _examinations;
    private readonly ICourseRepository _courses;
    private readonly IEnrollmentRepository _enrollments;
    private readonly IQuestionBankRepository _questionBanks;
    private readonly IQuestionRepository _questions;
    private readonly IExaminationAttemptRepository _attempts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionManager _transactionManager;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public ExaminationService(
        IExaminationRepository examinations,
        ICourseRepository courses,
        IEnrollmentRepository enrollments,
        IQuestionBankRepository questionBanks,
        IQuestionRepository questions,
        IExaminationAttemptRepository attempts,
        IUnitOfWork unitOfWork,
        ITransactionManager transactionManager,
        ICurrentUser currentUser,
        IClock clock)
    {
        _examinations = examinations;
        _courses = courses;
        _enrollments = enrollments;
        _questionBanks = questionBanks;
        _questions = questions;
        _attempts = attempts;
        _unitOfWork = unitOfWork;
        _transactionManager = transactionManager;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<ExaminationDto> CreateAsync(
        Guid courseId,
        CreateExaminationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        ValidateDefinition(
            request.Title,
            request.Description,
            request.StartAt,
            request.DueAt,
            request.DurationMinutes,
            request.Security);
        ValidateSelectionRules(request.QuestionSelectionRules);

        Examination? examination = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            var course = await GetCourseAsync(courseId, transactionCancellationToken);
            var actorId = CourseAuthorization.EnsureCanManage(_currentUser, course);
            examination = new Examination(
                course.Id,
                request.Title.Trim(),
                NormalizeOptional(request.Description),
                request.StartAt,
                request.DueAt,
                request.DurationMinutes,
                actorId);
            examination.SetDisplayOptions(request.ShuffleQuestions, request.ShowScoreImmediately);
            ApplySecurity(examination, request.Security);

            var questionBankIds = request.QuestionSelectionRules
                .Select(rule => rule.QuestionBankId)
                .Distinct()
                .ToArray();
            var questionBanks = await _questionBanks.ListByIdsAsync(
                questionBankIds,
                transactionCancellationToken);
            if (questionBanks.Count != questionBankIds.Length)
            {
                throw new NotFoundException("A selected question bank was not found.");
            }

            var questionBanksById = questionBanks.ToDictionary(questionBank => questionBank.Id);
            if (questionBanks.Any(questionBank => questionBank.TeacherId != course.TeacherId))
            {
                throw new ForbiddenException(
                    "Examinations can only use question banks owned by the course teacher.");
            }

            var sourceQuestions = await _questions.ListByQuestionBankIdsAsync(
                questionBankIds,
                transactionCancellationToken);
            var questionsByBankId = sourceQuestions
                .GroupBy(question => question.QuestionBankId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var snapshotCorrectOptions = new List<(ExaminationQuestion Question, Guid CorrectOptionId)>();
            var questionOrder = 0;

            foreach (var ruleInput in request.QuestionSelectionRules)
            {
                var questionBank = questionBanksById[ruleInput.QuestionBankId];
                var candidates = questionsByBankId.GetValueOrDefault(ruleInput.QuestionBankId, [])
                    .Where(question => question.Difficulty == ruleInput.Difficulty)
                    .ToList();

                if (candidates.Count < ruleInput.QuestionCount)
                {
                    throw new ConflictException(
                        $"Question bank '{questionBank.Name}' does not contain enough {ruleInput.Difficulty} questions.");
                }

                examination.QuestionSelectionRules.Add(new ExaminationQuestionSelectionRule(
                    examination.Id,
                    questionBank.Id,
                    ruleInput.Difficulty,
                    ruleInput.QuestionCount));

                Shuffle(candidates);
                foreach (var sourceQuestion in candidates.Take(ruleInput.QuestionCount))
                {
                    var snapshot = CreateSnapshot(
                        examination.Id,
                        sourceQuestion,
                        questionOrder++,
                        snapshotCorrectOptions);
                    examination.Questions.Add(snapshot);
                }
            }

            _examinations.Add(examination);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);

            foreach (var (question, correctOptionId) in snapshotCorrectOptions)
            {
                question.SetCorrectOption(correctOptionId);
            }

            if (snapshotCorrectOptions.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            }
        }, cancellationToken);

        return Map(examination!);
    }

    public async Task<ExaminationDto> GetForManagementAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationId, "Examination id");
        var examination = await GetExaminationAsync(examinationId, true, cancellationToken);
        await EnsureCanManageAsync(examination, cancellationToken);
        return Map(examination);
    }

    public async Task<StudentExaminationDto> GetForStudentAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationId, "Examination id");
        var examination = await GetExaminationAsync(examinationId, true, cancellationToken);
        var course = await GetCourseAsync(examination.CourseId, cancellationToken);
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);

        if (CourseAuthorization.IsCourseManager(_currentUser, course))
        {
            return MapForStudent(examination);
        }

        if (examination.Status == ExaminationStatus.Draft)
        {
            throw new NotFoundException("Examination was not found.");
        }

        return MapForStudent(examination);
    }

    public async Task<IReadOnlyList<ExaminationDto>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        var course = await GetCourseAsync(courseId, cancellationToken);
        CourseAuthorization.EnsureCanManage(_currentUser, course);
        var examinations = await _examinations.ListByCourseAsync(courseId, cancellationToken);
        return examinations.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<StudentExaminationDto>> ListAvailableForStudentAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        var course = await GetCourseAsync(courseId, cancellationToken);
        await CourseAuthorization.EnsureCanViewContentAsync(
            _currentUser,
            course,
            _enrollments,
            cancellationToken);
        var examinations = await _examinations.ListByCourseAsync(courseId, cancellationToken);

        if (!CourseAuthorization.IsCourseManager(_currentUser, course))
        {
            examinations = examinations
                .Where(examination => examination.Status != ExaminationStatus.Draft)
                .ToArray();
        }

        return examinations.Select(MapForStudent).ToArray();
    }

    public async Task<ExaminationDto> UpdateAsync(
        Guid examinationId,
        UpdateExaminationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationId, "Examination id");
        ValidateDefinition(
            request.Title,
            request.Description,
            request.StartAt,
            request.DueAt,
            request.DurationMinutes,
            request.Security);
        var examination = await GetExaminationAsync(examinationId, true, cancellationToken);
        var actorId = await EnsureCanManageAsync(examination, cancellationToken);

        if (examination.Status != ExaminationStatus.Draft)
        {
            throw new ConflictException("Only draft examinations can be updated.");
        }

        examination.SetTitle(request.Title.Trim());
        examination.SetDescription(NormalizeOptional(request.Description));
        examination.SetSchedule(request.StartAt, request.DueAt, request.DurationMinutes);
        examination.SetDisplayOptions(request.ShuffleQuestions, request.ShowScoreImmediately);
        ApplySecurity(examination, request.Security);
        examination.SetModifiedBy(actorId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(examination);
    }

    public async Task<ExaminationDto> PublishAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationId, "Examination id");
        var examination = await GetExaminationAsync(examinationId, true, cancellationToken);
        var actorId = await EnsureCanManageAsync(examination, cancellationToken);

        if (examination.Status != ExaminationStatus.Draft)
        {
            throw new ConflictException("Only draft examinations can be published.");
        }

        if (examination.Questions.Count == 0)
        {
            throw new ConflictException("An examination requires at least one snapshotted question before publishing.");
        }

        if (examination.DueAt <= _clock.UtcNow)
        {
            throw new ConflictException("An examination cannot be published after its due time.");
        }

        examination.SetStatus(ExaminationStatus.Published);
        examination.SetModifiedBy(actorId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(examination);
    }

    public async Task<ExaminationDto> CloseAsync(
        Guid examinationId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(examinationId, "Examination id");
        Examination? examination = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            examination = await GetExaminationAsync(examinationId, true, transactionCancellationToken);
            var actorId = await EnsureCanManageAsync(examination, transactionCancellationToken);

            if (examination.Status != ExaminationStatus.Published)
            {
                throw new ConflictException("Only published examinations can be closed.");
            }

            var activeAttempts = await _attempts.ListActiveByExaminationAsync(
                examination.Id,
                transactionCancellationToken);
            foreach (var attempt in activeAttempts)
            {
                AttemptGrader.CompleteAndGrade(
                    attempt,
                    examination,
                    ExaminationSubmitReason.ExaminationClosed,
                    autoSubmit: true);
            }

            examination.SetStatus(ExaminationStatus.Closed);
            examination.SetModifiedBy(actorId);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);
        return Map(examination!);
    }

    private async Task<Guid> EnsureCanManageAsync(
        Examination examination,
        CancellationToken cancellationToken)
    {
        var course = await GetCourseAsync(examination.CourseId, cancellationToken);
        return CourseAuthorization.EnsureCanManage(_currentUser, course);
    }

    private async Task<Course> GetCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        await _courses.GetByIdAsync(courseId, false, cancellationToken)
        ?? throw new NotFoundException("Course was not found.");

    private async Task<Examination> GetExaminationAsync(
        Guid examinationId,
        bool includeDefinition,
        CancellationToken cancellationToken) =>
        await _examinations.GetByIdAsync(examinationId, includeDefinition, cancellationToken)
        ?? throw new NotFoundException("Examination was not found.");

    private static ExaminationQuestion CreateSnapshot(
        Guid examinationId,
        Question source,
        int order,
        ICollection<(ExaminationQuestion Question, Guid CorrectOptionId)> correctOptions)
    {
        var snapshot = new ExaminationQuestion(
            examinationId,
            source.Id,
            source.Content,
            source.Type,
            source.Difficulty,
            source.ShuffleOptions,
            order);

        if (source.Type == QuestionType.MultipleChoice)
        {
            var sourceCorrectOption = source.MultipleChoiceOptions
                .SingleOrDefault(option => option.Id == source.CorrectOptionId)
                ?? throw new ConflictException("A selected multiple-choice question has no valid correct option.");
            ExaminationMultipleChoiceOption? snapshotCorrectOption = null;

            foreach (var sourceOption in source.MultipleChoiceOptions.OrderBy(option => option.Order))
            {
                var snapshotOption = new ExaminationMultipleChoiceOption(
                    snapshot.Id,
                    sourceOption.Content,
                    sourceOption.Order);
                snapshot.MultipleChoiceOptions.Add(snapshotOption);
                if (sourceOption.Id == sourceCorrectOption.Id)
                {
                    snapshotCorrectOption = snapshotOption;
                }
            }

            correctOptions.Add((snapshot, snapshotCorrectOption!.Id));
        }
        else
        {
            if (source.FillInBlankAnswerKeys.Count == 0)
            {
                throw new ConflictException("A selected fill-in-blank question has no answer key.");
            }

            foreach (var sourceAnswer in source.FillInBlankAnswerKeys)
            {
                snapshot.FillInBlankAnswerKeys.Add(new ExaminationFillInBlankAnswerKey(
                    snapshot.Id,
                    sourceAnswer.BlankOrder,
                    sourceAnswer.ExpectedAnswer));
            }
        }

        return snapshot;
    }

    private static void ValidateDefinition(
        string title,
        string? description,
        DateTime startAt,
        DateTime dueAt,
        int durationMinutes,
        ExaminationSecuritySettings security)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            throw new BadRequestException("Examination title must contain between 1 and 200 characters.");
        }

        if (description?.Trim().Length > 4000)
        {
            throw new BadRequestException("Examination description cannot exceed 4000 characters.");
        }

        if (startAt.Kind != DateTimeKind.Utc || dueAt.Kind != DateTimeKind.Utc)
        {
            throw new BadRequestException("Examination start and due times must use UTC.");
        }

        if (dueAt <= startAt)
        {
            throw new BadRequestException("Examination due time must be after its start time.");
        }

        if (durationMinutes <= 0 || durationMinutes > (dueAt - startAt).TotalMinutes)
        {
            throw new BadRequestException("Examination duration must fit within its availability window.");
        }

        if (security.FullscreenGraceSeconds < 0 ||
            security.MaxViolations < 0 ||
            security.MaxDisconnectMinutes < 0)
        {
            throw new BadRequestException("Examination security limits cannot be negative.");
        }
    }

    private static void ValidateSelectionRules(IReadOnlyList<QuestionSelectionRuleInput> rules)
    {
        if (rules.Count == 0)
        {
            throw new BadRequestException("At least one question selection rule is required.");
        }

        if (rules.Any(rule =>
                rule.QuestionBankId == Guid.Empty ||
                !Enum.IsDefined(rule.Difficulty) ||
                rule.QuestionCount <= 0))
        {
            throw new BadRequestException("Question selection rules are invalid.");
        }

        if (rules.GroupBy(rule => new { rule.QuestionBankId, rule.Difficulty }).Any(group => group.Count() > 1))
        {
            throw new ConflictException("Duplicate question selection rules are not allowed.");
        }
    }

    private static void ApplySecurity(Examination examination, ExaminationSecuritySettings security)
    {
        examination.SetAntiCheat(
            security.RequireFullscreen,
            security.FullscreenGraceSeconds,
            security.MaxViolations,
            security.BlockCopyPaste,
            security.BlockRightClick,
            security.DetectTabChange,
            security.DetectDevTools,
            security.MaxDisconnectMinutes);
    }

    private static void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var replacementIndex = Random.Shared.Next(index + 1);
            (items[index], items[replacementIndex]) = (items[replacementIndex], items[index]);
        }
    }

    private static void ValidateId(Guid id, string fieldName)
    {
        if (id == Guid.Empty)
        {
            throw new BadRequestException($"{fieldName} is required.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExaminationDto Map(Examination examination) => new(
        examination.Id,
        examination.CourseId,
        examination.Title,
        examination.Description,
        examination.Status,
        examination.StartAt,
        examination.DueAt,
        examination.DurationMinutes,
        examination.ShuffleQuestions,
        examination.ShowScoreImmediately,
        MapSecurity(examination),
        examination.QuestionSelectionRules
            .Select(rule => new QuestionSelectionRuleDto(
                rule.Id,
                rule.QuestionBankId,
                rule.Difficulty,
                rule.QuestionCount))
            .ToArray(),
        examination.Questions.Count,
        examination.CreatedAt,
        examination.ModifiedAt);

    private static StudentExaminationDto MapForStudent(Examination examination) => new(
        examination.Id,
        examination.CourseId,
        examination.Title,
        examination.Description,
        examination.Status,
        examination.StartAt,
        examination.DueAt,
        examination.DurationMinutes,
        examination.ShuffleQuestions,
        examination.ShowScoreImmediately,
        MapSecurity(examination),
        examination.Questions.Count,
        examination.CreatedAt,
        examination.ModifiedAt);

    private static ExaminationSecuritySettings MapSecurity(Examination examination) => new(
        examination.RequireFullscreen,
        examination.FullscreenGraceSeconds,
        examination.MaxViolations,
        examination.BlockCopyPaste,
        examination.BlockRightClick,
        examination.DetectTabChange,
        examination.DetectDevTools,
        examination.MaxDisconnectMinutes);
}
