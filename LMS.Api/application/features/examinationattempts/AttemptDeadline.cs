using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Enums.Examinations;

namespace LMS.Api.application.features.examinationattempts;

internal static class AttemptDeadline
{
    public static DateTime GetExaminationDeadline(
        ExaminationAttempt attempt,
        Examination examination)
    {
        var durationDeadline = attempt.StartedAt.AddMinutes(examination.DurationMinutes);
        return durationDeadline < examination.DueAt ? durationDeadline : examination.DueAt;
    }

    public static ExaminationSubmitReason GetExpiredReason(
        ExaminationAttempt attempt,
        Examination examination,
        DateTime now)
    {
        var examinationDeadline = GetExaminationDeadline(attempt, examination);
        if (attempt.Status != ExaminationAttemptStatus.Disconnected || attempt.DisconnectedAt is null)
        {
            return ExaminationSubmitReason.DurationExpired;
        }

        var disconnectDeadline = attempt.DisconnectedAt.Value.AddMinutes(examination.MaxDisconnectMinutes);
        if (examinationDeadline <= disconnectDeadline && now >= examinationDeadline)
        {
            return ExaminationSubmitReason.DurationExpired;
        }

        return ExaminationSubmitReason.DisconnectTimeout;
    }
}
