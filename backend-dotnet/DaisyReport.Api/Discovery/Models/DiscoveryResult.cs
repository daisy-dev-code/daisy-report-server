namespace DaisyReport.Api.Discovery.Models;

public class DiscoveryResult
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string ServiceType { get; set; } = ""; // MYSQL, MSSQL, POSTGRESQL, ORACLE, REDIS, MONGODB, ELASTICSEARCH, etc.
    public string? ServiceVersion { get; set; }
    public string? ServerName { get; set; }
    public string? InstanceName { get; set; }
    public bool IsAccessible { get; set; }
    public int LatencyMs { get; set; }
    public List<string> Databases { get; set; } = new();
    public string? ConnectionString { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}

public class NetworkScanResult
{
    public string ScanRange { get; set; } = "";
    public int HostsScanned { get; set; }
    public int HostsAlive { get; set; }
    public int ServicesFound { get; set; }
    public List<DiscoveryResult> Discoveries { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public long DurationMs { get; set; }
}

public class HostProbeResult
{
    public string Host { get; set; } = "";
    public bool IsAlive { get; set; }
    public int PingMs { get; set; }
    public string? Hostname { get; set; }
    public List<DiscoveryResult> Services { get; set; } = new();
}
