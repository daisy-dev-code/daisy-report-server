using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DaisyReport.Api.Discovery.Services;

public interface IPortScanner
{
    Task<bool> IsPortOpenAsync(string host, int port, int timeoutMs = 2000);
    Task<List<(int Port, bool Open)>> ScanPortsAsync(string host, int[] ports, int timeoutMs = 2000);
    Task<List<string>> ScanNetworkAsync(string cidr, int[] ports, int timeoutMs = 1000, int maxParallel = 50);
}

public class PortScanner : IPortScanner
{
    private readonly ILogger<PortScanner> _logger;

    /// <summary>Well-known database and service ports.</summary>
    public static readonly int[] DefaultPorts =
    {
        3306,  // MySQL / MariaDB
        5432,  // PostgreSQL
        1433,  // SQL Server
        1521,  // Oracle
        27017, // MongoDB
        6379,  // Redis
        9200,  // Elasticsearch
        5439,  // Redshift
        8123,  // ClickHouse
        9042,  // Cassandra
        7687,  // Neo4j
        8086,  // InfluxDB
        443,   // HTTPS / REST APIs
        80     // HTTP
    };

    public PortScanner(ILogger<PortScanner> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsPortOpenAsync(string host, int port, int timeoutMs = 2000)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<(int Port, bool Open)>> ScanPortsAsync(string host, int[] ports, int timeoutMs = 2000)
    {
        var results = new List<(int Port, bool Open)>();
        var semaphore = new SemaphoreSlim(20); // max 20 concurrent port checks per host
        var tasks = ports.Select(async port =>
        {
            await semaphore.WaitAsync();
            try
            {
                var open = await IsPortOpenAsync(host, port, timeoutMs);
                lock (results)
                {
                    results.Add((port, open));
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.OrderBy(r => r.Port).ToList();
    }

    public async Task<List<string>> ScanNetworkAsync(string cidr, int[] ports, int timeoutMs = 1000, int maxParallel = 50)
    {
        var ips = ParseCidr(cidr);
        _logger.LogInformation("Network scan: {Count} IPs from {Cidr}", ips.Count, cidr);

        // Phase 1: Ping sweep to find alive hosts
        var aliveHosts = new List<string>();
        var pingSemaphore = new SemaphoreSlim(maxParallel);
        var pingTasks = ips.Select(async ip =>
        {
            await pingSemaphore.WaitAsync();
            try
            {
                if (await IsHostAliveAsync(ip, timeoutMs))
                {
                    lock (aliveHosts)
                    {
                        aliveHosts.Add(ip);
                    }
                }
            }
            finally
            {
                pingSemaphore.Release();
            }
        });

        await Task.WhenAll(pingTasks);
        _logger.LogInformation("Ping sweep complete: {Alive}/{Total} hosts alive", aliveHosts.Count, ips.Count);

        // Phase 2: Port scan alive hosts — return hosts that have at least one open port
        var hostsWithOpenPorts = new List<string>();
        var portSemaphore = new SemaphoreSlim(maxParallel);
        var portTasks = aliveHosts.Select(async host =>
        {
            await portSemaphore.WaitAsync();
            try
            {
                var scanResults = await ScanPortsAsync(host, ports, timeoutMs);
                if (scanResults.Any(r => r.Open))
                {
                    lock (hostsWithOpenPorts)
                    {
                        hostsWithOpenPorts.Add(host);
                    }
                }
            }
            finally
            {
                portSemaphore.Release();
            }
        });

        await Task.WhenAll(portTasks);
        return hostsWithOpenPorts;
    }

    /// <summary>Ping a host to check if it is alive.</summary>
    public static async Task<bool> IsHostAliveAsync(string host, int timeoutMs = 1000)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Measure ping latency. Returns -1 if unreachable.</summary>
    public static async Task<int> PingLatencyAsync(string host, int timeoutMs = 2000)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Parse a CIDR notation (e.g. 192.168.1.0/24) into a list of IP addresses.</summary>
    public static List<string> ParseCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var networkAddress) || !int.TryParse(parts[1], out var prefixLength))
        {
            throw new ArgumentException($"Invalid CIDR notation: {cidr}");
        }

        if (prefixLength < 16 || prefixLength > 32)
        {
            throw new ArgumentException($"Prefix length must be between 16 and 32. Got: {prefixLength}");
        }

        var networkBytes = networkAddress.GetAddressBytes();
        if (networkBytes.Length != 4)
        {
            throw new ArgumentException("Only IPv4 CIDR is supported.");
        }

        var networkUint = (uint)(networkBytes[0] << 24 | networkBytes[1] << 16 | networkBytes[2] << 8 | networkBytes[3]);
        var hostBits = 32 - prefixLength;
        var hostCount = (1u << hostBits) - 2; // exclude network and broadcast
        var firstHost = (networkUint & (0xFFFFFFFFu << hostBits)) + 1;

        var ips = new List<string>();
        for (uint i = 0; i < hostCount; i++)
        {
            var ip = firstHost + i;
            ips.Add($"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}");
        }

        return ips;
    }
}
