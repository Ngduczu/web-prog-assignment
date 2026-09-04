namespace LMS.Api.domain.Enums.Examinations;

public enum ExaminationSubmitReason
{
    StudentSubmitted = 0,
    DurationExpired = 1,
    DisconnectTimeout = 2,
    MaxViolationsExceeded = 3
}
