using DaisyReport.Api.DataProviders.Abstractions;
using DaisyReport.Api.DataProviders.Providers;

namespace DaisyReport.Api.DataProviders;

public interface IDataProviderRegistry
{
    IDataProvider GetProvider(DataProviderType type);
    IDataProvider GetProvider(string typeName);
    IReadOnlyList<DataProviderType> SupportedTypes { get; }
}

public sealed class DataProviderRegistry : IDataProviderRegistry
{
    private readonly Dictionary<DataProviderType, IDataProvider> _providers;
    private readonly Dictionary<string, DataProviderType> _nameMap;

    public DataProviderRegistry()
    {
        _providers = new Dictionary<DataProviderType, IDataProvider>
        {
            [DataProviderType.SqlServer] = new SqlServerProvider(),
            [DataProviderType.PostgreSql] = new PostgreSqlProvider(),
            [DataProviderType.MySql] = new MySqlProvider(),
            [DataProviderType.Odbc] = new OdbcProvider(),
            [DataProviderType.Sqlite] = new SqliteProvider(),
        };

        _nameMap = new Dictionary<string, DataProviderType>(StringComparer.OrdinalIgnoreCase)
        {
            ["sqlserver"] = DataProviderType.SqlServer,
            ["mssql"] = DataProviderType.SqlServer,
            ["microsoft.data.sqlclient"] = DataProviderType.SqlServer,
            ["system.data.sqlclient"] = DataProviderType.SqlServer,
            ["com.microsoft.sqlserver.jdbc.sqlserverdriver"] = DataProviderType.SqlServer,
            ["net.sourceforge.jtds.jdbc.driver"] = DataProviderType.SqlServer,

            ["postgresql"] = DataProviderType.PostgreSql,
            ["postgres"] = DataProviderType.PostgreSql,
            ["npgsql"] = DataProviderType.PostgreSql,
            ["org.postgresql.driver"] = DataProviderType.PostgreSql,

            ["mysql"] = DataProviderType.MySql,
            ["mariadb"] = DataProviderType.MySql,
            ["mysqlconnector"] = DataProviderType.MySql,
            ["com.mysql.jdbc.driver"] = DataProviderType.MySql,
            ["com.mysql.cj.jdbc.driver"] = DataProviderType.MySql,
            ["org.mariadb.jdbc.driver"] = DataProviderType.MySql,

            ["odbc"] = DataProviderType.Odbc,
            ["system.data.odbc"] = DataProviderType.Odbc,

            ["oledb"] = DataProviderType.OleDb,
            ["system.data.oledb"] = DataProviderType.OleDb,

            ["sqlite"] = DataProviderType.Sqlite,
            ["microsoft.data.sqlite"] = DataProviderType.Sqlite,
            ["org.sqlite.jdbc"] = DataProviderType.Sqlite,

            ["oracle"] = DataProviderType.Oracle,
            ["oracle.jdbc.oracledriver"] = DataProviderType.Oracle,
            ["oracle.jdbc.driver.oracledriver"] = DataProviderType.Oracle,
        };
    }

    public IReadOnlyList<DataProviderType> SupportedTypes =>
        _providers.Keys.ToList().AsReadOnly();

    public IDataProvider GetProvider(DataProviderType type)
    {
        if (_providers.TryGetValue(type, out var provider))
            return provider;

        throw new NotSupportedException(
            $"Data provider '{type}' is not supported. Supported types: {string.Join(", ", _providers.Keys)}");
    }

    public IDataProvider GetProvider(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("Provider type name cannot be empty.", nameof(typeName));

        // Try direct name lookup
        if (_nameMap.TryGetValue(typeName.Trim(), out var providerType))
            return GetProvider(providerType);

        // Try enum parse
        if (Enum.TryParse<DataProviderType>(typeName.Trim(), ignoreCase: true, out var parsed))
            return GetProvider(parsed);

        throw new NotSupportedException(
            $"Unknown data provider '{typeName}'. Known names: {string.Join(", ", _nameMap.Keys.Take(10))}...");
    }
}
