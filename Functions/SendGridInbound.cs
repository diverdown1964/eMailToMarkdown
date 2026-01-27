using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using EmailToMarkdown.Models;
using EmailToMarkdown.Services;
using System.Text;
using System.Web;
using HttpMultipartParser;
using MimeKit;

namespace EmailToMarkdown.Functions;

public class SendGridInbound
{
    private readonly ILogger _logger;
    private readonly AppConfiguration _config;
    private readonly MarkdownConversionService _markdownService;
    private readonly AzureCommunicationEmailService _emailService;
    private readonly ConfigurationService _configService;
    private readonly OneDriveStorageService? _oneDriveService;

    public SendGridInbound(
        ILoggerFactory loggerFactory,
        AppConfiguration config,
        ConfigurationService configService,
        GraphServiceClient? graphClient = null)
    {
        _logger = loggerFactory.CreateLogger<SendGridInbound>();
        _config = config;
        _configService = configService;
        _markdownService = new MarkdownConversionService(_logger);

        if (!string.IsNullOrEmpty(config.AcsConnectionString))
        {
            _emailService = new AzureCommunicationEmailService(config.AcsConnectionString, _logger);
        }
        else
        {
            _logger.LogWarning("ACS Connection String is empty - email sending will fail");
            _emailService = null!;
        }

        if (graphClient != null)
        {
            _oneDriveService = new OneDriveStorageService(graphClient, _logger);
        }
    }

    [Function("SendGridInbound")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inbound")] HttpRequestData req)
    {
        _logger.LogInformation("SendGrid inbound email received - START");

        string from = "", to = "", subject = "No Subject", html = "";
        
        try
        {
            // Log request info
            var contentType = req.Headers.TryGetValues("Content-Type", out var ctValues) ? ctValues.FirstOrDefault() : "unknown";
            _logger.LogInformation($"Content-Type: {contentType}");
            _logger.LogInformation($"Body CanSeek: {req.Body.CanSeek}, CanRead: {req.Body.CanRead}");
            
            // Copy body to memory stream first
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            _logger.LogInformation($"Body length: {ms.Length} bytes");
            ms.Position = 0;
            
            // Try to parse multipart form data from SendGrid
            var parsedForm = await MultipartFormDataParser.ParseAsync(ms);
            _logger.LogInformation($"Parsed {parsedForm.Parameters.Count} parameters");

            string? rawEmail = null;
            var allParams = new System.Text.StringBuilder();

            foreach (var param in parsedForm.Parameters)
            {
                _logger.LogInformation($"Param: {param.Name} (length: {param.Data?.Length ?? 0})");
                allParams.AppendLine($"{param.Name}: {param.Data?.Length ?? 0} chars");
                
                switch (param.Name.ToLower())
                {
                    case "from": from = param.Data; break;
                    case "to": to = param.Data; break;
                    case "subject": subject = param.Data; break;
                    case "html": html = param.Data; break;
                    case "text": if (string.IsNullOrEmpty(html)) html = param.Data; break;
                    case "email": rawEmail = param.Data; break; // Raw MIME when "POST raw" is enabled
                }
            }
            
            _logger.LogInformation($"All parameters received:\n{allParams.ToString()}");

            // If raw MIME is present, parse it with MimeKit
            if (!string.IsNullOrEmpty(rawEmail))
            {
                _logger.LogInformation("Raw MIME email detected, parsing with MimeKit");
                try
                {
                    using var mimeStream = new MemoryStream(Encoding.UTF8.GetBytes(rawEmail));
                    var mimeMessage = await MimeMessage.LoadAsync(mimeStream);

                    // Extract headers if not already set
                    if (string.IsNullOrEmpty(from) && mimeMessage.From.Count > 0)
                    {
                        var sender = mimeMessage.From[0] as MailboxAddress;
                        from = sender != null ? $"{sender.Name} <{sender.Address}>" : mimeMessage.From[0].ToString();
                    }
                    if (string.IsNullOrEmpty(subject))
                    {
                        subject = mimeMessage.Subject ?? "No Subject";
                    }

                    // Extract body - prefer HTML, fall back to text
                    if (string.IsNullOrEmpty(html))
                    {
                        html = mimeMessage.HtmlBody ?? "";
                    }
                    if (string.IsNullOrEmpty(html))
                    {
                        html = mimeMessage.TextBody ?? "";
                    }

                    _logger.LogInformation($"MimeKit parsed - From: {from}, Subject: {subject}, Body length: {html?.Length ?? 0}");
                }
                catch (Exception mimeEx)
                {
                    _logger.LogError($"Failed to parse MIME message: {mimeEx.Message}");
                }
            }

            _logger.LogInformation($"Final HTML body length: {html?.Length ?? 0}");
            
            if (string.IsNullOrEmpty(subject)) subject = "No Subject";
            var senderName = ExtractNameFromEmail(from);
            var senderEmail = ExtractEmailAddress(from);

            _logger.LogInformation($"Processing email from {senderEmail} ({senderName}) - Subject: {subject}");

            // Convert to markdown
            _logger.LogInformation("Starting markdown conversion...");
            var markdownContent = _markdownService.ConvertToMarkdownBytes(
                subject,
                senderName,
                senderEmail,
                DateTime.UtcNow,
                html);
            _logger.LogInformation($"Markdown conversion complete. Content size: {markdownContent?.Length ?? 0} bytes");

            // Generate filename
            var fileName = GenerateFileName(DateTime.UtcNow, senderName, subject);
            _logger.LogInformation($"Generated filename: {fileName}");

            // Get user preferences to determine delivery method
            _logger.LogInformation($"Fetching user preferences for: {senderEmail}");
            var userPrefs = await _configService.GetUserPreferencesAsync(senderEmail);
            _logger.LogInformation($"User preferences retrieved. DeliveryMethod: {userPrefs?.DeliveryMethod ?? "null"}");
            
            var deliveryMethod = userPrefs?.DeliveryMethod ?? "email";
            var oneDriveUserEmail = userPrefs?.OneDriveUserEmail ?? senderEmail;
            var rootFolder = userPrefs?.RootFolder ?? "/EmailToMarkdown";

            _logger.LogInformation($"Delivery method for {senderEmail}: {deliveryMethod}");

            bool emailSent = false;
            bool fileSaved = false;

            // Handle OneDrive delivery
            if (deliveryMethod == "onedrive" || deliveryMethod == "both")
            {
                if (_oneDriveService != null)
                {
                    fileSaved = await _oneDriveService.SaveFileAsync(
                        oneDriveUserEmail,
                        rootFolder,
                        fileName,
                        markdownContent);

                    if (fileSaved)
                    {
                        _logger.LogInformation($"File saved to OneDrive for {oneDriveUserEmail}");
                    }
                    else
                    {
                        _logger.LogError($"Failed to save file to OneDrive for {oneDriveUserEmail}");
                    }
                }
                else
                {
                    _logger.LogWarning("OneDrive service not configured - skipping OneDrive delivery");
                }
            }

            // Handle email delivery
            if (deliveryMethod == "email" || deliveryMethod == "both")
            {
                if (_emailService != null)
                {
                    var replySubject = $"Re: {subject}";
                    var replyBody = deliveryMethod == "both" && fileSaved
                        ? $@"Your email has been converted to Markdown.

The markdown file is attached to this email and has also been saved to your OneDrive.

---
Email to Markdown Service"
                        : $@"Your email has been converted to Markdown.

The markdown file is attached to this email.

---
Email to Markdown Service";

                    _logger.LogInformation($"Sending email to {senderEmail}");

                    emailSent = await _emailService.SendEmailWithAttachmentAsync(
                        senderEmail,
                        senderName,
                        replySubject,
                        replyBody,
                        fileName,
                        markdownContent);

                    if (emailSent)
                    {
                        _logger.LogInformation($"Reply sent successfully to {senderEmail}");
                    }
                    else
                    {
                        _logger.LogError($"Failed to send reply to {senderEmail}");
                    }
                }
                else
                {
                    _logger.LogWarning("Email service not configured - skipping email delivery");
                }
            }

            // Determine overall success
            bool success = deliveryMethod switch
            {
                "email" => emailSent,
                "onedrive" => fileSaved,
                "both" => emailSent || fileSaved, // Success if at least one worked
                _ => emailSent // Default to email behavior
            };

            if (success)
            {
                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteStringAsync("Email processed successfully");
                return response;
            }
            else
            {
                var response = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await response.WriteStringAsync("Failed to process email");
                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error processing inbound email: {ex.Message} | StackTrace: {ex.StackTrace}");
            var response = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            await response.WriteStringAsync($"Error: {ex.Message}");
            return response;
        }
    }

    private string ExtractNameFromEmail(string email)
    {
        // Handle format: "John White <john@whites.site>" or just "john@whites.site"
        if (email.Contains('<') && email.Contains('>'))
        {
            var start = email.IndexOf('<');
            var name = email.Substring(0, start).Trim().Trim('"');
            return string.IsNullOrEmpty(name) ? "Unknown" : name;
        }
        
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email.Substring(0, atIndex) : "Unknown";
    }

    private string ExtractEmailAddress(string email)
    {
        // Handle format: "John White <john@whites.site>" or just "john@whites.site"
        if (email.Contains('<') && email.Contains('>'))
        {
            var start = email.IndexOf('<') + 1;
            var end = email.IndexOf('>');
            return email.Substring(start, end - start).Trim();
        }
        return email.Trim();
    }

    private string GenerateFileName(DateTime date, string senderName, string subject)
    {
        var sanitizedSender = SanitizeFileName(senderName);
        var sanitizedSubject = SanitizeFileName(subject);
        return $"{date:yyyy-MM-dd}-{sanitizedSender}-{sanitizedSubject}.md";
    }

    private string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Length > 50 ? sanitized.Substring(0, 50) : sanitized;
    }
}
