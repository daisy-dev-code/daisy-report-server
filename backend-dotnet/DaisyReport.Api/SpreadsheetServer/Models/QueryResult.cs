namespace DaisyReport.Api.SpreadsheetServer.Models;

public class QueryResult
{
    public List<ColumnInfo> Columns { get; set; } = new();
    public List<object?[]> Rows { get; set; } = new(); // Array of arrays for wire efficiency
    public int TotalRows { get; set; }
    public long ExecutionTimeMs { get; set; }
    public bool Cached { get; set; }
    public string? Error { get; set; }
}

public class ColumnInfo
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
}

public class ScalarResult
{
    public object? Value { get; set; }
    public long ExecutionTimeMs { get; set; }
    public bool Cached { get; set; }
    public string? Error { get; set; }
}
