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

        await SendAsync(
            email,
            recipientName,
            "Verify your LMS account",
            BuildCodeEmailHtml(
                recipientName,
                "Verify your email address",
                "Use the verification code below to activate your LMS account.",
                verificationCode),
            cancellationToken);
    }

    public Task SendPasswordResetCodeAsync(string email, string recipientName, string resetCode, CancellationToken cancellationToken = default) =>
        SendAsync(
            email,
            recipientName,
            "Reset your LMS password",
            BuildCodeEmailHtml(
                recipientName,
                "Reset your password",
                "Use the code below to reset your LMS password.",
                resetCode),
            cancellationToken);

    private async Task SendAsync(string email, string recipientName, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password) || string.IsNullOrWhiteSpace(_options.FromEmail))
            throw new InvalidOperationException("Mail credentials are not configured.");
        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromEmail, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(email, recipientName));
        using var smtpClient = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.EnableSsl, Credentials = new NetworkCredential(_options.Username, _options.Password) };
        await smtpClient.SendMailAsync(message, cancellationToken);
    }

        private static string BuildCodeEmailHtml(string recipientName, string heading, string description, string code)
        {
                var safeName = WebUtility.HtmlEncode(recipientName);
                var safeHeading = WebUtility.HtmlEncode(heading);
                var safeDescription = WebUtility.HtmlEncode(description);
                var safeCode = WebUtility.HtmlEncode(code);

                return $"""
                        <!doctype html>
                        <html lang="en">
                            <body style="margin:0;background:#f4f7fb;color:#172033;font-family:Arial,Helvetica,sans-serif;">
                                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 12px;background:#f4f7fb;">
                                    <tr><td align="center">
                                        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border:1px solid #e4e9f2;border-radius:16px;overflow:hidden;">
                                            <tr><td style="padding:24px 32px;background:#173b67;color:#ffffff;font-size:22px;font-weight:700;">LMS</td></tr>
                                            <tr><td style="padding:32px;">
                                                <p style="margin:0 0 16px;font-size:16px;">Hello {safeName},</p>
                                                <h1 style="margin:0 0 12px;font-size:25px;line-height:1.25;color:#173b67;">{safeHeading}</h1>
                                                <p style="margin:0 0 24px;font-size:15px;line-height:1.6;color:#536174;">{safeDescription}</p>
                                                <div style="padding:18px;text-align:center;background:#edf4fb;border:1px dashed #5c8fbd;border-radius:10px;">
                                                    <span style="font-size:32px;letter-spacing:6px;font-weight:700;color:#173b67;">{safeCode}</span>
                                                </div>
                                                <p style="margin:24px 0 0;font-size:13px;line-height:1.6;color:#718096;">This code expires in 15 minutes. If you did not request this email, you can safely ignore it.</p>
                                            </td></tr>
                                            <tr><td style="padding:18px 32px;background:#f8fafc;color:#8a96a8;font-size:12px;">This is an automated message from LMS. Please do not reply.</td></tr>
                                        </table>
                                    </td></tr>
                                </table>
                            </body>
                        </html>
                        """;
        }
}
