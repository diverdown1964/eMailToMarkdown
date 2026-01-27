using SendGrid;
using SendGrid.Helpers.Mail;
using System.Text;
using Microsoft.Extensions.Logging;

namespace EmailToMarkdown.Services;

public class SendGridEmailService
{
    private readonly string _apiKey;
    private readonly ILogger? _logger;

    public SendGridEmailService(string apiKey, ILogger? logger = null)
    {
        _apiKey = apiKey;
        _logger = logger;
    }

    public async Task<bool> SendEmailWithAttachmentAsync(
        string toEmail,
        string toName,
        string fromEmail,
        string fromName,
        string subject,
        string body,
        string attachmentFileName,
        byte[] attachmentContent)
    {
        var client = new SendGridClient(_apiKey);
        var from = new EmailAddress(fromEmail, fromName);
        var to = new EmailAddress(toEmail, toName);
        var msg = MailHelper.CreateSingleEmail(from, to, subject, body, body);

        // Add markdown attachment
        var base64Content = Convert.ToBase64String(attachmentContent);
        msg.AddAttachment(attachmentFileName, base64Content, "text/markdown");

        var response = await client.SendEmailAsync(msg);
        
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Body.ReadAsStringAsync();
            _logger?.LogError($"SendGrid failed: {response.StatusCode} - {responseBody}");
        }
        else
        {
            _logger?.LogInformation($"SendGrid success: {response.StatusCode}");
        }
        
        return response.IsSuccessStatusCode;
    }
}
