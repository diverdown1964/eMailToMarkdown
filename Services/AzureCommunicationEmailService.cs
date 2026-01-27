using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;

namespace EmailToMarkdown.Services;

public class AzureCommunicationEmailService
{
    private readonly EmailClient _client;
    private readonly ILogger? _logger;
    private const string SenderEmail = "noreply@4aaf2181-3fd3-48bc-a473-75e4e9842eb4.azurecomm.net";

    public AzureCommunicationEmailService(string connectionString, ILogger? logger = null)
    {
        _client = new EmailClient(connectionString);
        _logger = logger;
    }

    public async Task<bool> SendEmailWithAttachmentAsync(
        string toEmail,
        string toName,
        string subject,
        string body,
        string attachmentFileName,
        byte[] attachmentContent)
    {
        const int maxRetries = 3;
        int retryDelay = 1000; // Start with 1 second
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var emailContent = new EmailContent(subject)
                {
                    PlainText = body
                };

                var emailRecipients = new EmailRecipients(new List<EmailAddress>
                {
                    new EmailAddress(toEmail, toName)
                });

                var emailMessage = new EmailMessage(SenderEmail, emailRecipients, emailContent);

                // Add attachment
                var attachment = new EmailAttachment(
                    attachmentFileName,
                    "text/markdown",
                    new BinaryData(attachmentContent));
                emailMessage.Attachments.Add(attachment);

                _logger?.LogInformation($"Sending email to {toEmail} (attempt {attempt + 1}/{maxRetries})");

                var emailSendOperation = await _client.SendAsync(
                    WaitUntil.Started,
                    emailMessage);

                _logger?.LogInformation($"Email queued with operation ID: {emailSendOperation.Id}");
                
                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 429 && attempt < maxRetries - 1)
            {
                // Rate limited - wait and retry with exponential backoff
                _logger?.LogWarning($"Rate limited (429), retrying in {retryDelay}ms...");
                await Task.Delay(retryDelay);
                retryDelay *= 2; // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to send email: {ex.Message}");
                if (attempt == maxRetries - 1)
                {
                    return false;
                }
            }
        }
        
        return false;
    }
}
