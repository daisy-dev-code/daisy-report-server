using DaisyReport.Api.Discovery.Models;
using DaisyReport.Api.Discovery.Services;

namespace DaisyReport.Api.Discovery.Endpoints;

public static class DiscoveryEndpoints
{
    public static void MapDiscoveryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/discovery").RequireAuthorization();

        group.MapPost("/probe", ProbeHost);
        group.MapPost("/scan", ScanNetwork);
        group.MapPost("/check", CheckService);
        group.MapPost("/dns", DiscoverByDns);
        group.MapPost("/create-datasource", CreateFromDiscovery);
        group.MapGet("/ports", GetWellKnownPorts);
    }

    /// <summary>
    /// Probe a single host for all known database services.
    /// Body: { "host": "192.168.1.1", "username": "sa", "password": "..." }
    /// </summary>
    private static async Task<IResult> ProbeHost(ProbeHostRequest request, IDiscoveryService discovery)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return Results.BadRequest(new { error = "Host is required." });

        var result = await discovery.ProbeHostAsync(request.Host, request.Username, request.Password);
        return Results.Ok(result);
    }

    /// <summary>
    /// Scan a network range for database services.
    /// Body: { "range": "192.168.1.0/24", "maxParallel": 50 }
    /// </summary>
    private static async Task<IResult> ScanNetwork(ScanNetworkRequest request, IDiscoveryService discovery)
    {
        if (string.IsNullOrWhiteSpace(request.Range))
            return Results.BadRequest(new { error = "Network range (CIDR) is required." });

        // Validate CIDR format
        try
        {
            PortScanner.ParseCidr(request.Range);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var maxParallel = request.MaxParallel is > 0 and <= 200 ? request.MaxParallel.Value : 50;
        var result = await discovery.ScanNetworkAsync(request.Range, maxParallel);
        return Results.Ok(result);
    }

    /// <summary>
    /// Quick check a specific service on a specific host:port.
    /// Body: { "host": "192.168.1.1", "port": 3306, "username": "root", "password": "..." }
    /// </summary>
    private static async Task<IResult> CheckService(CheckServiceRequest request, IDiscoveryService discovery)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return Results.BadRequest(new { error = "Host is required." });
        if (request.Port is < 1 or > 65535)
            return Results.BadRequest(new { error = "Port must be between 1 and 65535." });

        var result = await discovery.CheckServiceAsync(request.Host, request.Port, request.Username, request.Password);
        return Results.Ok(result);
    }

    /// <summary>
    /// DNS-based discovery for a domain.
    /// Body: { "domain": "company.local" }
    /// </summary>
    private static async Task<IResult> DiscoverByDns(DnsDiscoveryRequest request, IDiscoveryService discovery)
    {
        if (string.IsNullOrWhiteSpace(request.Domain))
            return Results.BadRequest(new { error = "Domain is required." });

        var results = await discovery.DiscoverByDnsAsync(request.Domain);
        return Results.Ok(new { data = results });
    }

    /// <summary>
    /// Auto-create a datasource from a discovery result.
    /// Body: { "host": "192.168.1.1", "port": 3306, "serviceType": "MYSQL", "name": "Production DB", "username": "root", "password": "..." }
    /// </summary>
    private static async Task<IResult> CreateFromDiscovery(CreateFromDiscoveryRequest request, IDiscoveryService discovery)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return Results.BadRequest(new { error = "Host is required." });
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Name is required." });
        if (request.Port is < 1 or > 65535)
            return Results.BadRequest(new { error = "Port must be between 1 and 65535." });

        var discoveryResult = new DiscoveryResult
        {
            Host = request.Host,
            Port = request.Port,
            ServiceType = request.ServiceType ?? "UNKNOWN",
            IsAccessible = true
        };

        var id = await discovery.CreateDatasourceFromDiscoveryAsync(
            discoveryResult, request.Name, request.Username, request.Password);

        return Results.Created($"/api/datasources/{id}", new
        {
            id,
            name = request.Name,
            connectionString = discoveryResult.ConnectionString
        });
    }

    /// <summary>
    /// Get well-known database and service ports reference.
    /// </summary>
    private static IResult GetWellKnownPorts()
    {
        var ports = new[]
        {
            new { port = 3306, service = "MYSQL", description = "MySQL / MariaDB" },
            new { port = 3307, service = "MYSQL", description = "MySQL (Docker alternate)" },
            new { port = 5432, service = "POSTGRESQL", description = "PostgreSQL" },
            new { port = 5433, service = "POSTGRESQL", description = "PostgreSQL (alternate)" },
            new { port = 1433, service = "MSSQL", description = "Microsoft SQL Server" },
            new { port = 1434, service = "MSSQL", description = "SQL Server Browser" },
            new { port = 1521, service = "ORACLE", description = "Oracle Database" },
            new { port = 27017, service = "MONGODB", description = "MongoDB" },
            new { port = 27018, service = "MONGODB", description = "MongoDB (shard)" },
            new { port = 6379, service = "REDIS", description = "Redis" },
            new { port = 6380, service = "REDIS", description = "Redis (alternate)" },
            new { port = 9200, service = "ELASTICSEARCH", description = "Elasticsearch HTTP" },
            new { port = 9300, service = "ELASTICSEARCH", description = "Elasticsearch transport" },
            new { port = 5439, service = "REDSHIFT", description = "Amazon Redshift" },
            new { port = 8123, service = "CLICKHOUSE", description = "ClickHouse HTTP" },
            new { port = 9042, service = "CASSANDRA", description = "Apache Cassandra" },
            new { port = 7687, service = "NEO4J", description = "Neo4j Bolt" },
            new { port = 8086, service = "INFLUXDB", description = "InfluxDB" },
        };

        return Results.Ok(new { data = ports });
    }
}

// Request DTOs
public record ProbeHostRequest(string Host, string? Username, string? Password);
public record ScanNetworkRequest(string Range, int? MaxParallel);
public record CheckServiceRequest(string Host, int Port, string? Username, string? Password);
public record DnsDiscoveryRequest(string Domain);
public record CreateFromDiscoveryRequest(string Host, int Port, string? ServiceType, string Name, string? Username, string? Password);
