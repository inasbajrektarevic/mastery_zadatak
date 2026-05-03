namespace mastery_task.Models;

public class DocumentsIndexViewModel
{
    public DashboardSummary Dashboard { get; set; } = new();
    public List<DocumentRecord> Documents { get; set; } = [];
}
