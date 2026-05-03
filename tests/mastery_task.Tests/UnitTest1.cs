using mastery_task.Models;
using mastery_task.Services;
using mastery_task.Services.Ocr;

namespace mastery_task.Tests;

public class UnitTest1
{
    private readonly DocumentProcessingService _service = new(new FakeOcrService());

    [Fact]
    public void ParseRawText_ExtractsTotalWithoutCurrencySuffix()
    {
        var raw = """
                  Invoice
                  Supplier: Company 0
                  Number: INV-1000
                  Date: 2026-04-28
                  Subtotal 645
                  Tax (20%) 129.0
                  Total 800.0
                  """;

        var doc = _service.ParseRawText(raw, "invoice_1.pdf");

        Assert.Equal(800.0m, doc.Total);
    }

    [Fact]
    public void ParseRawText_ExtractsExpectedCoreFields()
    {
        var raw = """
                  Invoice
                  Supplier: Company 0
                  Number: INV-1000
                  Date: 2026-04-28
                  Description Qty Unit Price Total
                  Service A 5 129 645
                  Subtotal 645
                  Tax (20%) 129.0
                  Total 800.0 BAM
                  """;

        var doc = _service.ParseRawText(raw, "invoice_1.pdf");

        Assert.Equal("invoice", doc.DocumentType);
        Assert.Equal("Company 0", doc.SupplierName);
        Assert.Equal("INV-1000", doc.DocumentNumber);
        Assert.Equal("2026-04-28", doc.IssueDate);
        Assert.Equal("BAM", doc.Currency);
        Assert.Single(doc.LineItems);
    }

    [Fact]
    public void ValidateDocument_FlagsDuplicateAndTotalMismatch()
    {
        var doc = new DocumentRecord
        {
            DocumentType = "invoice",
            SupplierName = "Acme",
            DocumentNumber = "INV-42",
            IssueDate = "2026-04-28",
            Currency = "EUR",
            Subtotal = 100m,
            Tax = 20m,
            Total = 150m
        };

        var issues = _service.ValidateDocument(doc, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INV-42" });

        Assert.Contains(issues, i => i.Code == "DUPLICATE_DOCUMENT_NUMBER");
        Assert.Contains(issues, i => i.Code == "TOTAL_MISMATCH");
        Assert.Equal(DocumentStatuses.NeedsReview, doc.Status);
    }

    [Fact]
    public void ValidateDocument_FlagsMissingRequiredFields()
    {
        var doc = new DocumentRecord();

        var issues = _service.ValidateDocument(doc, []);

        Assert.True(issues.Count(i => i.Code == "MISSING_FIELD") >= 6);
    }

    private sealed class FakeOcrService : IOcrService
    {
        public string? TryExtractText(string imagePath) => null;
    }
}
