namespace mastery_task.Services.Ocr;

public interface IOcrService
{
    string? TryExtractText(string imagePath);
}
