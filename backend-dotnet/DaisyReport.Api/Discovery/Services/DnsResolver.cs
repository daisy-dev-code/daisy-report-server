using System.Net;

namespace DaisyReport.Api.Discovery.Services;

public interface IDnsResolver
{
    Task<string?> ResolveHostnameAsync(string ip);
    Task<List<string>> ResolveIpAddressesAsync(string hostname);
    Task<List<string>> DiscoverDnsNamesAsync(string domain);
}

public class DnsResolver : IDnsResolver
{
    private readonly ILogger<DnsResolver> _logger;

    /// <summary>Common DNS prefixes for database and reporting servers.</summary>
    private static readonly string[] CommonPrefixes =
    {
        "db", "database", "sql", "mysql", "mariadb",
        "postgres", "pg", "postgresql",
        "mssql", "sqlserver", "mssqlserver",
        "mongo", "mongodb",
        "redis", "cache",
        "elastic", "elasticsearch", "es",
        "reports", "reporting", "reportserver",
        "powerbi", "grafana", "kibana",
        "influx", "influxdb",
        "clickhouse", "ch",
        "neo4j", "graph",
        "cassandra",
        "data", "datawarehouse", "dw",
        "analytics", "bi",
        "api", "gateway"
    };

    public DnsResolver(ILogger<DnsResolver> logger)
    {
        _logger = logger;
    }

    /// <summary>Reverse DNS lookup: IP address to hostname.</summary>
    public async Task<string?> ResolveHostnameAsync(string ip)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip);
            return entry.HostName;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Reverse DNS lookup failed for {Ip}", ip);
            return null;
        }
    }

    /// <summary>Forward DNS lookup: hostname to IP addresses.</summary>
    public async Task<List<string>> ResolveIpAddressesAsync(string hostname)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(hostname);
            return entry.AddressList
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Forward DNS lookup failed for {Hostname}", hostname);
            return new List<string>();
        }
    }

    /// <summary>
    /// Try common DNS name patterns for database servers within a domain.
    /// Returns hostnames that successfully resolve.
    /// </summary>
    public async Task<List<string>> DiscoverDnsNamesAsync(string domain)
    {
        var resolvedNames = new List<string>();
        var tasks = CommonPrefixes.Select(async prefix =>
        {
            var fqdn = $"{prefix}.{domain}";
            try
            {
                var entry = await Dns.GetHostEntryAsync(fqdn);
                if (entry.AddressList.Length > 0)
                {
                    lock (resolvedNames)
                    {
                        resolvedNames.Add(fqdn);
                    }
                    _logger.LogInformation("DNS discovery found: {Fqdn} -> {Ip}", fqdn, entry.AddressList[0]);
                }
            }
            catch
            {
                // Name does not resolve, skip
            }
        });

        await Task.WhenAll(tasks);
        return resolvedNames.OrderBy(n => n).ToList();
    }
}
