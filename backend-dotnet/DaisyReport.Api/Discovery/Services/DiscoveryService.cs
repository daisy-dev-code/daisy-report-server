using System.Diagnostics;
using Dapper;
using DaisyReport.Api.Discovery.Models;
using DaisyReport.Api.Infrastructure;

namespace DaisyReport.Api.Discovery.Services;

public interface IDiscoveryService
{
    /// <summary>Probe a single host for all known database services.</summary>
    Task<HostProbeResult> ProbeHostAsync(string host, string? username = null, string? password = null);

    /// <summary>Scan a network range (CIDR) for database services.</summary>
    Task<NetworkScanResult> ScanNetworkAsync(string cidrOrRange, int maxParallel = 50);

    /// <summary>Quick check a specific service on a specific host:port.</summary>
    Task<DiscoveryResult> CheckServiceAsync(string host, int port, string? username = null, string? password = null);

    /// <summary>DNS-based discovery: try common database server names for a domain.</summary>
    Task<List<HostProbeResult>> DiscoverByDnsAsync(string domain);

    /// <summary>Auto-create a datasource from a discovery result.</summary>
    Task<long> CreateDatasourceFromDiscoveryAsync(DiscoveryResult discovery, string name, string? username = null, string? password = null);
}

public class DiscoveryService : IDiscoveryService
{
    private readonly IPortScanner _portScanner;
    private readonly IServiceProber _serviceProber;
    private readonly IDnsResolver _dnsResolver;
    private readonly IDatabase _database;
    private readonly ILogger<DiscoveryService> _logger;

    public DiscoveryService(
        IPortScanner portScanner,
        IServiceProber serviceProber,
        IDnsResolver dnsResolver,
        IDatabase database,
        ILogger<DiscoveryService> logger)
    {
        _portScanner = portScanner;
        _serviceProber = serviceProber;
        _dnsResolver = dnsResolver;
        _database = database;
        _logger = logger;
    }

    public async Task<HostProbeResult> ProbeHostAsync(string host, string? username = null, string? password = null)
    {
        var result = new HostProbeResult { Host = host };

        // Step 1: Check if host is alive
        var pingMs = await PortScanner.PingLatencyAsync(host);
        result.IsAlive = pingMs >= 0;
        result.PingMs = Math.Max(0, pingMs);

        // Step 2: Resolve hostname
        result.Hostname = await _dnsResolver.ResolveHostnameAsync(host);

        if (!result.IsAlive)
        {
            // Host might still have services even if ICMP is blocked — try port scan anyway
            _logger.LogInformation("Host {Host} did not respond to ping, scanning ports anyway", host);
        }

        // Step 3: Scan all known ports
        var portResults = await _portScanner.ScanPortsAsync(host, PortScanner.DefaultPorts);
        var openPorts = portResults.Where(p => p.Open).Select(p => p.Port).ToArray();

        _logger.LogInformation("Host {Host}: {OpenCount}/{TotalCount} ports open ({Ports})",
            host, openPorts.Length, PortScanner.DefaultPorts.Length,
            string.Join(", ", openPorts));

        // Step 4: Probe each open port for service identification
        var probeTasks = openPorts.Select(async port =>
        {
            var discovery = port switch
            {
                3306 => await _serviceProber.ProbeMySqlAsync(host, port, username, password),
                5432 => await _serviceProber.ProbePostgreSqlAsync(host, port, username, password),
                1433 => await _serviceProber.ProbeSqlServerAsync(host, port, username, password),
                27017 => await _serviceProber.ProbeMongoDbAsync(host, port),
                6379 => await _serviceProber.ProbeRedisAsync(host, port),
                9200 => await _serviceProber.ProbeElasticsearchAsync(host, port),
                _ => await _serviceProber.ProbeServiceAsync(host, port)
            };
            return discovery;
        });

        var probeResults = await Task.WhenAll(probeTasks);
        result.Services = probeResults.Where(r => r != null).Select(r => r!).ToList();

        // Mark host as alive if any service responded, even if ping failed
        if (result.Services.Any(s => s.IsAccessible))
        {
            result.IsAlive = true;
        }

        return result;
    }

    public async Task<NetworkScanResult> ScanNetworkAsync(string cidrOrRange, int maxParallel = 50)
    {
        var scanResult = new NetworkScanResult
        {
            ScanRange = cidrOrRange,
            StartedAt = DateTime.UtcNow
        };

        var sw = Stopwatch.StartNew();

        try
        {
            var ips = PortScanner.ParseCidr(cidrOrRange);
            scanResult.HostsScanned = ips.Count;

            _logger.LogInformation("Starting network scan of {Range} ({Count} hosts, max parallel {Max})",
                cidrOrRange, ips.Count, maxParallel);

            // Phase 1: Ping sweep
            var aliveHosts = new List<string>();
            var pingSemaphore = new SemaphoreSlim(maxParallel);
            var pingTasks = ips.Select(async ip =>
            {
                await pingSemaphore.WaitAsync();
                try
                {
                    if (await PortScanner.IsHostAliveAsync(ip, 1000))
                    {
                        lock (aliveHosts) { aliveHosts.Add(ip); }
                    }
                }
                finally { pingSemaphore.Release(); }
            });
            await Task.WhenAll(pingTasks);
            scanResult.HostsAlive = aliveHosts.Count;

            _logger.LogInformation("Ping sweep complete: {Alive}/{Total} hosts alive", aliveHosts.Count, ips.Count);

            // Phase 2: Probe each alive host (with limited parallelism)
            var probeSemaphore = new SemaphoreSlim(Math.Min(maxParallel, 10)); // limit probing concurrency
            var discoveries = new List<DiscoveryResult>();
            var probeTasks = aliveHosts.Select(async ip =>
            {
                await probeSemaphore.WaitAsync();
                try
                {
                    var portResults = await _portScanner.ScanPortsAsync(ip, PortScanner.DefaultPorts, 1500);
                    var openPorts = portResults.Where(p => p.Open).Select(p => p.Port).ToArray();

                    foreach (var port in openPorts)
                    {
                        var result = await _serviceProber.ProbeServiceAsync(ip, port);
                        if (result != null)
                        {
                            lock (discoveries) { discoveries.Add(result); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error probing host {Ip}", ip);
                }
                finally { probeSemaphore.Release(); }
            });
            await Task.WhenAll(probeTasks);

            scanResult.Discoveries = discoveries.OrderBy(d => d.Host).ThenBy(d => d.Port).ToList();
            scanResult.ServicesFound = discoveries.Count(d => d.IsAccessible);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network scan failed for {Range}", cidrOrRange);
        }

        sw.Stop();
        scanResult.CompletedAt = DateTime.UtcNow;
        scanResult.DurationMs = sw.ElapsedMilliseconds;
        return scanResult;
    }

    public async Task<DiscoveryResult> CheckServiceAsync(string host, int port, string? username = null, string? password = null)
    {
        var result = port switch
        {
            3306 => await _serviceProber.ProbeMySqlAsync(host, port, username, password),
            5432 => await _serviceProber.ProbePostgreSqlAsync(host, port, username, password),
            1433 => await _serviceProber.ProbeSqlServerAsync(host, port, username, password),
            27017 => await _serviceProber.ProbeMongoDbAsync(host, port),
            6379 => await _serviceProber.ProbeRedisAsync(host, port),
            9200 => await _serviceProber.ProbeElasticsearchAsync(host, port),
            _ => await _serviceProber.ProbeServiceAsync(host, port)
        };

        return result ?? new DiscoveryResult
        {
            Host = host,
            Port = port,
            IsAccessible = false,
            ErrorMessage = "Probe returned no result"
        };
    }

    public async Task<List<HostProbeResult>> DiscoverByDnsAsync(string domain)
    {
        _logger.LogInformation("Starting DNS-based discovery for domain {Domain}", domain);

        var resolvedNames = await _dnsResolver.DiscoverDnsNamesAsync(domain);
        _logger.LogInformation("DNS discovery found {Count} resolvable names for {Domain}", resolvedNames.Count, domain);

        var results = new List<HostProbeResult>();
        var semaphore = new SemaphoreSlim(5); // limit concurrent probes

        var tasks = resolvedNames.Select(async hostname =>
        {
            await semaphore.WaitAsync();
            try
            {
                var probeResult = await ProbeHostAsync(hostname);
                lock (results) { results.Add(probeResult); }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to probe DNS-discovered host {Host}", hostname);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
        return results.OrderBy(r => r.Host).ToList();
    }

    public async Task<long> CreateDatasourceFromDiscoveryAsync(DiscoveryResult discovery, string name, string? username = null, string? password = null)
    {
        // Map service type to driver class and JDBC URL format
        var (driverClass, jdbcUrl) = discovery.ServiceType.ToUpperInvariant() switch
        {
            "MYSQL" or "MARIADB" => (
                "com.mysql.cj.jdbc.Driver",
                $"jdbc:mysql://{discovery.Host}:{discovery.Port}/"),
            "MSSQL" => (
                "com.microsoft.sqlserver.jdbc.SQLServerDriver",
                $"jdbc:sqlserver://{discovery.Host}:{discovery.Port}"),
            "POSTGRESQL" => (
                "org.postgresql.Driver",
                $"jdbc:postgresql://{discovery.Host}:{discovery.Port}/"),
            "MONGODB" => (
                "mongodb.jdbc.MongoDriver",
                $"mongodb://{discovery.Host}:{discovery.Port}"),
            "ORACLE" => (
                "oracle.jdbc.driver.OracleDriver",
                $"jdbc:oracle:thin:@{discovery.Host}:{discovery.Port}:ORCL"),
            _ => ("generic", discovery.ConnectionString ?? $"{discovery.Host}:{discovery.Port}")
        };

        using var conn = await _database.GetConnectionAsync();
        using var tx = conn.BeginTransaction();

        var datasourceId = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_DATASOURCE (name, description, dtype, folder_id, created_at, updated_at)
              VALUES (@Name, @Description, 'database', NULL, @Now, @Now);
              SELECT LAST_INSERT_ID();",
            new
            {
                Name = name,
                Description = $"Auto-discovered {discovery.ServiceType} on {discovery.Host}:{discovery.Port}",
                Now = DateTime.UtcNow
            },
            tx);

        await conn.ExecuteAsync(
            @"INSERT INTO RS_DATABASE_DATASOURCE (datasource_id, driver_class, jdbc_url, username, password_encrypted, min_pool, max_pool, query_timeout)
              VALUES (@DatasourceId, @DriverClass, @JdbcUrl, @Username, @Password, 2, 10, 30)",
            new
            {
                DatasourceId = datasourceId,
                DriverClass = driverClass,
                JdbcUrl = jdbcUrl,
                Username = username ?? "",
                Password = password ?? ""
            },
            tx);

        tx.Commit();

        _logger.LogInformation("Created datasource #{Id} ({Name}) from discovery: {Type} on {Host}:{Port}",
            datasourceId, name, discovery.ServiceType, discovery.Host, discovery.Port);

        return datasourceId;
    }
}
