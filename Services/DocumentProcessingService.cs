using System.Globalization;
using System.Text.RegularExpressions;
using mastery_task.Models;
using mastery_task.Services.Ocr;
using UglyToad.PdfPig;

namespace mastery_task.Services;

public class DocumentProcessingService
{
    private readonly IOcrService _ocrService;

    public DocumentProcessingService(IOcrService ocrService)
    {
        _ocrService = ocrService;
    }

    public DocumentRecord ProcessFile(string filePath, HashSet<string> existingNumbers)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ProcessPdf(filePath, existingNumbers),
            ".txt" => ProcessTxt(filePath, existingNumbers),
            ".csv" => ProcessCsv(filePath, existingNumbers),
            ".png" or ".jpg" or ".jpeg" or ".webp" => ProcessImage(filePath, existingNumbers),
            _ => ProcessFallback(filePath, existingNumbers)
        };
    }

    private DocumentRecord ProcessPdf(string path, HashSet<string> existingNumbers)
    {
        var raw = ExtractPdfText(path);
        DocumentRecord doc;
        if (string.IsNullOrWhiteSpace(raw))
        {
            doc = new DocumentRecord
            {
                SourceFile = Path.GetFileName(path),
                DocumentType = "invoice",
                DocumentNumber = Path.GetFileNameWithoutExtension(path).ToUpperInvariant(),
                RawText = "PDF text extraction failed for this file. Please review manually."
            };
        }
        else
        {
            doc = ParseCommon(raw, Path.GetFileName(path));
            doc.SourceFile = Path.GetFileName(path);
        }

        ApplyValidation(doc, existingNumbers);
        if (string.IsNullOrWhiteSpace(raw))
        {
            doc.Issues.Add(new ValidationIssue
            {
                Code = "PDF_EXTRACTION_FAILED",
                Severity = "warning",
                Message = "Could not parse PDF text (unsupported/invalid font encoding)."
            });
            doc.Status = DocumentStatuses.NeedsReview;
        }

        return doc;
    }

    private DocumentRecord ProcessTxt(string path, HashSet<string> existingNumbers)
    {
        var raw = File.ReadAllText(path);
        var doc = ParseCommon(raw, Path.GetFileName(path));
        doc.SourceFile = Path.GetFileName(path);
        ApplyValidation(doc, existingNumbers);
        return doc;
    }

    private DocumentRecord ProcessCsv(string path, HashSet<string> existingNumbers)
    {
        var rows = File.ReadAllLines(path).Skip(1).Where(x => !string.IsNullOrWhiteSpace(x));
        var lineItems = new List<LineItem>();
        decimal subtotal = 0m;
        foreach (var row in rows)
        {
            var parts = row.Split(',');
            if (parts.Length < 4) continue;
            var qty = ParseDecimal(parts[1]);
            var unitPrice = ParseDecimal(parts[2]);
            var total = ParseDecimal(parts[3]);
            lineItems.Add(new LineItem
            {
                Description = parts[0],
                Quantity = qty,
                UnitPrice = unitPrice,
                Total = total
            });
            subtotal += total ?? 0m;
        }

        var doc = new DocumentRecord
        {
            SourceFile = Path.GetFileName(path),
            DocumentType = "invoice",
            DocumentNumber = Path.GetFileNameWithoutExtension(path).ToUpperInvariant(),
            LineItems = lineItems,
            Subtotal = subtotal,
            Tax = 0m,
            Total = subtotal,
            RawText = File.ReadAllText(path)
        };
        ApplyValidation(doc, existingNumbers);
        return doc;
    }

    private DocumentRecord ProcessImage(string path, HashSet<string> existingNumbers)
    {
        var ocrText = _ocrService.TryExtractText(path);
        DocumentRecord doc;
        if (!string.IsNullOrWhiteSpace(ocrText))
        {
            doc = ParseCommon(ocrText, Path.GetFileName(path));
            doc.SourceFile = Path.GetFileName(path);
        }
        else
        {
            var filename = Path.GetFileNameWithoutExtension(path);
            doc = new DocumentRecord
            {
                SourceFile = Path.GetFileName(path),
                DocumentType = "invoice",
                DocumentNumber = filename.ToUpperInvariant(),
                RawText = "OCR not available. Install Tesseract and ensure `tesseract` is in PATH."
            };
        }

        ApplyValidation(doc, existingNumbers);
        return doc;
    }

    private DocumentRecord ProcessFallback(string path, HashSet<string> existingNumbers)
    {
        var doc = new DocumentRecord
        {
            SourceFile = Path.GetFileName(path),
            DocumentType = null,
            DocumentNumber = Path.GetFileNameWithoutExtension(path),
            RawText = string.Empty
        };
        ApplyValidation(doc, existingNumbers);
        return doc;
    }

    private static string ExtractPdfText(string path)
    {
        try
        {
            using var pdf = PdfDocument.Open(path);
            return string.Join("\n", pdf.GetPages().Select(p => p.Text));
        }
        catch
        {
            return string.Empty;
        }
    }

    public DocumentRecord ParseRawText(string raw, string sourceFile)
    {
        return ParseCommon(raw, sourceFile);
    }

    public List<ValidationIssue> ValidateDocument(DocumentRecord doc, HashSet<string> existingNumbers)
    {
        ApplyValidation(doc, existingNumbers);
        return doc.Issues;
    }

    private DocumentRecord ParseCommon(string raw, string sourceFile)
    {
        raw = NormalizeExtractedText(raw);

        var doc = new DocumentRecord
        {
            SourceFile = sourceFile,
            RawText = raw
        };

        var lower = raw.ToLowerInvariant();
        if (lower.Contains("purchase order") || sourceFile.StartsWith("po_", StringComparison.OrdinalIgnoreCase))
        {
            doc.DocumentType = "purchase_order";
        }
        else if (lower.Contains("invoice") || lower.Contains("facture"))
        {
            doc.DocumentType = "invoice";
        }

        doc.SupplierName = Match(raw, @"Supplier:\s*([^\r\n]+?)(?:\s+Number:|\s+Date:|$)")
            ?? Match(raw, @"Organization\s*[:\-]?\s*(.+)")
            ?? Match(raw, @"INVOICE TO\s*(.+)");

        doc.DocumentNumber = Match(raw, @"Number:\s*([A-Za-z]{2,}-\d+)")
            ?? Match(raw, @"Invoice No[:#\s]*([A-Za-z0-9\-\/]+)")
            ?? Match(raw, @"INVOICE\s*#\s*([A-Za-z0-9\-\/]+)")
            ?? Match(raw, @"PO Number\s*[:#]?\s*([A-Za-z0-9\-\/]+)");

        doc.IssueDate = Match(raw, @"Date:\s*([0-9]{4}-[0-9]{2}-[0-9]{2})")
            ?? Match(raw, @"Dated:\s*([0-9]{1,2}-[A-Za-z]{3}-[0-9]{4})")
            ?? Match(raw, @"Date de Facturation\s*:?\s*([0-9]{2}\/[0-9]{2}\/[0-9]{4})");

        doc.DueDate = Match(raw, @"Due Date:\s*([0-9]{1,2}\s+[A-Za-z]+\s+[0-9]{4})")
            ?? Match(raw, @"Date d['’]échéance\s*:?\s*([0-9]{2}\/[0-9]{2}\/[0-9]{4})");

        doc.Currency = Match(raw, @"\b(EUR|USD|BAM|GBP|AED)\b")?.ToUpperInvariant();

        doc.Subtotal = ParseDecimal(Match(raw, @"Subtotal(?: without VAT)?\s*([0-9.,]+)"));
        doc.Tax = ParseDecimal(Match(raw, @"Tax(?: due)?\s*(?:\([0-9]{1,2}%\))?\s*([0-9.,]+)"));
        // Avoid `(?: GBP| Due| TTC|)` with a trailing `|` — empty branch matches inside "Subtotal 645".
        doc.Total = ParseDecimal(Match(raw, @"Total(?:\s+GBP|\s+Due|\s+TTC)\s*[: ]\s*([0-9.,]+)"))
            ?? ParseDecimal(Match(raw, @"Grand Total\s*\$?([0-9.,]+)"))
            ?? ParseDecimal(Match(raw, @"Total:\s*([0-9.,]+)\s*(EUR|USD|BAM|GBP|AED)?"))
            ?? ParseDecimal(MatchTotalNotSubtotal(raw));

        if (string.IsNullOrWhiteSpace(doc.Currency))
        {
            var totalWithCurrency = Regex.Match(
                raw,
                @"Total\s+[\d.,]+\s+(EUR|USD|BAM|GBP|AED)\b",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (totalWithCurrency.Success)
            {
                doc.Currency = totalWithCurrency.Groups[1].Value.ToUpperInvariant();
            }
        }

        doc.LineItems = ParseLineItems(raw);
        return doc;
    }

    private static List<LineItem> ParseLineItems(string raw)
    {
        var lines = raw.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l));
        var items = new List<LineItem>();
        var regex = new Regex(@"^(.*?)\s+(\d+)\s+([\d.,]+)\s+([\d.,]+)$", RegexOptions.IgnoreCase);
        foreach (var line in lines)
        {
            var m = regex.Match(line);
            if (!m.Success) continue;
            items.Add(new LineItem
            {
                Description = m.Groups[1].Value.Trim(),
                Quantity = ParseDecimal(m.Groups[2].Value),
                UnitPrice = ParseDecimal(m.Groups[3].Value),
                Total = ParseDecimal(m.Groups[4].Value)
            });
        }

        return items;
    }

    private void ApplyValidation(DocumentRecord doc, HashSet<string> existingNumbers)
    {
        var issues = new List<ValidationIssue>();
        var required = new[] { nameof(doc.DocumentType), nameof(doc.SupplierName), nameof(doc.DocumentNumber), nameof(doc.IssueDate), nameof(doc.Currency), nameof(doc.Total) };

        foreach (var field in required)
        {
            var val = field switch
            {
                nameof(doc.DocumentType) => doc.DocumentType,
                nameof(doc.SupplierName) => doc.SupplierName,
                nameof(doc.DocumentNumber) => doc.DocumentNumber,
                nameof(doc.IssueDate) => doc.IssueDate,
                nameof(doc.Currency) => doc.Currency,
                nameof(doc.Total) => doc.Total?.ToString(CultureInfo.InvariantCulture),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(val))
            {
                issues.Add(new ValidationIssue { Code = "MISSING_FIELD", Severity = "warning", Message = $"Missing required field: {field}" });
            }
        }

        if (!string.IsNullOrWhiteSpace(doc.IssueDate) && NormalizeDate(doc.IssueDate) is null)
        {
            issues.Add(new ValidationIssue { Code = "INVALID_DATE", Severity = "error", Message = "Issue date format is invalid." });
        }

        var issueDate = NormalizeDate(doc.IssueDate);
        var dueDate = NormalizeDate(doc.DueDate);
        if (issueDate.HasValue && dueDate.HasValue && dueDate.Value < issueDate.Value)
        {
            issues.Add(new ValidationIssue { Code = "INVALID_DATE_ORDER", Severity = "error", Message = "Due date is before issue date." });
        }

        decimal lineItemsTotal = 0m;
        for (int i = 0; i < doc.LineItems.Count; i++)
        {
            var item = doc.LineItems[i];
            if (!item.Quantity.HasValue || !item.UnitPrice.HasValue || !item.Total.HasValue)
            {
                issues.Add(new ValidationIssue { Code = "LINE_ITEM_MISSING_VALUE", Severity = "warning", Message = $"Line item {i + 1} has missing values." });
                continue;
            }

            var expected = Math.Round(item.Quantity.Value * item.UnitPrice.Value, 2);
            if (Math.Abs(expected - item.Total.Value) > 0.01m)
            {
                issues.Add(new ValidationIssue { Code = "LINE_ITEM_MISMATCH", Severity = "error", Message = $"Line item {i + 1} mismatch. Expected {expected}, got {item.Total}." });
            }

            lineItemsTotal += item.Total.Value;
        }

        if (doc.Subtotal.HasValue && doc.LineItems.Count > 0 && Math.Abs(doc.Subtotal.Value - lineItemsTotal) > 0.01m)
        {
            issues.Add(new ValidationIssue { Code = "SUBTOTAL_MISMATCH", Severity = "error", Message = $"Subtotal mismatch. Expected {lineItemsTotal}, got {doc.Subtotal}." });
        }

        if (doc.Subtotal.HasValue && doc.Tax.HasValue && doc.Total.HasValue)
        {
            var expectedTotal = Math.Round(doc.Subtotal.Value + doc.Tax.Value, 2);
            if (Math.Abs(expectedTotal - doc.Total.Value) > 0.01m)
            {
                issues.Add(new ValidationIssue { Code = "TOTAL_MISMATCH", Severity = "error", Message = $"Total mismatch. Expected {expectedTotal}, got {doc.Total}." });
            }
        }

        if (!string.IsNullOrWhiteSpace(doc.DocumentNumber) && existingNumbers.Contains(doc.DocumentNumber))
        {
            issues.Add(new ValidationIssue { Code = "DUPLICATE_DOCUMENT_NUMBER", Severity = "error", Message = $"Duplicate document number: {doc.DocumentNumber}" });
        }

        doc.Issues = issues;
        doc.Status = issues.Count == 0 ? DocumentStatuses.Validated : DocumentStatuses.NeedsReview;
    }

    private static string? Match(string input, string pattern)
    {
        var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>Matches invoice total lines; avoids the "total" inside "Subtotal".</summary>
    private static string? MatchTotalNotSubtotal(string raw)
    {
        // Prefer whole-line "Total ..." (PdfPig often emits cleaner line breaks here).
        var line = Regex.Match(raw, @"(?m)^\s*Total\s+([\d.,]+)\b", RegexOptions.IgnoreCase);
        if (line.Success)
        {
            return line.Groups[1].Value.Trim();
        }

        // "Total800.0" or trailing garbage on same line
        var compact = Regex.Match(raw, @"(?<![A-Za-z])Total\s*([\d.,]+)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return compact.Success ? compact.Groups[1].Value.Trim() : null;
    }

    private static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalized = raw.Replace(",", "", StringComparison.Ordinal).Trim();
        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTime? NormalizeDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d-MMM-yyyy", "d MMMM yyyy", "M/d/yyyy", "dd-MMM-yyyy" };
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(raw.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt.Date;
            }
        }

        return DateTime.TryParse(raw, out var genericDt) ? genericDt.Date : null;
    }

    private static string NormalizeExtractedText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = raw.Replace("\r", "\n");
        // Do not insert "\n" before bare "Total" — it matches inside "Subtotal" and breaks amounts.
        var labels = new[]
        {
            "Supplier:",
            "Number:",
            "Date:",
            "Description",
            "Subtotal",
            "Tax"
        };

        foreach (var label in labels)
        {
            normalized = Regex.Replace(
                normalized,
                $@"(?<!\n){Regex.Escape(label)}",
                $"\n{label}",
                RegexOptions.IgnoreCase
            );
        }

        normalized = Regex.Replace(normalized, @"\n{2,}", "\n");
        return normalized.Trim();
    }
}
