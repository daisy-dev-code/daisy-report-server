using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DaisyReport.Api.Discovery.Models;

namespace DaisyReport.Api.Discovery.Services;

public interface IServiceProber
{
    Task<DiscoveryResult?> ProbeServiceAsync(string host, int port, int timeoutMs = 5000);
    Task<DiscoveryResult?> ProbeMySqlAsync(string host, int port, string? username = null, string? password = null);
    Task<DiscoveryResult?> ProbeSqlServerAsync(string host, int port, string? username = null, string? password = null);
    Task<DiscoveryResult?> ProbePostgreSqlAsync(string host, int port, string? username = null, string? password = null);
    Task<DiscoveryResult?> ProbeMongoDbAsync(string host, int port);
    Task<DiscoveryResult?> ProbeRedisAsync(string host, int port);
    Task<DiscoveryResult?> ProbeElasticsearchAsync(string host, int port);
    Task<DiscoveryResult?> ProbeHttpAsync(string host, int port);
    Task<List<SqlServerInstance>> QuerySqlBrowserAsync(string host, int timeoutMs = 3000);
}

public class SqlServerInstance
{
    public string ServerName { get; set; } = "";
    public string InstanceName { get; set; } = "";
    public int Port { get; set; }
    public string? Version { get; set; }
    public bool IsClustered { get; set; }
}

public class ServiceProber : IServiceProber
{
    private readonly ILogger<ServiceProber> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // Map well-known ports to their probe methods
    private static readonly Dictionary<int, string> PortServiceMap = new()
    {
        { 3306, "MYSQL" },
        { 5432, "POSTGRESQL" },
        { 1433, "MSSQL" },
        { 1521, "ORACLE" },
        { 27017, "MONGODB" },
        { 6379, "REDIS" },
        { 9200, "ELASTICSEARCH" },
        { 5439, "REDSHIFT" },
        { 8123, "CLICKHOUSE" },
        { 9042, "CASSANDRA" },
        { 7687, "NEO4J" },
        { 8086, "INFLUXDB" },
        { 443, "HTTPS" },
        { 80, "HTTP" }
    };

    public ServiceProber(ILogger<ServiceProber> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DiscoveryResult?> ProbeServiceAsync(string host, int port, int timeoutMs = 5000)
    {
        return port switch
        {
            3306 => await ProbeMySqlAsync(host, port),
            5432 => await ProbePostgreSqlAsync(host, port),
            1433 => await ProbeSqlServerAsync(host, port),
            27017 => await ProbeMongoDbAsync(host, port),
            6379 => await ProbeRedisAsync(host, port),
            9200 => await ProbeElasticsearchAsync(host, port),
            443 => await ProbeHttpAsync(host, port),
            80 => await ProbeHttpAsync(host, port),
            8123 => await ProbeHttpAsync(host, port), // ClickHouse HTTP
            8086 => await ProbeHttpAsync(host, port), // InfluxDB HTTP
            _ => await ProbeGenericTcpAsync(host, port, timeoutMs)
        };
    }

    /// <summary>
    /// MySQL/MariaDB: Read the server greeting packet to extract version info.
    /// The server sends an initial handshake packet immediately after TCP connect.
    /// </summary>
    public async Task<DiscoveryResult?> ProbeMySqlAsync(string host, int port, string? username = null, string? password = null)
    {
        var result = new DiscoveryResult { Host = host, Port = port, ServiceType = "MYSQL" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(5000);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;

            var stream = client.GetStream();
            stream.ReadTimeout = 5000;

            // MySQL server sends greeting packet on connect
            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);

            if (bytesRead > 5)
            {
                // Packet structure: 3 bytes length + 1 byte sequence + payload
                // Payload byte 0 = protocol version, then null-terminated server version string
                var payloadStart = 4;
                var protocolVersion = buffer[payloadStart];

                if (protocolVersion == 10 || protocolVersion == 9) // MySQL protocol v10 or v9
                {
                    // Read null-terminated version string starting at offset 5
                    var versionEnd = Array.IndexOf(buffer, (byte)0, payloadStart + 1);
                    if (versionEnd > payloadStart + 1)
                    {
                        result.ServiceVersion = Encoding.ASCII.GetString(buffer, payloadStart + 1, versionEnd - payloadStart - 1);
                    }

                    result.IsAccessible = true;

                    // Determine if MySQL or MariaDB
                    if (result.ServiceVersion?.Contains("MariaDB", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        result.ServiceType = "MARIADB";
                    }
                }
            }

            // If credentials provided, try to list databases
            if (!string.IsNullOrEmpty(username))
            {
                try
                {
                    var connStr = $"Server={host};Port={port};User={username};Password={password ?? ""};";
                    using var mysqlConn = new MySqlConnector.MySqlConnection(connStr);
                    await mysqlConn.OpenAsync(cts.Token);

                    using var cmd = mysqlConn.CreateCommand();
                    cmd.CommandText = "SHOW DATABASES";
                    using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                    while (await reader.ReadAsync(cts.Token))
                    {
                        result.Databases.Add(reader.GetString(0));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not enumerate MySQL databases on {Host}:{Port}", host, port);
                }
            }

            result.ConnectionString = $"Server={host};Port={port};Database=;User={username ?? "root"};Password={password ?? ""};";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = false;
            result.ErrorMessage = ex.Message;
            _logger.LogDebug(ex, "MySQL probe failed for {Host}:{Port}", host, port);
        }

        result.DiscoveredAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// SQL Server: Send a TDS prelogin packet and parse the response.
    /// </summary>
    public async Task<DiscoveryResult?> ProbeSqlServerAsync(string host, int port, string? username = null, string? password = null)
    {
        var result = new DiscoveryResult { Host = host, Port = port, ServiceType = "MSSQL" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(5000);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;

            var stream = client.GetStream();
            stream.ReadTimeout = 5000;

            // Build TDS prelogin packet
            var preloginPayload = BuildTdsPreloginPacket();
            // TDS header: type=0x12 (prelogin), status=0x01 (EOM), length (2 bytes big-endian), SPID=0, packet=0, window=0
            var tdsPacket = new byte[8 + preloginPayload.Length];
            tdsPacket[0] = 0x12; // Prelogin
            tdsPacket[1] = 0x01; // EOM
            var totalLen = (ushort)(8 + preloginPayload.Length);
            tdsPacket[2] = (byte)(totalLen >> 8);
            tdsPacket[3] = (byte)(totalLen & 0xFF);
            Array.Copy(preloginPayload, 0, tdsPacket, 8, preloginPayload.Length);

            await stream.WriteAsync(tdsPacket, 0, tdsPacket.Length, cts.Token);

            var responseBuffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(responseBuffer, 0, responseBuffer.Length, cts.Token);

            if (bytesRead > 8 && responseBuffer[0] == 0x04) // TDS response
            {
                result.IsAccessible = true;

                // Try to extract version from prelogin response
                // The version option is at offset determined by the option tokens in the payload
                // For simplicity, look for the VERSION option (token 0x00) in the response payload
                var payload = responseBuffer.AsSpan(8, bytesRead - 8);
                var version = TryParseTdsVersion(payload.ToArray());
                if (version != null)
                {
                    result.ServiceVersion = version;
                }
            }

            result.ConnectionString = $"Server={host},{port};Database=;User Id={username ?? "sa"};Password={password ?? ""};TrustServerCertificate=true;";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = false;
            result.ErrorMessage = ex.Message;
            _logger.LogDebug(ex, "SQL Server probe failed for {Host}:{Port}", host, port);
        }

        result.DiscoveredAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// PostgreSQL: Send a startup message and check the response type.
    /// </summary>
    public async Task<DiscoveryResult?> ProbePostgreSqlAsync(string host, int port, string? username = null, string? password = null)
    {
        var result = new DiscoveryResult { Host = host, Port = port, ServiceType = "POSTGRESQL" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(5000);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;

            var stream = client.GetStream();
            stream.ReadTimeout = 5000;

            // Send SSLRequest message first (commonly used to detect PG)
            // SSLRequest: 8 bytes total — 4 bytes length (8) + 4 bytes magic (80877103)
            var sslRequest = new byte[]
            {
                0x00, 0x00, 0x00, 0x08, // length = 8
                0x04, 0xD2, 0x16, 0x2F  // 80877103 = SSLRequest code
            };

            await stream.WriteAsync(sslRequest, 0, sslRequest.Length, cts.Token);

            var response = new byte[1];
            var bytesRead = await stream.ReadAsync(response, 0, 1, cts.Token);

            if (bytesRead == 1 && (response[0] == (byte)'S' || response[0] == (byte)'N'))
            {
                // 'S' = SSL supported, 'N' = SSL not supported — both confirm PostgreSQL
                result.IsAccessible = true;
                result.ServiceVersion = "PostgreSQL (detected via SSLRequest)";
            }

            // If credentials provided, try to connect and list databases
            if (!string.IsNullOrEmpty(username))
            {
                try
                {
                    // We need a fresh connection since the SSLRequest was already sent
                    var connStr = $"Host={host};Port={port};Username={username};Password={password ?? ""};Database=postgres;Timeout=5;";
                    using var pgConn = new Npgsql.NpgsqlConnection(connStr);
                    await pgConn.OpenAsync(cts.Token);

                    result.ServiceVersion = $"PostgreSQL {pgConn.ServerVersion}";

                    using var cmd = pgConn.CreateCommand();
                    cmd.CommandText = "SELECT datname FROM pg_database WHERE datistemplate = false ORDER BY datname";
                    using var reader = await cmd.ExecuteReaderAsync(cts.Token);
                    while (await reader.ReadAsync(cts.Token))
                    {
                        result.Databases.Add(reader.GetString(0));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not enumerate PostgreSQL databases on {Host}:{Port}", host, port);
                }
            }

            result.ConnectionString = $"Host={host};Port={port};Database=;Username={username ?? "postgres"};Password={password ?? ""};";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = false;
            result.ErrorMessage = ex.Message;
            _logger.LogDebug(ex, "PostgreSQL probe failed for {Host}:{Port}", host, port);
        }

        result.DiscoveredAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// MongoDB: Connect and read the initial server response bytes.
    /// </summary>
    public async Task<DiscoveryResult?> ProbeMongoDbAsync(string host, int port)
    {
        var result = new DiscoveryResult { Host = host, Port = port, ServiceType = "MONGODB" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(5000);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;

            var stream = client.GetStream();
            stream.ReadTimeout = 3000;

            // Send an isMaster command using OP_MSG (MongoDB wire protocol)
            // This is a simplified approach — build a minimal OP_MSG
            var isMasterDoc = BuildBsonIsMaster();
            var opMsg = BuildOpMsg(isMasterDoc);

            await stream.WriteAsync(opMsg, 0, opMsg.Length, cts.Token);

            var responseBuffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(responseBuffer, 0, responseBuffer.Length, cts.Token);

            if (bytesRead > 16)
            {
                // If we got a response, it is likely MongoDB
                result.IsAccessible = true;
                result.ServiceVersion = "MongoDB (detected via OP_MSG)";

                // Try to find version string in BSON response
                var responseStr = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);
                var versionIdx = responseStr.IndexOf("version", StringComparison.Ordinal);
                if (versionIdx > 0)
                {
                    // Extract version from the BSON response (rough parse)
                    var vStart = responseStr.IndexOf('\0', versionIdx) + 1;
                    if (vStart > 0 && vStart < bytesRead - 2)
                    {
                        var vEnd = responseStr.IndexOf('\0', vStart);
                        if (vEnd > vStart && vEnd - vStart < 20)
                        {
                            result.ServiceVersion = $"MongoDB {responseStr[vStart..vEnd]}";
                        }
                    }
                }
            }

            result.ConnectionString = $"mongodb://{host}:{port}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;

            // MongoDB might refuse the connection but the port being open is still a signal
            // If we connected but got an error reading, it could still be MongoDB
            result.IsAccessible = false;
            result.ErrorMessage = ex.Message;
            _logger.LogDebug(ex, "MongoDB probe failed for {Host}:{Port}", host, port);
        }

        result.DiscoveredAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Redis: Send PING command and expect +PONG response.
    /// Also send INFO server to get version.
    /// </summary>
    public async Task<DiscoveryResult?> ProbeRedisAsync(string host, int port)
    {
        var result = new DiscoveryResult { Host = host, Port = port, ServiceType = "REDIS" };
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(5000);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;

            var stream = client.GetStream();
            stream.ReadTimeout = 3000;

            // Send PING
            var pingCmd = Encoding.ASCII.GetBytes("PING\r\n");
            await stream.WriteAsync(pingCmd, 0, pingCmd.Length, cts.Token);

            var buffer = new byte[1024];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            var response = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            if (response.Contains("+PONG") || response.Contains("-NOAUTH"))
            {
                result.IsAccessible = true;

                // Try INFO server for version
                if (response.Contains("+PONG"))
                {
                    var infoCmd = Encoding.ASCII.GetBytes("INFO server\r\n");
                    await stream.WriteAsync(infoCmd, 0, infoCmd.Length, cts.Token);

                    var infoBuffer = new byte[8192];
                    bytesRead = await stream.ReadAsync(infoBuffer, 0, infoBuffer.Length, cts.Token);
                    var infoResponse = Encoding.ASCII.GetString(infoBuffer, 0, bytesRead);

                    // Parse redis_version:x.y.z
                    var versionLine = infoResponse.Split('\n')
                        .FirstOrDefault(l => l.StartsWith("redis_version:", StringComparison.OrdinalIgnoreCase));
                    if (versionLine != null)
                    {
                        result.ServiceVersion = $"Redis {versionLine.Split(':')[1].Trim()}";
                    }
                }
                else
                {
                    result.ServiceVersion = "Redis (requires authentication)";
                }
            }

            result.ConnectionString = $"{host}:{port}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = false;
            result.ErrorMessage = ex.Message;
            _logger.LogDebug(ex, "Redis probe failed for {Host}:{Port}", host, port);
        }

        result.DiscoveredAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Elasticsearch: HTTP GET / and parse JSON response for cluster info.
    /// </summary>
    public async Task<DiscoveryResult?> ProbeElasticsearchAsync(string host, int port)
    {
        var result = new DiscoveryResult { Host = host, Port = port, ServiceType = "ELASTICSEARCH" };
        var sw = Stopwatch.StartNew();

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var url = $"http://{host}:{port}/";
            var response = await httpClient.GetAsync(url);
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                result.IsAccessible = true;

                if (doc.RootElement.TryGetProperty("version", out var versionObj) &&
                    versionObj.TryGetProperty("number", out var versionNum))
                {
                    result.ServiceVersion = $"Elasticsearch {versionNum.GetString()}";
                }

                if (doc.RootElement.TryGetProperty("name", out var nameElem))
                {
                    result.ServerName = nameElem.GetString();
                }

                if (doc.RootElement.TryGetProperty("cluster_name", out var clusterElem))
                {
                    result.ServerName = $"{result.ServerName} ({clusterElem.GetString()})";
                }
            }

            result.ConnectionString = url;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = false;
            result.ErrorMessage = ex.Message;
            _logger.LogDebug(ex, "Elasticsearch probe failed for {Host}:{Port}", host, port);
        }

        result.DiscoveredAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// HTTP/HTTPS: GET / and examine response headers to identify known services.
    /// </summary>
    public async Task<DiscoveryResult?> ProbeHttpAsync(string host, int port)
    {
        var result = new DiscoveryResult { Host = host, Port = port, ServiceType = port == 443 ? "HTTPS" : "HTTP" };
        var sw = Stopwatch.StartNew();

        try
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true // accept self-signed for probing
            };
            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var scheme = port == 443 ? "https" : "http";
            var url = $"{scheme}://{host}:{port}/";
            var response = await httpClient.GetAsync(url);
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = true;
            result.ConnectionString = url;

            // Identify service from headers
            var server = response.Headers.Server?.ToString() ?? "";
            var poweredBy = response.Headers.Contains("X-Powered-By")
                ? response.Headers.GetValues("X-Powered-By").FirstOrDefault()
                : null;

            var body = "";
            try { body = await response.Content.ReadAsStringAsync(); } catch { }

            // Identify known services
            if (body.Contains("Grafana", StringComparison.OrdinalIgnoreCase) || server.Contains("Grafana", StringComparison.OrdinalIgnoreCase))
            {
                result.ServiceType = "GRAFANA";
                result.ServiceVersion = "Grafana";
            }
            else if (body.Contains("Kibana", StringComparison.OrdinalIgnoreCase))
            {
                result.ServiceType = "KIBANA";
                result.ServiceVersion = "Kibana";
            }
            else if (body.Contains("phpMyAdmin", StringComparison.OrdinalIgnoreCase))
            {
                result.ServiceType = "PHPMYADMIN";
                result.ServiceVersion = "phpMyAdmin";
            }
            else if (body.Contains("pgAdmin", StringComparison.OrdinalIgnoreCase))
            {
                result.ServiceType = "PGADMIN";
                result.ServiceVersion = "pgAdmin";
            }
            else if (body.Contains("\"tagline\":\"You Know, for Search\"", StringComparison.OrdinalIgnoreCase))
            {
                // Elasticsearch on non-standard port
                result.ServiceType = "ELASTICSEARCH";
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("version", out var v) && v.TryGetProperty("number", out var n))
                        result.ServiceVersion = $"Elasticsearch {n.GetString()}";
                }
                catch { result.ServiceVersion = "Elasticsearch"; }
            }
            else if (body.Contains("ClickHouse", StringComparison.OrdinalIgnoreCase))
            {
                result.ServiceType = "CLICKHOUSE";
                result.ServiceVersion = body.Trim().Length < 60 ? body.Trim() : "ClickHouse";
            }
            else if (body.Contains("InfluxDB", StringComparison.OrdinalIgnoreCase) || response.Headers.Contains("X-Influxdb-Version"))
            {
                result.ServiceType = "INFLUXDB";
                result.ServiceVersion = response.Headers.Contains("X-Influxdb-Version")
                    ? $"InfluxDB {response.Headers.GetValues("X-Influxdb-Version").First()}"
                    : "InfluxDB";
            }
            else
            {
                result.ServiceVersion = string.IsNullOrEmpty(server) ? null : server;
            }

            result.ServerName = server;
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = false;
            result.ErrorMessage = ex.Message;
            _logger.LogDebug(ex, "HTTP probe failed for {Host}:{Port}", host, port);
        }

        result.DiscoveredAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>Generic TCP probe: connect and attempt to read a banner.</summary>
    private async Task<DiscoveryResult?> ProbeGenericTcpAsync(string host, int port, int timeoutMs)
    {
        var serviceType = PortServiceMap.TryGetValue(port, out var svc) ? svc : "UNKNOWN";
        var result = new DiscoveryResult { Host = host, Port = port, ServiceType = serviceType };
        var sw = Stopwatch.StartNew();

        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = true;

            // Try to read a banner
            var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            var buffer = new byte[512];
            try
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (bytesRead > 0)
                {
                    var banner = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    if (banner.Length > 0 && banner.Length < 200)
                    {
                        result.ServiceVersion = banner;
                    }
                }
            }
            catch { /* many services don't send banners */ }
        }
        catch (Exception ex)
        {
            sw.Stop();
            result.LatencyMs = (int)sw.ElapsedMilliseconds;
            result.IsAccessible = false;
            result.ErrorMessage = ex.Message;
        }

        result.DiscoveredAt = DateTime.UtcNow;
        return result;
    }

    #region TDS Protocol Helpers

    /// <summary>Build a minimal TDS prelogin packet payload.</summary>
    private static byte[] BuildTdsPreloginPacket()
    {
        // Prelogin options: VERSION, ENCRYPTION, INSTOPT, THREADID, MARS, TERMINATOR
        // Simplified: just VERSION + TERMINATOR
        var options = new MemoryStream();
        var data = new MemoryStream();

        // VERSION option: token=0x00, offset=5 (after option headers), length=6
        // Each option = 1 (token) + 2 (offset) + 2 (length) = 5 bytes, then 0xFF terminator
        // So offset for VERSION data = 5 + 1 = 6
        options.WriteByte(0x00); // VERSION token
        options.WriteByte(0x00); options.WriteByte(0x06); // offset = 6
        options.WriteByte(0x00); options.WriteByte(0x06); // length = 6
        options.WriteByte(0xFF); // TERMINATOR

        // VERSION data: UL_VERSION (4 bytes) + US_SUBBUILD (2 bytes)
        // Send a fake client version
        data.WriteByte(0x00); data.WriteByte(0x00); data.WriteByte(0x00); data.WriteByte(0x00);
        data.WriteByte(0x00); data.WriteByte(0x00);

        var result = new byte[options.Length + data.Length];
        Array.Copy(options.ToArray(), 0, result, 0, (int)options.Length);
        Array.Copy(data.ToArray(), 0, result, (int)options.Length, (int)data.Length);
        return result;
    }

    /// <summary>Try to parse SQL Server version from TDS prelogin response.</summary>
    private static string? TryParseTdsVersion(byte[] payload)
    {
        try
        {
            // Parse option tokens to find VERSION (token 0x00)
            var idx = 0;
            while (idx < payload.Length && payload[idx] != 0xFF)
            {
                var token = payload[idx];
                if (idx + 4 >= payload.Length) break;
                var offset = (payload[idx + 1] << 8) | payload[idx + 2];
                var length = (payload[idx + 3] << 8) | payload[idx + 4];

                if (token == 0x00 && length >= 6 && offset + 5 < payload.Length)
                {
                    var major = payload[offset];
                    var minor = payload[offset + 1];
                    var build = (payload[offset + 2] << 8) | payload[offset + 3];
                    return $"SQL Server {major}.{minor}.{build}";
                }

                idx += 5;
            }
        }
        catch { }
        return null;
    }

    #endregion

    #region MongoDB Wire Protocol Helpers

    /// <summary>Build a BSON document for { isMaster: 1, $db: "admin" }.</summary>
    private static byte[] BuildBsonIsMaster()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // We'll write the BSON doc and then go back to fix the length
        var startPos = ms.Position;
        writer.Write(0); // placeholder for document length

        // isMaster: 1 (int32, type 0x10)
        writer.Write((byte)0x10); // int32 type
        writer.Write(Encoding.UTF8.GetBytes("isMaster"));
        writer.Write((byte)0x00); // null terminator
        writer.Write(1); // value = 1

        // $db: "admin" (string, type 0x02)
        writer.Write((byte)0x02); // string type
        writer.Write(Encoding.UTF8.GetBytes("$db"));
        writer.Write((byte)0x00);
        var dbStr = Encoding.UTF8.GetBytes("admin");
        writer.Write(dbStr.Length + 1); // string length including null
        writer.Write(dbStr);
        writer.Write((byte)0x00);

        writer.Write((byte)0x00); // document terminator

        var docLength = (int)(ms.Position - startPos);
        ms.Position = startPos;
        writer.Write(docLength);

        return ms.ToArray();
    }

    /// <summary>Build a MongoDB OP_MSG packet wrapping a BSON document.</summary>
    private static byte[] BuildOpMsg(byte[] bsonDoc)
    {
        // OP_MSG: MsgHeader (16 bytes) + flagBits (4 bytes) + section kind=0 (1 byte) + BSON doc
        var messageLength = 16 + 4 + 1 + bsonDoc.Length;
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // MsgHeader
        writer.Write(messageLength); // messageLength
        writer.Write(1);             // requestID
        writer.Write(0);             // responseTo
        writer.Write(2013);          // opCode = OP_MSG (2013)

        // flagBits
        writer.Write(0);

        // Section: Kind 0 (body)
        writer.Write((byte)0);
        writer.Write(bsonDoc);

        return ms.ToArray();
    }

    #endregion

    #region SQL Server Browser (UDP 1434)

    /// <summary>
    /// Query SQL Server Browser Service on UDP 1434 to discover all instances and their ports.
    /// This is how SSMS discovers named instances — sends 0x02 byte, gets back a semicolon-delimited response.
    /// </summary>
    public async Task<List<SqlServerInstance>> QuerySqlBrowserAsync(string host, int timeoutMs = 3000)
    {
        var instances = new List<SqlServerInstance>();
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = timeoutMs;

            var resolvedAddresses = await System.Net.Dns.GetHostAddressesAsync(host);
            var targetAddress = resolvedAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? resolvedAddresses.FirstOrDefault();
            if (targetAddress == null) return instances;

            var endpoint = new System.Net.IPEndPoint(targetAddress, 1434);

            // Send 0x02 = request all instances, 0x03 = request specific instance
            await udp.SendAsync(new byte[] { 0x02 }, 1, endpoint);

            // Wait for response with timeout
            var receiveTask = udp.ReceiveAsync();
            if (await Task.WhenAny(receiveTask, Task.Delay(timeoutMs)) != receiveTask)
            {
                _logger.LogDebug("SQL Browser on {Host} did not respond within {Timeout}ms", host, timeoutMs);
                return instances;
            }

            var response = receiveTask.Result;
            var responseText = Encoding.UTF8.GetString(response.Buffer, 3, response.Buffer.Length - 3);

            _logger.LogInformation("SQL Browser response from {Host}: {Response}", host, responseText);

            // Parse response: instances are separated by ";;", fields by ";"
            // Format: ServerName;SERVERNAME;InstanceName;INSTANCE;IsClustered;No;Version;16.0.1135.2;tcp;1433;;
            var instanceBlocks = responseText.Split(";;", StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in instanceBlocks)
            {
                var fields = block.Split(';');
                var instance = new SqlServerInstance();

                for (int i = 0; i < fields.Length - 1; i += 2)
                {
                    var key = fields[i].Trim();
                    var value = fields[i + 1].Trim();
                    switch (key.ToLowerInvariant())
                    {
                        case "servername": instance.ServerName = value; break;
                        case "instancename": instance.InstanceName = value; break;
                        case "isclustered": instance.IsClustered = value.Equals("Yes", StringComparison.OrdinalIgnoreCase); break;
                        case "version": instance.Version = value; break;
                        case "tcp": if (int.TryParse(value, out var port)) instance.Port = port; break;
                    }
                }

                if (!string.IsNullOrEmpty(instance.InstanceName))
                {
                    instances.Add(instance);
                    _logger.LogInformation("Found SQL Server instance: {Server}\\{Instance} on port {Port} (v{Version})",
                        instance.ServerName, instance.InstanceName, instance.Port, instance.Version);
                }
            }
        }
        catch (SocketException ex)
        {
            _logger.LogDebug("SQL Browser query failed for {Host}: {Error}", host, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("SQL Browser query error for {Host}: {Error}", host, ex.Message);
        }

        return instances;
    }

    #endregion
}
