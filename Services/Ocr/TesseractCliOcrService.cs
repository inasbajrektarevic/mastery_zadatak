using System.Diagnostics;
using System.Text;

namespace mastery_task.Services.Ocr;

public class TesseractCliOcrService : IOcrService
{
    public string? TryExtractText(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            var outputBase = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}");
            var startInfo = new ProcessStartInfo
            {
                FileName = "tesseract",
                Arguments = $"\"{imagePath}\" \"{outputBase}\" --dpi 300",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            process.WaitForExit(15000);
            var txtPath = $"{outputBase}.txt";
            if (process.ExitCode != 0 || !File.Exists(txtPath))
            {
                _ = process.StandardError.ReadToEnd();
                return null;
            }

            var text = File.ReadAllText(txtPath, Encoding.UTF8).Trim();
            File.Delete(txtPath);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
