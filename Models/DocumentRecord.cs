using System.Text.Json.Serialization;

namespace mastery_task.Models;

public class DocumentRecord
{
    public int Id { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string? SupplierName { get; set; }
    public string? DocumentNumber { get; set; }
    public string? IssueDate { get; set; }
    public string? DueDate { get; set; }
    public string? Currency { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Total { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string Status { get; set; } = DocumentStatuses.Uploaded;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public List<LineItem> LineItems { get; set; } = [];
    public List<ValidationIssue> Issues { get; set; } = [];
}

public class LineItem
{
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Total { get; set; }
}

public class ValidationIssue
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string Message { get; set; } = string.Empty;
}

public static class DocumentStatuses
{
    public const string Uploaded = "Uploaded";
    public const string NeedsReview = "Needs Review";
    public const string Validated = "Validated";
    public const string Rejected = "Rejected";

    [JsonIgnore]
    public static readonly string[] All = [Uploaded, NeedsReview, Validated, Rejected];
}
