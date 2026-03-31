using Serilog;

namespace DaisyReport.Api.SpreadsheetServer.Distribution.Channels;

public class FileSystemChannel : IDistributionChannel
{
    private readonly ILogger<FileSystemChannel> _logger;

    public FileSystemChannel(ILogger<FileSystemChannel> logger)
    {
        _logger = logger;
    }

    public async Task<bool> DeliverAsync(string fileName, byte[] content, string contentType,
        ChannelConfig config, CancellationToken ct = default)
    {
        var outputDir = config.Settings.GetValueOrDefault("path", "output");

        try
        {
            // Ensure directory exists
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Sanitize filename
            var safeName = SanitizeFileName(fileName);
            var fullPath = Path.Combine(outputDir, safeName);

            // If file already exists, add a timestamp suffix
            if (File.Exists(fullPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
                var ext = Path.GetExtension(safeName);
                safeName = $"{nameWithoutExt}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{ext}";
                fullPath = Path.Combine(outputDir, safeName);
            }

            await File.WriteAllBytesAsync(fullPath, content, ct);
            _logger.LogInformation("File saved: {Path} ({Size} bytes)", fullPath, content.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file to {Dir}/{FileName}", outputDir, fileName);
            return false;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "report" : sanitized;
    }
}
