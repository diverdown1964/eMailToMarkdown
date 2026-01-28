using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using EmailToMarkdown.Models;
using EmailToMarkdown.Services;
using System.Text;
using HttpMultipartParser;
using MimeKit;

namespace EmailToMarkdown.Functions;

public class SendGridInbound
{
    private readonly ILogger _logger;
    private readonly AppConfiguration _config;
    private readonly MarkdownConversionService _markdownService;
    private readonly AzureCommunicationEmailService? _emailService;
    private readonly ConfigurationService _configService;
    private readonly StorageProviderFactory _storageFactory;

    public SendGridInbound(
        ILoggerFactory loggerFactory,
        AppConfiguration config,
        ConfigurationService configService,
        StorageProviderFactory storageFactory)
    {
        _logger = loggerFactory.CreateLogger<SendGridInbound>();
        _config = config;
        _configService = configService;
        _storageFactory = storageFactory;
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
    }

    [Function("SendGridInbound")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "inbound")] HttpRequestData req)
    {
        _logger.LogInformation("Inbound email received");

        string from = "", to = "", subject = "No Subject", html = "";

        try
        {
            // Copy body to memory stream first
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            ms.Position = 0;

            // Try to parse multipart form data from SendGrid
            var parsedForm = await MultipartFormDataParser.ParseAsync(ms);

            string? rawEmail = null;

            foreach (var param in parsedForm.Parameters)
            {
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

            // If raw MIME is present, parse it with MimeKit
            if (!string.IsNullOrEmpty(rawEmail))
            {
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
                }
                catch (Exception mimeEx)
                {
                    _logger.LogError(mimeEx, "Failed to parse MIME message");
                }
            }

            if (string.IsNullOrEmpty(subject)) subject = "No Subject";

            // The forwarder/subscriber info - used for user preferences lookup
            var forwarderName = ExtractNameFromEmail(from);
            var forwarderEmail = ExtractEmailAddress(from);

            // Display info for markdown output - defaults to forwarder, may be overwritten for forwards
            var displaySenderName = forwarderName;
            var displaySenderEmail = forwarderEmail;
            var receivedDateTime = DateTime.UtcNow;

            // Handle forwarded emails - extract original sender metadata for display only
            if (MarkdownConversionService.IsForwardedEmail(subject))
            {
                // Strip FW:/Fwd: prefix from subject
                subject = MarkdownConversionService.StripForwardingPrefix(subject);

                // Try to extract original sender metadata from the forwarding header
                var originalMetadata = _markdownService.ExtractForwardedMetadata(html);
                if (originalMetadata != null)
                {
                    if (!string.IsNullOrEmpty(originalMetadata.SenderEmail))
                    {
                        displaySenderName = !string.IsNullOrEmpty(originalMetadata.SenderName)
                            ? originalMetadata.SenderName
                            : originalMetadata.SenderEmail.Split('@')[0];
                        displaySenderEmail = originalMetadata.SenderEmail;
                    }
                    if (originalMetadata.SentDate.HasValue)
                    {
                        receivedDateTime = originalMetadata.SentDate.Value;
                    }
                }
            }

            _logger.LogInformation("Processing email from {Email} - Subject: {Subject}", forwarderEmail, subject);

            // Convert to markdown - use display sender (original sender for forwards)
            var markdownContent = _markdownService.ConvertToMarkdownBytes(
                subject,
                displaySenderName,
                displaySenderEmail,
                receivedDateTime,
                html);

            // Generate filename using the original date and sender for forwarded emails
            var fileName = GenerateFileName(receivedDateTime, displaySenderName, subject);

            // Get user preferences using FORWARDER's email (the subscriber), not the original sender
            var userPrefs = await _configService.GetUserPreferencesAsync(forwarderEmail);

            if (userPrefs == null)
            {
                _logger.LogWarning("No user preferences found for {Email}", forwarderEmail);
                userPrefs = new UserPreferences
                {
                    DeliveryMethod = "email",
                    RootFolder = "/EmailToMarkdown"
                };
            }

            var deliveryMethod = userPrefs.DeliveryMethod ?? "email";

            // Track results for all storage providers
            var storageResults = new List<(string Provider, string Email, StorageResult Result)>();
            var requiresReauth = new List<string>();

            // Handle storage delivery - send to ALL configured providers
            if (deliveryMethod == "onedrive" || deliveryMethod == "both" || deliveryMethod == "storage")
            {
                try
                {
                    // Get all storage connections for the user (including linked identities)
                    var allConnections = await _configService.GetAllStorageConnectionsForUserAsync(forwarderEmail);

                    foreach (var kvp in allConnections)
                    {
                        var providerName = kvp.Key;
                        var connection = kvp.Value;
                        var rootFolder = connection.RootFolder ?? "/EmailToMarkdown";

                        try
                        {
                            var storageProvider = _storageFactory.GetProvider(providerName);

                            var result = await storageProvider.SaveFileAsync(
                                connection.PartitionKey, // Use the email associated with this connection
                                rootFolder,
                                fileName,
                                markdownContent);

                            storageResults.Add((providerName, connection.PartitionKey, result));

                            if (result.Success)
                            {
                                _logger.LogInformation("File saved to {Provider}: {Path}", providerName, result.FilePath);
                                connection.LastSuccessfulSync = DateTimeOffset.UtcNow;
                                await _configService.SaveStorageConnectionAsync(connection.PartitionKey, providerName, connection);
                            }
                            else if (result.RequiresReauth)
                            {
                                _logger.LogWarning("Re-authentication required for {Provider}: {Error}", providerName, result.ErrorMessage);
                                requiresReauth.Add(providerName);
                            }
                            else
                            {
                                _logger.LogError("Failed to save to {Provider}: {Error}", providerName, result.ErrorMessage);
                            }
                        }
                        catch (NotImplementedException ex)
                        {
                            _logger.LogWarning(ex, $"Storage provider {providerName} not yet implemented");
                            storageResults.Add((providerName, connection.PartitionKey, StorageResult.Failed($"Provider {providerName} not yet implemented")));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Exception saving to {providerName} for {connection.PartitionKey}: {ex.Message}");
                            storageResults.Add((providerName, connection.PartitionKey, StorageResult.Failed(ex.Message)));
                        }
                    }

                    // If no connections found, fall back to legacy preferences-based storage
                    if (allConnections.Count == 0 && !string.IsNullOrEmpty(userPrefs.StorageProvider))
                    {
                        var rootFolder = userPrefs.RootFolder ?? "/EmailToMarkdown";

                        try
                        {
                            var storageProvider = _storageFactory.GetProvider(userPrefs.StorageProvider);
                            var result = await storageProvider.SaveFileAsync(
                                forwarderEmail,
                                rootFolder,
                                fileName,
                                markdownContent);

                            storageResults.Add((userPrefs.StorageProvider, forwarderEmail, result));

                            if (result.Success)
                            {
                                userPrefs.LastSuccessfulSync = DateTimeOffset.UtcNow;
                                await _configService.SaveUserPreferencesAsync(userPrefs);
                            }
                            else if (result.RequiresReauth)
                            {
                                requiresReauth.Add(userPrefs.StorageProvider);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Exception in legacy storage save");
                            storageResults.Add((userPrefs.StorageProvider, forwarderEmail, StorageResult.Failed(ex.Message)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception retrieving storage connections");
                }
            }

            // Determine storage success - at least one provider succeeded
            var successfulSaves = storageResults.Where(r => r.Result.Success).ToList();
            var failedSaves = storageResults.Where(r => !r.Result.Success).ToList();
            bool anyStorageSuccess = successfulSaves.Any();

            bool emailSent = false;

            // Handle email delivery - send if:
            // 1. Delivery method includes email, OR
            // 2. Any storage saves failed (send notification with attachment)
            bool shouldSendEmail = deliveryMethod == "email" || deliveryMethod == "both" || failedSaves.Any();

            if (shouldSendEmail && _emailService != null)
            {
                var replySubject = $"Re: {subject}";
                string replyBody;

                if (failedSaves.Any() && deliveryMethod != "email")
                {
                    // Build failure notification
                    var failureDetails = new StringBuilder();
                    failureDetails.AppendLine("**Storage Save Failures:**\n");
                    foreach (var failure in failedSaves)
                    {
                        var reauthNote = failure.Result.RequiresReauth ? " (re-authentication required)" : "";
                        failureDetails.AppendLine($"- {failure.Provider}: {failure.Result.ErrorMessage}{reauthNote}");
                    }

                    if (successfulSaves.Any())
                    {
                        failureDetails.AppendLine("\n**Successful Saves:**");
                        foreach (var saved in successfulSaves)
                        {
                            failureDetails.AppendLine($"- {saved.Provider}: {saved.Result.FilePath}");
                        }
                    }

                    if (requiresReauth.Any())
                    {
                        failureDetails.AppendLine($"\n**Action Required:** Please visit the registration page to re-authenticate your {string.Join(", ", requiresReauth)} connection(s).");
                    }

                    replyBody = $@"Your email has been converted to Markdown.

The markdown file is attached to this email because some storage saves failed.

{failureDetails}

---
Email to Markdown Service";
                }
                else if (requiresReauth.Any())
                {
                    replyBody = $@"Your email has been converted to Markdown.

The markdown file is attached to this email.

**Important:** Your {string.Join(", ", requiresReauth)} connection has expired. Please visit the registration page to re-authenticate and restore automatic saving.

---
Email to Markdown Service";
                }
                else if (deliveryMethod == "both" && anyStorageSuccess)
                {
                    var savedTo = string.Join(", ", successfulSaves.Select(s => s.Provider));
                    replyBody = $@"Your email has been converted to Markdown.

The markdown file is attached to this email and has also been saved to your {savedTo}.

---
Email to Markdown Service";
                }
                else
                {
                    replyBody = $@"Your email has been converted to Markdown.

The markdown file is attached to this email.

---
Email to Markdown Service";
                }

                emailSent = await _emailService.SendEmailWithAttachmentAsync(
                    forwarderEmail,
                    forwarderName,
                    replySubject,
                    replyBody,
                    fileName,
                    markdownContent);

                if (!emailSent)
                {
                    _logger.LogError("Failed to send reply email to {Email}", forwarderEmail);
                }
            }

            // Determine overall success
            // Success if: email delivery worked when required, OR at least one storage save worked
            bool success = deliveryMethod switch
            {
                "email" => emailSent,
                "onedrive" or "storage" => anyStorageSuccess,
                "both" => emailSent || anyStorageSuccess, // Success if at least one method worked
                _ => emailSent || anyStorageSuccess // Default: any success counts
            };

            if (success)
            {
                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                var summary = new StringBuilder("Email processed successfully.");
                if (successfulSaves.Any())
                {
                    summary.Append($" Saved to: {string.Join(", ", successfulSaves.Select(s => s.Provider))}.");
                }
                if (failedSaves.Any())
                {
                    summary.Append($" Failed: {string.Join(", ", failedSaves.Select(f => f.Provider))}.");
                }
                await response.WriteStringAsync(summary.ToString());
                return response;
            }
            else
            {
                var response = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await response.WriteStringAsync("Failed to process email - no delivery methods succeeded");
                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing inbound email");
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
