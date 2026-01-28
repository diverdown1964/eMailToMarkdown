using ReverseMarkdown;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using HtmlAgilityPack;

namespace EmailToMarkdown.Services;

public class MarkdownConversionService
{
    private readonly Converter _converter;
    private readonly ILogger? _logger;

    public MarkdownConversionService(ILogger? logger = null)
    {
        _logger = logger;
        var config = new ReverseMarkdown.Config
        {
            // Drop unknown tags - don't pass them through as raw HTML
            UnknownTags = Config.UnknownTagsOption.Drop,
            GithubFlavored = true,
            SmartHrefHandling = true,
            // Remove HTML comments
            RemoveComments = true,
            // Preserve line breaks in text
            PassThroughTags = new[] { "br" }
        };
        _converter = new Converter(config);
    }

    public string ConvertHtmlToMarkdown(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // Log first 1000 chars of HTML for debugging
        _logger?.LogInformation($"Raw HTML (first 1000): {html.Substring(0, Math.Min(1000, html.Length))}");

        // Save full HTML to temp file for debugging
        try
        {
            var debugPath = Path.Combine(Path.GetTempPath(), $"email-html-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            File.WriteAllText(debugPath, html);
            _logger?.LogInformation($"Saved full HTML to: {debugPath}");
        }
        catch { }

        // First, clean the HTML to remove email cruft (signatures, headers, etc.)
        string cleaned;
        try
        {
            cleaned = CleanEmailHtml(html);
            _logger?.LogInformation("HTML cleaned successfully");
            _logger?.LogInformation($"Cleaned HTML (first 500 chars): {cleaned.Substring(0, Math.Min(500, cleaned.Length))}");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"HTML cleanup failed, using original: {ex.Message}");
            cleaned = html;
        }

        // Try Pandoc first - it handles complex HTML much better
        try
        {
            var markdown = ConvertWithPandoc(cleaned);
            if (!string.IsNullOrWhiteSpace(markdown))
            {
                _logger?.LogInformation($"Pandoc conversion successful ({markdown.Length} chars)");
                markdown = PostProcessMarkdown(markdown);
                return markdown.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Pandoc conversion failed: {ex.Message}");
        }

        // Fallback to ReverseMarkdown
        _logger?.LogInformation("Falling back to ReverseMarkdown");
        try
        {
            var markdown = _converter.Convert(cleaned);
            _logger?.LogInformation($"ReverseMarkdown conversion ({markdown.Length} chars)");
            markdown = PostProcessMarkdown(markdown);
            return markdown.Trim();
        }
        catch (Exception ex)
        {
            _logger?.LogError($"ReverseMarkdown also failed: {ex.Message}");
            // Last resort - just extract text
            return ExtractTextFromHtml(cleaned);
        }
    }

    private string ConvertWithPandoc(string html)
    {
        // Find pandoc.exe - it should be in the same directory as the assembly
        var assemblyDir = Path.GetDirectoryName(typeof(MarkdownConversionService).Assembly.Location) ?? "";
        var pandocPath = Path.Combine(assemblyDir, "pandoc.exe");

        // Also check Tools subdirectory (for local development)
        if (!File.Exists(pandocPath))
        {
            pandocPath = Path.Combine(assemblyDir, "Tools", "pandoc.exe");
        }

        // Check current directory as well
        if (!File.Exists(pandocPath))
        {
            pandocPath = Path.Combine(Directory.GetCurrentDirectory(), "pandoc.exe");
        }

        if (!File.Exists(pandocPath))
        {
            _logger?.LogWarning($"Pandoc not found. Searched: {assemblyDir}");
            throw new FileNotFoundException("Pandoc executable not found");
        }

        _logger?.LogInformation($"Using Pandoc at: {pandocPath}");

        // Write HTML to temp file (safer than stdin for large content)
        var tempInput = Path.GetTempFileName() + ".html";
        var tempOutput = Path.GetTempFileName() + ".md";

        try
        {
            File.WriteAllText(tempInput, html);

            var startInfo = new ProcessStartInfo
            {
                FileName = pandocPath,
                Arguments = $"-f html -t markdown-raw_html-native_divs-native_spans --wrap=none \"{tempInput}\" -o \"{tempOutput}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new Exception("Failed to start Pandoc process");
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000); // 30 second timeout

            if (process.ExitCode != 0)
            {
                _logger?.LogWarning($"Pandoc exited with code {process.ExitCode}: {stderr}");
                throw new Exception($"Pandoc failed: {stderr}");
            }

            var result = File.ReadAllText(tempOutput);
            _logger?.LogInformation($"Pandoc output length: {result.Length}");
            return result;
        }
        finally
        {
            // Cleanup temp files
            try { File.Delete(tempInput); } catch { }
            try { File.Delete(tempOutput); } catch { }
        }
    }

    private string ExtractTextFromHtml(string html)
    {
        // Last resort - just get text content
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var text = doc.DocumentNode.InnerText;
            // Clean up whitespace
            text = Regex.Replace(text, @"\s+", " ");
            text = Regex.Replace(text, @"\n\s*\n", "\n\n");
            return text.Trim();
        }
        catch
        {
            // Absolute last resort
            return Regex.Replace(html, @"<[^>]+>", " ").Trim();
        }
    }

    private string CleanEmailHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        
        var initialTextLength = doc.DocumentNode.InnerText?.Length ?? 0;
        _logger?.LogInformation($"CleanEmailHtml starting. Initial text length: {initialTextLength}");

        // Remove elements that don't contribute to content
        RemoveUnwantedElements(doc);
        _logger?.LogInformation($"After RemoveUnwantedElements: {doc.DocumentNode.InnerText?.Length ?? 0} chars");

        // Remove forwarding headers - ONLY the header block, keep the body
        RemoveForwardingHeaders(doc);
        _logger?.LogInformation($"After RemoveForwardingHeaders: {doc.DocumentNode.InnerText?.Length ?? 0} chars");

        // Remove email signatures
        RemoveSignatures(doc);
        _logger?.LogInformation($"After RemoveSignatures: {doc.DocumentNode.InnerText?.Length ?? 0} chars");

        // Clean up quoted reply chains
        CleanQuotedReplies(doc);
        _logger?.LogInformation($"After CleanQuotedReplies: {doc.DocumentNode.InnerText?.Length ?? 0} chars");

        // Clean up attributes that cause conversion issues
        CleanAttributes(doc);

        // Simplify tables - unwrap layout tables to expose content
        _logger?.LogInformation("Starting table simplification...");
        SimplifyTables(doc);
        _logger?.LogInformation($"After table simplification: {doc.DocumentNode.InnerText?.Length ?? 0} chars");

        // Unwrap unnecessary container divs/tables
        _logger?.LogInformation("Starting structure simplification...");
        SimplifyStructure(doc);
        _logger?.LogInformation($"After structure simplification: {doc.DocumentNode.InnerText?.Length ?? 0} chars");
        
        var finalTextLength = doc.DocumentNode.InnerText?.Length ?? 0;
        _logger?.LogInformation($"CleanEmailHtml complete. Final text length: {finalTextLength}");
        
        // Warning if we removed too much content
        if (finalTextLength < initialTextLength * 0.1 && initialTextLength > 100)
        {
            _logger?.LogWarning($"WARNING: Removed {100 - (finalTextLength * 100 / initialTextLength)}% of content. Initial: {initialTextLength}, Final: {finalTextLength}");
        }

        return doc.DocumentNode.OuterHtml;
    }

    private void RemoveUnwantedElements(HtmlDocument doc)
    {
        // Elements to completely remove (including content)
        var elementsToRemove = new List<HtmlNode>();

        // Remove style tags
        elementsToRemove.AddRange(doc.DocumentNode.SelectNodes("//style") ?? Enumerable.Empty<HtmlNode>());

        // Remove script tags
        elementsToRemove.AddRange(doc.DocumentNode.SelectNodes("//script") ?? Enumerable.Empty<HtmlNode>());

        // Remove tracking pixels (1x1 images, common tracking patterns)
        var images = doc.DocumentNode.SelectNodes("//img") ?? Enumerable.Empty<HtmlNode>();
        foreach (var img in images)
        {
            var width = img.GetAttributeValue("width", "");
            var height = img.GetAttributeValue("height", "");
            var src = img.GetAttributeValue("src", "").ToLower();

            // Remove 1x1 tracking pixels
            if ((width == "1" || width == "0") && (height == "1" || height == "0"))
            {
                elementsToRemove.Add(img);
                continue;
            }

            // Remove common tracking pixel patterns
            if (src.Contains("track") || src.Contains("pixel") || src.Contains("beacon") ||
                src.Contains("open.") || src.Contains("/o/") || src.Contains("mailtrack"))
            {
                elementsToRemove.Add(img);
            }
        }

        // Remove hidden elements
        var allElements = doc.DocumentNode.SelectNodes("//*[@style]") ?? Enumerable.Empty<HtmlNode>();
        foreach (var elem in allElements)
        {
            var style = elem.GetAttributeValue("style", "").ToLower();
            if (style.Contains("display:none") || style.Contains("display: none") ||
                style.Contains("visibility:hidden") || style.Contains("visibility: hidden"))
            {
                elementsToRemove.Add(elem);
            }
        }

        // Remove noscript tags
        elementsToRemove.AddRange(doc.DocumentNode.SelectNodes("//noscript") ?? Enumerable.Empty<HtmlNode>());

        // Remove MSO/Office elements (o:p, w:p, etc.) - find elements with colons in name
        var allNodes = doc.DocumentNode.SelectNodes("//*") ?? Enumerable.Empty<HtmlNode>();
        foreach (var node in allNodes)
        {
            if (node.Name.Contains(':'))
            {
                elementsToRemove.Add(node);
            }
        }

        // Perform removal
        foreach (var node in elementsToRemove.Distinct())
        {
            node.Remove();
        }

        // Remove HTML comments (including MSO conditionals)
        var comments = doc.DocumentNode.SelectNodes("//comment()") ?? Enumerable.Empty<HtmlNode>();
        foreach (var comment in comments.ToList())
        {
            comment.Remove();
        }
    }

    private void RemoveForwardingHeaders(HtmlDocument doc)
    {
        var removedCount = 0;

        // Method 1: Target Outlook mobile forwarding header specifically
        // The header is in a div with class containing "ms-outlook-mobile-reference-message"
        // It contains <b>From:</b>...<br><b>Sent:</b>...<br><b>To:</b>...<br><b>Subject:</b>...<br>
        var outlookHeaders = doc.DocumentNode.SelectNodes("//div[contains(@class, 'ms-outlook-mobile-reference-message')]");
        if (outlookHeaders != null)
        {
            foreach (var headerDiv in outlookHeaders.ToList())
            {
                // Find and remove the From/Sent/To/Subject header elements within this div
                // They are typically <b> tags followed by <br>
                var boldTags = headerDiv.SelectNodes(".//b");
                if (boldTags != null)
                {
                    var headerBolds = boldTags.Where(b =>
                    {
                        var text = b.InnerText?.Trim() ?? "";
                        return text.StartsWith("From:", StringComparison.OrdinalIgnoreCase) ||
                               text.StartsWith("Sent:", StringComparison.OrdinalIgnoreCase) ||
                               text.StartsWith("To:", StringComparison.OrdinalIgnoreCase) ||
                               text.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase) ||
                               text.StartsWith("Date:", StringComparison.OrdinalIgnoreCase);
                    }).ToList();

                    foreach (var bold in headerBolds)
                    {
                        // Remove the bold tag and any following text/br until next element
                        var parent = bold.ParentNode;
                        if (parent == null) continue;

                        // Remove siblings until we hit the next <b> or block element
                        var sibling = bold.NextSibling;
                        while (sibling != null)
                        {
                            var next = sibling.NextSibling;
                            if (sibling.Name == "b" || sibling.Name == "div" || sibling.Name == "p")
                                break;
                            if (sibling.Name == "br" || sibling.NodeType == HtmlNodeType.Text)
                            {
                                sibling.Remove();
                            }
                            sibling = next;
                        }
                        bold.Remove();
                        removedCount++;
                    }
                }
                _logger?.LogInformation($"Cleaned Outlook mobile forwarding header in div");
            }
        }

        // Method 2: Look for standalone forwarding header blocks
        var allNodes = doc.DocumentNode.SelectNodes("//*[contains(text(), 'From:') or contains(., 'From:')]");
        if (allNodes != null)
        {
            foreach (var node in allNodes.ToList())
            {
                var text = node.InnerText;
                if (IsForwardingHeaderBlock(text))
                {
                    var container = FindForwardingHeaderContainer(node);
                    if (container != null)
                    {
                        _logger?.LogInformation($"Found forwarding header block, removing container: {container.Name} with text length {container.InnerText?.Length ?? 0}");
                        container.Remove();
                        removedCount++;
                    }
                }
            }
        }

        _logger?.LogInformation($"DOM-based header removal: removed {removedCount} elements");

        // Method 3: Regex fallback for patterns that DOM might miss
        var bodyHtml = doc.DocumentNode.OuterHtml;
        var cleanedHtml = StripForwardingHeaderPatterns(bodyHtml);
        if (cleanedHtml != bodyHtml)
        {
            _logger?.LogInformation($"Removed forwarding header via regex pattern");
            doc.LoadHtml(cleanedHtml);
        }
    }

    private bool IsForwardingHeaderBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Normalize whitespace for matching
        var normalizedText = Regex.Replace(text, @"\s+", " ").Trim();

        // Must have From: and Subject: at minimum
        if (!normalizedText.Contains("From:", StringComparison.OrdinalIgnoreCase)) return false;
        if (!normalizedText.Contains("Subject:", StringComparison.OrdinalIgnoreCase)) return false;

        // Should also have Date/Sent and To
        var hasDate = normalizedText.Contains("Date:", StringComparison.OrdinalIgnoreCase) ||
                      normalizedText.Contains("Sent:", StringComparison.OrdinalIgnoreCase);
        var hasTo = normalizedText.Contains("To:", StringComparison.OrdinalIgnoreCase);

        // Require at least 3 of 4 markers
        var markerCount = 1 + (hasDate ? 1 : 0) + (hasTo ? 1 : 0) + 1; // From + Subject always present
        if (markerCount < 3) return false;

        // Forwarding headers are typically under 500 chars
        // If the text is longer, it probably includes body content - don't treat as just a header
        if (normalizedText.Length > 500)
        {
            _logger?.LogInformation($"Text too long to be just a forwarding header: {normalizedText.Length} chars");
            return false;
        }

        // Additional check: the markers should be near the beginning of the text
        // A real forwarding header has From: near the start
        var fromIndex = normalizedText.IndexOf("From:", StringComparison.OrdinalIgnoreCase);
        if (fromIndex > 100)
        {
            _logger?.LogInformation($"'From:' found too far into text ({fromIndex}), not a pure header block");
            return false;
        }

        _logger?.LogInformation($"Identified forwarding header block: {normalizedText.Length} chars, markers: {markerCount}");
        return true;
    }

    private HtmlNode? FindForwardingHeaderContainer(HtmlNode node)
    {
        // Walk up the DOM to find a container that ONLY contains the forwarding header
        // We want to be conservative - only remove elements that are clearly just the header
        var current = node;
        HtmlNode? bestContainer = null;

        // Get the forwarding header text to compare against
        var headerText = node.InnerText?.Trim() ?? "";
        var headerTextLength = headerText.Length;
        
        _logger?.LogInformation($"Finding container for header text of length {headerTextLength}");

        while (current != null && current.ParentNode != null)
        {
            var parent = current.ParentNode;

            // Stop at body or document - never go that high
            if (parent.Name == "body" || parent.Name == "#document" || parent.Name == "html") 
            {
                _logger?.LogInformation("Reached body/document, stopping search");
                break;
            }

            // Check if this container is a reasonable size for just a header
            var parentText = parent.InnerText?.Trim() ?? "";
            var parentTextLength = parentText.Length;

            // If the parent contains significantly more text than the header,
            // it probably contains body content too - don't go higher
            if (parentTextLength > headerTextLength * 1.5 && parentTextLength > 600)
            {
                _logger?.LogInformation($"Parent too large ({parentTextLength} chars vs header {headerTextLength} chars), using current best");
                break;
            }

            // This level looks like it's mostly the header, mark it as candidate
            if (!string.IsNullOrEmpty(parentText))
            {
                var ratio = (double)headerTextLength / parentTextLength;
                if (ratio > 0.6) // Header is >60% of parent content
                {
                    bestContainer = parent;
                    _logger?.LogInformation($"Found candidate container: {parent.Name}, ratio: {ratio:F2}");
                }
            }

            // Stop at common boundary elements if we've found something
            if ((current.Name == "div" || current.Name == "tr" || current.Name == "blockquote" || current.Name == "table") 
                && bestContainer != null)
            {
                break;
            }

            current = parent;
        }

        // If we didn't find a good container, just return the original node
        var result = bestContainer ?? node;
        _logger?.LogInformation($"Final container decision: {result.Name}, text length: {result.InnerText?.Length ?? 0}");
        return result;
    }

    private string StripForwardingHeaderPatterns(string html)
    {
        // Multiple regex patterns for different email clients
        var patterns = new[]
        {
            // Pattern 1: Paragraph-wrapped headers with bold (Outlook/Gmail)
            @"<p[^>]*>\s*<b>From:</b>[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*<b>(?:Date|Sent):</b>[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*<b>To:</b>[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*<b>Subject:</b>[^<]*(?:<[^>]+>[^<]*)*</p>",

            // Pattern 2: Line break separated
            @"<b>From:</b>[^<\r\n]*<br[^>]*>\s*" +
            @"<b>(?:Date|Sent):</b>[^<\r\n]*<br[^>]*>\s*" +
            @"<b>To:</b>[^<\r\n]*<br[^>]*>\s*" +
            @"<b>Subject:</b>[^<\r\n]*(?:<br[^>]*>)?",

            // Pattern 3: Strong tags
            @"<p[^>]*>\s*<strong>From:</strong>[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*<strong>(?:Date|Sent):</strong>[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*<strong>To:</strong>[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*<strong>Subject:</strong>[^<]*(?:<[^>]+>[^<]*)*</p>",

            // Pattern 4: Plain text with colons
            @"<p[^>]*>\s*From:[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*(?:Date|Sent):[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*To:[^<]*(?:<[^>]+>[^<]*)*</p>\s*" +
            @"<p[^>]*>\s*Subject:[^<]*(?:<[^>]+>[^<]*)*</p>",

            // Pattern 5: Div-wrapped (common in many clients)
            @"<div[^>]*>\s*(?:<[^>]+>)*From:(?:</[^>]+>)*[^<]*(?:<[^>]+>[^<]*)*</div>\s*" +
            @"<div[^>]*>\s*(?:<[^>]+>)*(?:Date|Sent):(?:</[^>]+>)*[^<]*(?:<[^>]+>[^<]*)*</div>\s*" +
            @"<div[^>]*>\s*(?:<[^>]+>)*To:(?:</[^>]+>)*[^<]*(?:<[^>]+>[^<]*)*</div>\s*" +
            @"<div[^>]*>\s*(?:<[^>]+>)*Subject:(?:</[^>]+>)*[^<]*(?:<[^>]+>[^<]*)*</div>",

            // Pattern 6: Apple Mail style (single block with line breaks)
            @"<blockquote[^>]*>[\s\S]*?From:[^\n]*\n[^\n]*(?:Date|Sent):[^\n]*\n[^\n]*To:[^\n]*\n[^\n]*Subject:[^\n]*",

            // Pattern 7: Table-based forwarding header
            @"<table[^>]*>[\s\S]*?From:[\s\S]*?(?:Date|Sent):[\s\S]*?To:[\s\S]*?Subject:[\s\S]*?</table>",

            // Pattern 8: Gmail's "---------- Forwarded message ---------" style
            @"-{5,}\s*Forwarded\s+message\s*-{5,}[^<]*(?:<br[^>]*>[^<]*)*(?:From|Date|Subject|To):[^<]*(?:<br[^>]*>[^<]*)*"
        };

        foreach (var pattern in patterns)
        {
            var result = Regex.Replace(html, pattern, "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (result != html)
            {
                return result;
            }
        }

        return html;
    }

    private void RemoveSignatures(HtmlDocument doc)
    {
        try
        {
            // Look for common signature containers by ID
            var signatureContainers = new[]
            {
                "//div[@id='Signature']",
                "//div[@id='signature']",
                "//div[contains(@id, 'signature')]",
                "//div[@id='ms-outlook-mobile-signature']"
            };

            foreach (var xpath in signatureContainers)
            {
                var sigs = doc.DocumentNode.SelectNodes(xpath);
                if (sigs != null)
                {
                    foreach (var sig in sigs.ToList())
                    {
                        sig.Remove();
                        _logger?.LogInformation("Removed signature container");
                    }
                }
            }

            // Also look for "Sent from my" patterns in text
            var allText = doc.DocumentNode.SelectNodes("//text()");
            if (allText == null) return;

            foreach (var textNode in allText.Take(500).ToList()) // Limit iterations
            {
                var text = textNode.InnerText ?? "";
                if (text.Contains("Sent from my", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Get Outlook for", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove this node and try to remove parent if it's now empty
                    var parent = textNode.ParentNode;
                    textNode.Remove();
                    if (parent != null && string.IsNullOrWhiteSpace(parent.InnerText))
                    {
                        parent.Remove();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Signature removal failed: {ex.Message}");
        }
    }

    private void CleanQuotedReplies(HtmlDocument doc)
    {
        try
        {
            // Only remove Gmail quote containers - these are clearly reply quotes
            // Do NOT remove Outlook forward containers as they contain the forwarded content
            var gmailQuotes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'gmail_quote')]");
            if (gmailQuotes != null)
            {
                foreach (var quote in gmailQuotes.ToList())
                {
                    quote.Remove();
                    _logger?.LogInformation("Removed Gmail quote container");
                }
            }

            // Remove "appendonsend" divs (Outlook adds these for mobile signatures/ads)
            var appendOnSend = doc.DocumentNode.SelectNodes("//div[@id='appendonsend']");
            if (appendOnSend != null)
            {
                foreach (var div in appendOnSend.ToList())
                {
                    div.Remove();
                    _logger?.LogInformation("Removed appendonsend container");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Quote cleanup failed: {ex.Message}");
        }
    }

    private void SimplifyTables(HtmlDocument doc)
    {
        try
        {
            // For complex email templates with nested tables,
            // the best approach is to strip ALL table markup entirely
            // This preserves text content while removing layout tables

            // Remove table-related tags but keep their content
            var tableElements = new[] { "table", "tbody", "thead", "tfoot", "tr", "td", "th" };

            foreach (var tagName in tableElements)
            {
                var elements = doc.DocumentNode.SelectNodes($"//{tagName}");
                if (elements == null) continue;

                _logger?.LogInformation($"Found {elements.Count} {tagName} elements to unwrap");

                foreach (var element in elements.ToList())
                {
                    try
                    {
                        // Replace the element with its children
                        var parent = element.ParentNode;
                        if (parent == null) continue;

                        // For td/th elements, add a line break after content for readability
                        if (tagName == "td" || tagName == "th")
                        {
                            var br = doc.CreateElement("br");
                            parent.InsertBefore(br, element);
                        }

                        // Move all children to parent
                        foreach (var child in element.ChildNodes.ToList())
                        {
                            parent.InsertBefore(child, element);
                        }

                        element.Remove();
                    }
                    catch
                    {
                        // Skip problematic elements
                    }
                }
            }

            _logger?.LogInformation("Table markup stripped successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Table simplification failed: {ex.Message}");
        }
    }

    private void SafeUnwrapTable(HtmlNode table)
    {
        var parent = table.ParentNode;
        if (parent == null) return;

        // Only get DIRECT cells (not nested table cells)
        // This prevents DOM corruption when tables are nested
        var directCells = new List<HtmlNode>();
        var rows = table.SelectNodes("./tbody/tr | ./tr");
        if (rows != null)
        {
            foreach (var row in rows)
            {
                var cells = row.SelectNodes("./td | ./th");
                if (cells != null)
                {
                    directCells.AddRange(cells);
                }
            }
        }

        if (directCells.Count == 0)
        {
            table.Remove();
            return;
        }

        foreach (var cell in directCells)
        {
            foreach (var child in cell.ChildNodes.ToList())
            {
                parent.InsertBefore(child, table);
            }
            // Add a line break between cells for readability
            var br = table.OwnerDocument.CreateElement("br");
            parent.InsertBefore(br, table);
        }

        table.Remove();
    }

    private void CleanAttributes(HtmlDocument doc)
    {
        // Remove all attributes except essential ones (href, src, alt)
        var allElements = doc.DocumentNode.SelectNodes("//*");
        if (allElements != null)
        {
            foreach (var elem in allElements)
            {
                var attributesToRemove = elem.Attributes
                    .Where(a => a.Name != "href" && a.Name != "src" && a.Name != "alt")
                    .Select(a => a.Name)
                    .ToList();

                foreach (var attrName in attributesToRemove)
                {
                    elem.Attributes.Remove(attrName);
                }
            }
        }

        // Remove width/height from images too (cleaner output)
        var images = doc.DocumentNode.SelectNodes("//img[@width or @height]");
        if (images != null)
        {
            foreach (var img in images)
            {
                img.Attributes["width"]?.Remove();
                img.Attributes["height"]?.Remove();
            }
        }
    }

    private void SimplifyStructure(HtmlDocument doc)
    {
        // Note: Table unwrapping is now handled by SimplifyTables
        // This method only handles cleanup of empty elements

        // Remove empty paragraphs and divs
        RemoveEmptyElements(doc, "p");
        RemoveEmptyElements(doc, "div");
        RemoveEmptyElements(doc, "span");

        // Convert non-breaking spaces to regular spaces in text nodes
        var textNodes = doc.DocumentNode.SelectNodes("//text()");
        if (textNodes != null)
        {
            foreach (var textNode in textNodes)
            {
                if (textNode.InnerText.Contains("&nbsp;") || textNode.InnerText.Contains('\u00A0'))
                {
                    textNode.InnerHtml = textNode.InnerHtml
                        .Replace("&nbsp;", " ")
                        .Replace("\u00A0", " ");
                }
            }
        }
    }

    private void RemoveEmptyElements(HtmlDocument doc, string tagName)
    {
        var elements = doc.DocumentNode.SelectNodes($"//{tagName}");
        if (elements == null) return;

        foreach (var elem in elements.ToList())
        {
            var text = elem.InnerText?.Trim() ?? "";
            // Remove if empty or only whitespace/nbsp
            if (string.IsNullOrWhiteSpace(text.Replace("\u00A0", "").Replace("&nbsp;", "")))
            {
                // Don't remove if it contains images or other meaningful children
                if (elem.SelectNodes(".//img") == null)
                {
                    elem.Remove();
                }
            }
        }
    }

    private string PostProcessMarkdown(string markdown)
    {
        // === Pandoc-specific cleanup ===

        // Remove Pandoc div markers like ::: {#mail-editor-reference-message-container}
        markdown = Regex.Replace(markdown, @"^:::.*$", "", RegexOptions.Multiline);

        // Remove attribute blocks like {outlook-id="..."} or {width="40" height="40"}
        markdown = Regex.Replace(markdown, @"\{[^}]*(?:outlook-id|target|rel|width|height)[^}]*\}", "");

        // Remove standalone backslashes (Pandoc escape artifacts)
        markdown = Regex.Replace(markdown, @"^\\\s*$", "", RegexOptions.Multiline);
        markdown = Regex.Replace(markdown, @"([^\\\n])\\\s*\n", "$1\n"); // backslash at end of line

        // Remove forwarding header block from markdown output
        // Pattern: **From:** ... **Sent:** ... **To:** ... **Subject:** ...
        markdown = Regex.Replace(markdown,
            @"\*\*From:\*\*[^\n]*\n\*\*Sent:\*\*[^\n]*\n\*\*To:\*\*[^\n]*\n\*\*Subject:\*\*[^\n]*\n?",
            "", RegexOptions.IgnoreCase);

        // Also handle non-bold version
        markdown = Regex.Replace(markdown,
            @"From:[^\n]*\nSent:[^\n]*\nTo:[^\n]*\nSubject:[^\n]*\n?",
            "", RegexOptions.IgnoreCase);

        // === General cleanup ===

        // Remove excessive blank lines (more than 2 consecutive)
        markdown = Regex.Replace(markdown, @"\n{4,}", "\n\n\n");

        // Remove trailing whitespace from lines
        markdown = Regex.Replace(markdown, @"[ \t]+\n", "\n");

        // Remove leading/trailing blank lines
        markdown = markdown.Trim();

        // Fix common conversion artifacts
        // Remove empty links like []()
        markdown = Regex.Replace(markdown, @"\[\s*\]\(\s*\)", "");

        // Fix duplicate links where text equals URL: [https://example.com](https://example.com) -> https://example.com
        markdown = Regex.Replace(markdown,
            @"\[([^\]]+)\]\(\1\)",
            m => m.Groups[1].Value);

        // Also handle with trailing slashes or minor differences
        markdown = Regex.Replace(markdown,
            @"\[(https?://[^\]]+?)/?\]\((https?://[^\)]+?)/?\)",
            m =>
            {
                var text = m.Groups[1].Value.TrimEnd('/');
                var url = m.Groups[2].Value.TrimEnd('/');
                // If they're the same (ignoring trailing slash), just use the URL
                if (text.Equals(url, StringComparison.OrdinalIgnoreCase))
                    return m.Groups[2].Value;
                return m.Value; // Keep original if different
            });

        // Clean up mailto links that duplicate the email
        markdown = Regex.Replace(markdown,
            @"\[([^\]@]+@[^\]]+)\]\(mailto:\1\)",
            m => m.Groups[1].Value);

        // Remove empty bold/italic markers
        markdown = Regex.Replace(markdown, @"\*\*\s*\*\*", "");
        markdown = Regex.Replace(markdown, @"\*\s*\*", "");
        markdown = Regex.Replace(markdown, @"__\s*__", "");
        markdown = Regex.Replace(markdown, @"_\s*_", "");

        // Clean up multiple spaces
        markdown = Regex.Replace(markdown, @"  +", " ");

        // Ensure proper spacing around headers
        markdown = Regex.Replace(markdown, @"(^|\n)(#{1,6})\s*([^\n]+)", "$1$2 $3");

        // Clean up orphaned quote indicators from reply chains
        markdown = Regex.Replace(markdown, @"^>\s*$", "", RegexOptions.Multiline);
        markdown = Regex.Replace(markdown, @"(\n>\s*){3,}", "\n>\n");

        // Remove lines that are just horizontal rules stacked together
        markdown = Regex.Replace(markdown, @"(\n---\n){2,}", "\n---\n");

        // Final cleanup - remove lines that are just whitespace
        markdown = Regex.Replace(markdown, @"^\s+$", "", RegexOptions.Multiline);
        markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n");

        return markdown;
    }

    public byte[] ConvertToMarkdownBytes(
        string subject,
        string senderName,
        string senderEmail,
        DateTime receivedDateTime,
        string bodyHtml)
    {
        var markdown = $"""
            # {subject}

            **From:** {senderName} ({senderEmail})
            **Received:** {receivedDateTime:yyyy-MM-dd HH:mm:ss}

            ---

            {ConvertHtmlToMarkdown(bodyHtml)}
            """;

        return System.Text.Encoding.UTF8.GetBytes(markdown);
    }

    /// <summary>
    /// Checks if an email subject indicates it's a forwarded message.
    /// </summary>
    public static bool IsForwardedEmail(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        var trimmed = subject.TrimStart();
        return trimmed.StartsWith("FW:", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("Fwd:", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("FWD:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips forwarding prefixes (FW:, Fwd:, RE:, Re:) from the subject line.
    /// </summary>
    public static string StripForwardingPrefix(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return subject;

        // Pattern to match multiple FW:/Fwd:/RE:/Re: prefixes at the start
        var pattern = @"^(\s*(FW|Fwd|RE|Re)\s*:\s*)+";
        return Regex.Replace(subject, pattern, "", RegexOptions.IgnoreCase).Trim();
    }

    /// <summary>
    /// Extracts original sender metadata from a forwarded email's HTML body.
    /// Returns the original sender name, email, and sent date if found.
    /// </summary>
    public ForwardedEmailMetadata? ExtractForwardedMetadata(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        _logger?.LogInformation("Attempting to extract forwarded email metadata");

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Try DOM-based extraction first
        var metadata = ExtractMetadataFromDom(doc);
        if (metadata != null)
        {
            _logger?.LogInformation($"Extracted metadata via DOM: From={metadata.SenderName} <{metadata.SenderEmail}>, Date={metadata.SentDate}");
            return metadata;
        }

        // Fall back to regex-based extraction on raw HTML
        metadata = ExtractMetadataFromRegex(html);
        if (metadata != null)
        {
            _logger?.LogInformation($"Extracted metadata via regex: From={metadata.SenderName} <{metadata.SenderEmail}>, Date={metadata.SentDate}");
        }
        else
        {
            _logger?.LogWarning("Could not extract forwarded email metadata");
        }

        return metadata;
    }

    private ForwardedEmailMetadata? ExtractMetadataFromDom(HtmlDocument doc)
    {
        // Look for Outlook mobile forwarding header div
        var outlookHeader = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'ms-outlook-mobile-reference-message')]");
        if (outlookHeader != null)
        {
            return ExtractMetadataFromHeaderNode(outlookHeader);
        }

        // Look for nodes containing forwarding header patterns
        var candidates = doc.DocumentNode.SelectNodes("//*[contains(text(), 'From:') and contains(., 'Sent:') or contains(., 'Date:')]");
        if (candidates != null)
        {
            foreach (var candidate in candidates)
            {
                var text = candidate.InnerText ?? "";
                if (IsForwardingHeaderBlock(text))
                {
                    var metadata = ExtractMetadataFromText(text);
                    if (metadata != null) return metadata;
                }
            }
        }

        return null;
    }

    private ForwardedEmailMetadata? ExtractMetadataFromHeaderNode(HtmlNode node)
    {
        var text = node.InnerText ?? "";
        return ExtractMetadataFromText(text);
    }

    private ForwardedEmailMetadata? ExtractMetadataFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string? senderName = null;
        string? senderEmail = null;
        DateTime? sentDate = null;

        // Extract From: field
        var fromMatch = Regex.Match(text, @"From:\s*([^<\n\r]+?)\s*<([^>]+)>", RegexOptions.IgnoreCase);
        if (fromMatch.Success)
        {
            senderName = fromMatch.Groups[1].Value.Trim();
            senderEmail = fromMatch.Groups[2].Value.Trim();
        }
        else
        {
            // Try simpler pattern: From: email@address.com
            fromMatch = Regex.Match(text, @"From:\s*([\w\.\-]+@[\w\.\-]+\.\w+)", RegexOptions.IgnoreCase);
            if (fromMatch.Success)
            {
                senderEmail = fromMatch.Groups[1].Value.Trim();
                senderName = senderEmail.Split('@')[0];
            }
        }

        // Extract Date/Sent field
        var dateMatch = Regex.Match(text, @"(?:Date|Sent):\s*([^\n\r]+?)(?=\s*(?:To:|Subject:|$))", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (dateMatch.Success)
        {
            var dateStr = dateMatch.Groups[1].Value.Trim();
            // Try various date formats
            if (DateTime.TryParse(dateStr, out var parsed))
            {
                sentDate = parsed;
            }
            else
            {
                // Try common email date formats
                var formats = new[] {
                    "dddd, MMMM d, yyyy h:mm tt",
                    "MMMM d, yyyy h:mm tt",
                    "M/d/yyyy h:mm:ss tt",
                    "yyyy-MM-dd HH:mm:ss",
                    "ddd, dd MMM yyyy HH:mm:ss"
                };
                foreach (var fmt in formats)
                {
                    if (DateTime.TryParseExact(dateStr, fmt, null, System.Globalization.DateTimeStyles.None, out parsed))
                    {
                        sentDate = parsed;
                        break;
                    }
                }
            }
        }

        if (senderEmail != null || sentDate != null)
        {
            return new ForwardedEmailMetadata
            {
                SenderName = senderName ?? "",
                SenderEmail = senderEmail ?? "",
                SentDate = sentDate
            };
        }

        return null;
    }

    private ForwardedEmailMetadata? ExtractMetadataFromRegex(string html)
    {
        // Strip HTML tags for easier regex matching
        var plainText = Regex.Replace(html, "<[^>]+>", " ");
        plainText = System.Net.WebUtility.HtmlDecode(plainText);
        plainText = Regex.Replace(plainText, @"\s+", " ");

        return ExtractMetadataFromText(plainText);
    }
}

/// <summary>
/// Holds metadata extracted from a forwarded email's header block.
/// </summary>
public class ForwardedEmailMetadata
{
    public string SenderName { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public DateTime? SentDate { get; set; }
}
