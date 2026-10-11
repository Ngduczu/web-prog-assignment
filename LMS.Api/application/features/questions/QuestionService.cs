using System.Net.Mail;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.mail;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.Enums.Questions;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.questions;

public sealed class QuestionService : IQuestionService
{
    private readonly IQuestionBankRepository _questionBanks;
    private readonly IQuestionRepository _questions;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionManager _transactionManager;
    private readonly ICurrentUser _currentUser;
    private readonly IMailSender _mailSender;

    public QuestionService(
        IQuestionBankRepository questionBanks,
        IQuestionRepository questions,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ITransactionManager transactionManager,
        ICurrentUser currentUser,
        IMailSender mailSender)
    {
        _mailSender = mailSender;
        _questionBanks = questionBanks;
        _questions = questions;
        _users = users;
        _unitOfWork = unitOfWork;
        _transactionManager = transactionManager;
        _currentUser = currentUser;
    }

    public async Task<QuestionBankDto> CreateQuestionBankAsync(
        CreateQuestionBankRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(request.TeacherId, "Teacher id");
        ValidateQuestionBank(request.Name, request.Description);
        var actorId = _currentUser.GetRequiredUserId();
        var actorRole = _currentUser.GetRequiredRole();

        if (!actorRole.IsAdminOrSystem() && (actorRole != Role.Teacher || actorId != request.TeacherId))
        {
            throw new ForbiddenException("Teachers can only create question banks assigned to themselves.");
        }

        var teacher = await _users.GetByIdAsync(request.TeacherId, cancellationToken)
            ?? throw new NotFoundException("Teacher was not found.");
        if (teacher.Role != Role.Teacher || teacher.Status != AccountStatus.Active)
        {
            throw new BadRequestException("Question bank owner must be an active teacher account.");
        }

        var questionBank = new QuestionBank(
            teacher.Id,
            request.Name.Trim(),
            NormalizeOptional(request.Description),
            actorId);
        _questionBanks.Add(questionBank);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(questionBank);
    }

    public async Task<QuestionBankDto> UpdateQuestionBankAsync(
        Guid questionBankId,
        UpdateQuestionBankRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(questionBankId, "Question bank id");
        ValidateQuestionBank(request.Name, request.Description);
        var questionBank = await GetManagedQuestionBankAsync(questionBankId, cancellationToken);
        questionBank.SetName(request.Name.Trim());
        questionBank.SetDescription(NormalizeOptional(request.Description));
        questionBank.SetModifiedBy(_currentUser.GetRequiredUserId());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(questionBank);
    }

    public async Task<IReadOnlyList<QuestionBankDto>> ListQuestionBanksAsync(
        Guid? teacherId = null,
        CancellationToken cancellationToken = default)
    {
        var actorId = _currentUser.GetRequiredUserId();
        var actorRole = _currentUser.GetRequiredRole();
        var ownerId = teacherId ?? actorId;

        if (!actorRole.IsAdminOrSystem() && (actorRole != Role.Teacher || ownerId != actorId))
        {
            throw new ForbiddenException("Teachers can only view their own question banks.");
        }

        var questionBanks = await _questionBanks.ListByTeacherAsync(ownerId, cancellationToken);
        return questionBanks.Select(Map).ToArray();
    }

    public async Task DeleteQuestionBankAsync(
        Guid questionBankId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(questionBankId, "Question bank id");
        var questionBank = await GetManagedQuestionBankAsync(questionBankId, cancellationToken);
        if (await _questionBanks.IsReferencedByExaminationAsync(questionBankId, cancellationToken))
        {
            throw new ConflictException("Question bank is referenced by an examination and cannot be deleted.");
        }

        _questionBanks.Remove(questionBank);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<QuestionBankDto> ShareQuestionBankAsync(
        Guid questionBankId,
        ShareQuestionBankRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(questionBankId, "Question bank id");
        var email = request.RecipientEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 255 ||
            !MailAddress.TryCreate(email, out var address) || address.Address != email)
        {
            throw new BadRequestException("Recipient email address is invalid.");
        }

        var actorId = _currentUser.GetRequiredUserId();
        var source = await _questionBanks.GetByIdAsync(questionBankId, cancellationToken)
            ?? throw new NotFoundException("Question bank was not found.");
        if (_currentUser.GetRequiredRole() != Role.Teacher || source.TeacherId != actorId)
        {
            throw new ForbiddenException("Only the question bank owner can share it.");
        }

        var recipient = await _users.GetByEmailAsync(email, cancellationToken);
        if (recipient is null || recipient.Role != Role.Teacher || recipient.Status != AccountStatus.Active)
        {
            throw new NotFoundException("An active teacher with this email was not found.");
        }

        if (recipient.Id == actorId)
        {
            throw new BadRequestException("You cannot share a question bank with yourself.");
        }

        var sender = await _users.GetByIdAsync(actorId, cancellationToken)
            ?? throw new NotFoundException("User was not found.");
        var sourceQuestions = await _questions.ListByQuestionBankAsync(questionBankId, cancellationToken);

        var note = $"Shared by {sender.FullName} ({sender.Email}).";
        var description = string.IsNullOrWhiteSpace(source.Description) ? note : $"{note}\n{source.Description}";
        var copy = new QuestionBank(
            recipient.Id,
            source.Name,
            description.Length > 2000 ? description[..2000] : description,
            actorId);

        await _transactionManager.ExecuteAsync(async transactionCancellationToken =>
        {
            _questionBanks.Add(copy);
            var correctOptions = new List<(Question Question, MultipleChoiceOption Option)>();
            foreach (var original in sourceQuestions)
            {
                var question = new Question(
                    copy.Id,
                    original.Content,
                    original.Type,
                    original.Difficulty,
                    original.ShuffleOptions,
                    actorId);
                foreach (var option in original.MultipleChoiceOptions)
                {
                    var optionCopy = new MultipleChoiceOption(question.Id, option.Content, option.Order);
                    question.MultipleChoiceOptions.Add(optionCopy);
                    if (option.Id == original.CorrectOptionId)
                    {
                        correctOptions.Add((question, optionCopy));
                    }
                }

                foreach (var answer in original.FillInBlankAnswerKeys)
                {
                    question.FillInBlankAnswerKeys.Add(new FillInBlankAnswerKey(
                        question.Id,
                        answer.BlankOrder,
                        answer.ExpectedAnswer));
                }

                _questions.Add(question);
            }

            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            foreach (var (question, option) in correctOptions)
            {
                question.SetCorrectOption(option.Id);
            }

            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        try
        {
            await _mailSender.SendQuestionBankSharedAsync(
                recipient.Email,
                recipient.FullName,
                sender.FullName,
                source.Name,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The copy is already committed; a notification failure must not fail the share.
        }

        return Map(copy);
    }

    public async Task<QuestionDto> CreateMultipleChoiceAsync(
        Guid questionBankId,
        CreateMultipleChoiceQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(questionBankId, "Question bank id");
        ValidateQuestion(request.Content, request.Difficulty);
        ValidateMultipleChoiceOptions(request.Options);
        _ = await GetManagedQuestionBankAsync(questionBankId, cancellationToken);
        var actorId = _currentUser.GetRequiredUserId();
        Question? result = null;

        await _transactionManager.ExecuteAsync(async transactionCancellationToken =>
        {
            var question = new Question(
                questionBankId,
                request.Content.Trim(),
                QuestionType.MultipleChoice,
                request.Difficulty,
                request.ShuffleOptions,
                actorId);
            MultipleChoiceOption? correctOption = null;
            for (var index = 0; index < request.Options.Count; index++)
            {
                var input = request.Options[index];
                var option = new MultipleChoiceOption(question.Id, input.Content.Trim(), index);
                question.MultipleChoiceOptions.Add(option);
                if (input.IsCorrect)
                {
                    correctOption = option;
                }
            }

            _questions.Add(question);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            question.SetCorrectOption(correctOption!.Id);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            result = question;
        }, cancellationToken);

        return Map(result!);
    }

    public async Task<QuestionDto> CreateFillInBlankAsync(
        Guid questionBankId,
        CreateFillInBlankQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(questionBankId, "Question bank id");
        ValidateQuestion(request.Content, request.Difficulty);
        ValidateFillInBlankAnswers(request.Answers);
        _ = await GetManagedQuestionBankAsync(questionBankId, cancellationToken);
        var actorId = _currentUser.GetRequiredUserId();
        var question = new Question(
            questionBankId,
            request.Content.Trim(),
            QuestionType.FillInBlank,
            request.Difficulty,
            false,
            actorId);

        foreach (var answer in request.Answers)
        {
            question.FillInBlankAnswerKeys.Add(new FillInBlankAnswerKey(
                question.Id,
                answer.BlankOrder,
                answer.ExpectedAnswer.Trim()));
        }

        _questions.Add(question);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(question);
    }

    public async Task<IReadOnlyList<QuestionDto>> ListQuestionsAsync(
        Guid questionBankId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(questionBankId, "Question bank id");
        _ = await GetManagedQuestionBankAsync(questionBankId, cancellationToken);
        var questions = await _questions.ListByQuestionBankAsync(questionBankId, cancellationToken);
        return questions.Select(Map).ToArray();
    }

    public async Task<QuestionDto> UpdateQuestionAsync(
        Guid questionId,
        UpdateQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(questionId, "Question id");
        ValidateQuestion(request.Content, request.Difficulty);
        if (request.Options is not null)
        {
            ValidateMultipleChoiceOptions(request.Options);
        }

        if (request.Answers is not null)
        {
            ValidateFillInBlankAnswers(request.Answers);
        }

        var question = await _questions.GetByIdAsync(questionId, true, cancellationToken)
            ?? throw new NotFoundException("Question was not found.");
        _ = await GetManagedQuestionBankAsync(question.QuestionBankId, cancellationToken);
        if (request.Options is not null && question.Type != QuestionType.MultipleChoice)
        {
            throw new BadRequestException("Options can only be updated on a multiple-choice question.");
        }

        if (request.Answers is not null && question.Type != QuestionType.FillInBlank)
        {
            throw new BadRequestException("Answers can only be updated on a fill-in-blank question.");
        }

        var actorId = _currentUser.GetRequiredUserId();
        await _transactionManager.ExecuteAsync(async transactionCancellationToken =>
        {
            question.SetContent(request.Content.Trim());
            question.SetDifficulty(request.Difficulty);
            if (request.ShuffleOptions is not null && question.Type == QuestionType.MultipleChoice)
            {
                question.SetShuffleOptions(request.ShuffleOptions.Value);
            }

            question.SetModifiedBy(actorId);

            // Removed rows are saved first so unique (question, order) indexes never collide with new rows.
            if (request.Options is not null)
            {
                question.SetCorrectOption(null);
                question.MultipleChoiceOptions.Clear();
                await _unitOfWork.SaveChangesAsync(transactionCancellationToken);

                MultipleChoiceOption? correctOption = null;
                for (var index = 0; index < request.Options.Count; index++)
                {
                    var input = request.Options[index];
                    var option = new MultipleChoiceOption(question.Id, input.Content.Trim(), index);
                    question.MultipleChoiceOptions.Add(option);
                    if (input.IsCorrect)
                    {
                        correctOption = option;
                    }
                }

                await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
                question.SetCorrectOption(correctOption!.Id);
            }
            else if (request.Answers is not null)
            {
                question.FillInBlankAnswerKeys.Clear();
                await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
                foreach (var answer in request.Answers)
                {
                    question.FillInBlankAnswerKeys.Add(new FillInBlankAnswerKey(
                        question.Id,
                        answer.BlankOrder,
                        answer.ExpectedAnswer.Trim()));
                }
            }

            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
        }, cancellationToken);

        return Map(question);
    }

    public async Task DeleteQuestionAsync(
        Guid questionId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(questionId, "Question id");
        var question = await _questions.GetByIdAsync(questionId, true, cancellationToken)
            ?? throw new NotFoundException("Question was not found.");
        _ = await GetManagedQuestionBankAsync(question.QuestionBankId, cancellationToken);
        _questions.Remove(question);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<QuestionBank> GetManagedQuestionBankAsync(
        Guid questionBankId,
        CancellationToken cancellationToken)
    {
        var questionBank = await _questionBanks.GetByIdAsync(questionBankId, cancellationToken)
            ?? throw new NotFoundException("Question bank was not found.");
        var actorId = _currentUser.GetRequiredUserId();
        var actorRole = _currentUser.GetRequiredRole();

        if (!actorRole.IsAdminOrSystem() && (actorRole != Role.Teacher || questionBank.TeacherId != actorId))
        {
            throw new ForbiddenException("Only the question bank owner or an administrator can manage it.");
        }

        return questionBank;
    }

    private static void ValidateQuestionBank(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new BadRequestException("Question bank name must contain between 1 and 200 characters.");
        }

        if (description?.Trim().Length > 2000)
        {
            throw new BadRequestException("Question bank description cannot exceed 2000 characters.");
        }
    }

    private static void ValidateQuestion(string content, QuestionDifficulty difficulty)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new BadRequestException("Question content is required.");
        }

        if (!Enum.IsDefined(difficulty))
        {
            throw new BadRequestException("Question difficulty is invalid.");
        }
    }

    private static void ValidateMultipleChoiceOptions(IReadOnlyList<MultipleChoiceOptionInput> options)
    {
        if (options.Count < 2)
        {
            throw new BadRequestException("A multiple-choice question requires at least two options.");
        }

        if (options.Count(option => option.IsCorrect) != 1)
        {
            throw new BadRequestException("A multiple-choice question requires exactly one correct option.");
        }

        if (options.Any(option => string.IsNullOrWhiteSpace(option.Content)))
        {
            throw new BadRequestException("Multiple-choice option content is required.");
        }
    }

    private static void ValidateFillInBlankAnswers(IReadOnlyList<FillInBlankAnswerInput> answers)
    {
        if (answers.Count == 0)
        {
            throw new BadRequestException("A fill-in-blank question requires at least one answer.");
        }

        if (answers.Any(answer =>
                answer.BlankOrder < 0 ||
                string.IsNullOrWhiteSpace(answer.ExpectedAnswer) ||
                answer.ExpectedAnswer.Trim().Length > 1000))
        {
            throw new BadRequestException("Fill-in-blank answers must have a valid order and contain at most 1000 characters.");
        }

        if (answers.GroupBy(answer => new { answer.BlankOrder, Answer = answer.ExpectedAnswer.Trim() }).Any(group => group.Count() > 1))
        {
            throw new ConflictException("Duplicate expected answers for the same blank are not allowed.");
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

    private static QuestionBankDto Map(QuestionBank questionBank) => new(
        questionBank.Id,
        questionBank.TeacherId,
        questionBank.Name,
        questionBank.Description,
        questionBank.CreatedAt,
        questionBank.ModifiedAt);

    private static QuestionDto Map(Question question) => new(
        question.Id,
        question.QuestionBankId,
        question.Content,
        question.Type,
        question.Difficulty,
        question.ShuffleOptions,
        question.MultipleChoiceOptions
            .OrderBy(option => option.Order)
            .Select(option => new MultipleChoiceOptionDto(
                option.Id,
                option.Content,
                option.Order,
                option.Id == question.CorrectOptionId))
            .ToArray(),
        question.FillInBlankAnswerKeys
            .OrderBy(answer => answer.BlankOrder)
            .ThenBy(answer => answer.ExpectedAnswer)
            .Select(answer => new FillInBlankAnswerDto(answer.Id, answer.BlankOrder, answer.ExpectedAnswer))
            .ToArray(),
        question.CreatedAt,
        question.ModifiedAt);
}
