namespace LMS.Api.application.abstractions.mail;

public interface IMailSender
{
    Task SendEmailVerificationAsync(
        string email,
        string recipientName,
        string verificationCode,
        CancellationToken cancellationToken = default);
}
