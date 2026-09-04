using LMS.Api.application.abstractions.mail;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace LMS.Api.infrastructure.mail.Gmail;

public sealed class GmailService : IMailSender
{
    private readonly MailOptions _options;

    public GmailService(IOptions<MailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendEmailVerificationAsync(
        string email,
        string recipientName,
        string verificationCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password) ||
            string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            throw new InvalidOperationException("Mail credentials are not configured.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = "Verify your LMS account",
            Body = $"Hello {recipientName},\n\nYour LMS verification code is: {verificationCode}\n\nThis code expires in 15 minutes.",
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(email, recipientName));

        using var smtpClient = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = new NetworkCredential(_options.Username, _options.Password)
        };
        await smtpClient.SendMailAsync(message, cancellationToken);
    }

    public Task SendPasswordResetCodeAsync(string email, string recipientName, string resetCode, CancellationToken cancellationToken = default) =>
        SendAsync(email, recipientName, "Reset your LMS password", $"Hello {recipientName},\n\nYour LMS password reset code is: {resetCode}\n\nThis code expires in 15 minutes.", cancellationToken);

    private async Task SendAsync(string email, string recipientName, string subject, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password) || string.IsNullOrWhiteSpace(_options.FromEmail))
            throw new InvalidOperationException("Mail credentials are not configured.");
        using var message = new MailMessage { From = new MailAddress(_options.FromEmail, _options.FromName), Subject = subject, Body = body, IsBodyHtml = false };
        message.To.Add(new MailAddress(email, recipientName));
        using var smtpClient = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.EnableSsl, Credentials = new NetworkCredential(_options.Username, _options.Password) };
        await smtpClient.SendMailAsync(message, cancellationToken);
    }
}
