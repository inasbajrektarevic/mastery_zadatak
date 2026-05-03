using System.Text.Json;
using mastery_task.Data;
using mastery_task.Models;
using mastery_task.Services;
using Microsoft.AspNetCore.Mvc;

namespace mastery_task.Controllers;

public class DocumentsController : Controller
{
    private static readonly HashSet<string> ProcessableExtensions =
    [
        ".pdf", ".txt", ".csv", ".png", ".jpg", ".jpeg", ".webp"
    ];

    private readonly IWebHostEnvironment _env;
    private readonly DocumentRepository _repository;
    private readonly DocumentProcessingService _processor;

    private static bool IsProcessableDataFile(string path) =>
        ProcessableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public DocumentsController(IWebHostEnvironment env, DocumentRepository repository, DocumentProcessingService processor)
    {
        _env = env;
        _repository = repository;
        _processor = processor;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var docs = _repository.GetAll();
        var dashboard = new DashboardSummary
        {
            TotalDocuments = docs.Count,
            TotalIssues = docs.Sum(d => d.Issues.Count),
            StatusCounts = docs
                .GroupBy(d => d.Status)
                .ToDictionary(g => g.Key, g => g.Count()),
            TotalsByCurrency = docs
                .Where(d => !string.IsNullOrWhiteSpace(d.Currency) && d.Total.HasValue)
                .GroupBy(d => d.Currency!)
                .ToDictionary(g => g.Key, g => Math.Round(g.Sum(x => x.Total ?? 0), 2))
        };
        var vm = new DocumentsIndexViewModel { Documents = docs, Dashboard = dashboard };
        return View(vm);
    }

    [HttpPost]
    public IActionResult ProcessResources()
    {
        var resourceDir = Path.Combine(_env.ContentRootPath, "resources");
        if (!Directory.Exists(resourceDir))
        {
            TempData["Error"] = "resources folder not found.";
            return RedirectToAction(nameof(Index));
        }

        var allPaths = Directory.GetFiles(resourceDir);
        var files = allPaths.Where(IsProcessableDataFile).ToArray();
        var ignored = allPaths.Length - files.Length;
        var result = ProcessFiles(files);
        var msg =
            $"Processed {result.Inserted} file(s), skipped {result.SkippedDuplicates} duplicate(s), skipped {result.SkippedUnsupported} unsupported file(s).";
        if (ignored > 0)
        {
            msg += $" ({ignored} ignored in folder: not .pdf/.txt/.csv/.image — e.g. README.md)";
        }

        TempData["Success"] = msg;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Upload(List<IFormFile> files)
    {
        if (files is null || files.Count == 0 || files.All(f => f.Length == 0))
        {
            TempData["Error"] = "Nijedan fajl nije odabran. Klikni Choose Files, odaberi dokumente, pa Upload and process.";
            return RedirectToAction(nameof(Index));
        }

        var uploadDir = Path.Combine(_env.ContentRootPath, ".uploads");
        Directory.CreateDirectory(uploadDir);

        var paths = new List<string>();
        foreach (var file in files)
        {
            var path = Path.Combine(uploadDir, file.FileName);
            await using var stream = System.IO.File.Create(path);
            await file.CopyToAsync(stream);
            paths.Add(path);
        }

        var result = ProcessFiles(paths);
        TempData["Success"] =
            $"Processed {result.Inserted} uploaded file(s), skipped {result.SkippedDuplicates} duplicate(s), skipped {result.SkippedUnsupported} unsupported file(s).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult Reset()
    {
        _repository.DeleteAll();
        TempData["Success"] = "Database reset complete.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Review(int id)
    {
        var doc = _repository.GetById(id);
        if (doc is null)
        {
            return NotFound();
        }

        return View(doc);
    }

    [HttpPost]
    public IActionResult Review(
        int id,
        string? documentType,
        string? supplierName,
        string? documentNumber,
        string? issueDate,
        string? dueDate,
        string? currency,
        decimal? subtotal,
        decimal? tax,
        decimal? total,
        string? lineItemsJson,
        string? rawText,
        string status)
    {
        var doc = _repository.GetById(id);
        if (doc is null)
        {
            return NotFound();
        }

        doc.DocumentType = string.IsNullOrWhiteSpace(documentType) ? null : documentType.Trim();
        doc.SupplierName = string.IsNullOrWhiteSpace(supplierName) ? null : supplierName.Trim();
        doc.DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim();
        doc.IssueDate = string.IsNullOrWhiteSpace(issueDate) ? null : issueDate.Trim();
        doc.DueDate = string.IsNullOrWhiteSpace(dueDate) ? null : dueDate.Trim();
        doc.Currency = string.IsNullOrWhiteSpace(currency) ? null : currency.Trim().ToUpperInvariant();
        doc.Subtotal = subtotal;
        doc.Tax = tax;
        doc.Total = total;
        if (rawText != null)
        {
            doc.RawText = rawText;
        }

        if (!string.IsNullOrWhiteSpace(lineItemsJson))
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                doc.LineItems = JsonSerializer.Deserialize<List<LineItem>>(lineItemsJson.Trim(), options) ?? [];
            }
            catch (JsonException)
            {
                TempData["Error"] = "Line items JSON is invalid. Fix the JSON syntax and try again.";
                return View(doc);
            }
        }

        var requested = DocumentStatuses.All.Contains(status) ? status : DocumentStatuses.NeedsReview;
        var otherNumbers = _repository.GetDocumentNumbersExcluding(doc.Id);
        _processor.ValidateDocument(doc, otherNumbers);

        if (requested == DocumentStatuses.Rejected)
        {
            doc.Status = DocumentStatuses.Rejected;
        }
        else if (doc.Issues.Count > 0)
        {
            doc.Status = DocumentStatuses.NeedsReview;
            if (requested == DocumentStatuses.Validated)
            {
                TempData["Error"] = "Cannot mark as Validated while validation issues remain. Fix fields or reject the document.";
            }
        }
        else
        {
            doc.Status = requested;
        }

        _repository.UpdateReview(doc);
        TempData["Success"] = "Document saved and re-validated.";
        return RedirectToAction(nameof(Review), new { id });
    }

    private ProcessResult ProcessFiles(IEnumerable<string> files)
    {
        var numbers = _repository.GetDocumentNumbers();
        var inserted = 0;
        var skippedDuplicates = 0;
        var skippedUnsupported = 0;
        foreach (var path in files)
        {
            if (!IsProcessableDataFile(path))
            {
                skippedUnsupported++;
                continue;
            }

            var sourceFile = Path.GetFileName(path);
            if (_repository.ExistsBySourceFile(sourceFile))
            {
                skippedDuplicates++;
                continue;
            }

            DocumentRecord doc;
            try
            {
                doc = _processor.ProcessFile(path, numbers);
            }
            catch (Exception ex)
            {
                doc = new DocumentRecord
                {
                    SourceFile = sourceFile,
                    DocumentType = null,
                    DocumentNumber = Path.GetFileNameWithoutExtension(sourceFile).ToUpperInvariant(),
                    RawText = $"Unhandled processing exception: {ex.Message}",
                    Status = DocumentStatuses.NeedsReview,
                    Issues =
                    [
                        new ValidationIssue
                        {
                            Code = "PROCESSING_EXCEPTION",
                            Severity = "error",
                            Message = "Document processing failed unexpectedly. Please review manually."
                        }
                    ]
                };
            }

            if (!string.IsNullOrWhiteSpace(doc.DocumentNumber))
            {
                numbers.Add(doc.DocumentNumber);
            }
            _repository.Insert(doc);
            inserted++;
        }

        return new ProcessResult(inserted, skippedDuplicates, skippedUnsupported);
    }

    private readonly record struct ProcessResult(int Inserted, int SkippedDuplicates, int SkippedUnsupported);
}
