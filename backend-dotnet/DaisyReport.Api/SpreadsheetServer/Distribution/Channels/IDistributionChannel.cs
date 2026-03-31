namespace DaisyReport.Api.SpreadsheetServer.Distribution.Channels;

public interface IDistributionChannel
{
    Task<bool> DeliverAsync(string fileName, byte[] content, string contentType,
        ChannelConfig config, CancellationToken ct = default);
}

public class ChannelConfig
{
    public string ChannelType { get; set; } = ""; // EMAIL, SFTP, FILESYSTEM
    public Dictionary<string, string> Settings { get; set; } = new();
    public List<string> Recipients { get; set; } = new();
}
