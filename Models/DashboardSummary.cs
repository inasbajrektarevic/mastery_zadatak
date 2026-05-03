namespace mastery_task.Models;

public class DashboardSummary
{
    public int TotalDocuments { get; set; }
    public int TotalIssues { get; set; }
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public Dictionary<string, decimal> TotalsByCurrency { get; set; } = new();
}
