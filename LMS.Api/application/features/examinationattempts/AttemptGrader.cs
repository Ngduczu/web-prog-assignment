using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.Enums.Questions;
using LMS.Api.domain.exceptions;

namespace LMS.Api.application.features.examinationattempts;

internal static class AttemptGrader
{
    public static void CompleteAndGrade(
        ExaminationAttempt attempt,
        Examination examination,
        ExaminationSubmitReason reason,
        bool autoSubmit)
    {
        if (attempt.Status is ExaminationAttemptStatus.Submitted or ExaminationAttemptStatus.AutoSubmitted)
        {
            throw new ConflictException("The examination attempt has already been submitted.");
        }

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
}
