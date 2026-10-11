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

    public Task SendQuestionBankSharedAsync(string email, string recipientName, string senderName, string questionBankName, CancellationToken cancellationToken = default) =>
        SendAsync(
            email,
            recipientName,
            "A question bank was shared with you",
            BuildBrandedEmailHtml(
                recipientName,
                $"""
                <h1 style="margin:0 0 12px;font-size:25px;line-height:1.25;color:#245b3d;">A question bank was shared with you</h1>
                <p style="margin:0;font-size:15px;line-height:1.7;color:#53665a;">{WebUtility.HtmlEncode(senderName)} shared <strong style="color:#245b3d;">{WebUtility.HtmlEncode(questionBankName)}</strong> with you. A copy is now available in your question banks.</p>
                """),
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
        var safeHeading = WebUtility.HtmlEncode(heading);
        var safeDescription = WebUtility.HtmlEncode(description);
        var safeCode = WebUtility.HtmlEncode(code);

        return BuildBrandedEmailHtml(
            recipientName,
            $"""
            <h1 style="margin:0 0 12px;font-size:25px;line-height:1.25;color:#245b3d;">{safeHeading}</h1>
            <p style="margin:0 0 24px;font-size:15px;line-height:1.7;color:#53665a;">{safeDescription}</p>
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#edf5ee;border:1px dashed #78a887;border-radius:10px;">
                <tr><td align="center" style="padding:18px 12px;font-size:32px;letter-spacing:6px;font-weight:700;color:#245b3d;">{safeCode}</td></tr>
            </table>
            <p style="margin:24px 0 0;font-size:13px;line-height:1.6;color:#718078;">This code expires in 15 minutes. If you did not request this email, you can safely ignore it.</p>
            """);
    }

    private static string BuildBrandedEmailHtml(string recipientName, string contentHtml)
    {
        var safeName = WebUtility.HtmlEncode(recipientName);

        return $"""
            <!doctype html>
            <html lang="en">
                <body style="margin:0;background:#f2f6f2;color:#203128;font-family:Arial,Helvetica,sans-serif;">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 12px;background:#f2f6f2;">
                        <tr><td align="center">
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border:1px solid #dfe9e0;border-radius:16px;overflow:hidden;">
                                <tr><td style="padding:20px 28px;background:#245b3d;color:#ffffff;">
                                    <table role="presentation" cellspacing="0" cellpadding="0"><tr>
                                        <td style="padding-right:12px;font-size:30px;line-height:1;">&#129417;</td>
                                        <td style="font-size:20px;font-weight:700;letter-spacing:.3px;">SFIT LMS</td>
                                    </tr></table>
                                </td></tr>
                                <tr><td style="padding:32px 28px;">
                                    <p style="margin:0 0 20px;font-size:16px;color:#35483b;">Hello {safeName},</p>
                                    {contentHtml}
                                </td></tr>
                                <tr><td style="padding:16px 28px;background:#f6f9f6;border-top:1px solid #e7eee7;color:#718078;font-size:12px;line-height:1.6;">This is an automated message from SFIT LMS. Please do not reply.</td></tr>
                            </table>
                        </td></tr>
                    </table>
                </body>
            </html>
            """;
    }
}
