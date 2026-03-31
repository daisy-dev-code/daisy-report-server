namespace DaisyReport.Api.SpreadsheetServer.Models;

public class QueryRequest
{
    public long ConnectionId { get; set; }
    public string QueryNameOrSql { get; set; } = "";
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public int MaxRows { get; set; } = 10000;
    public int TimeoutSeconds { get; set; } = 30;
}

public class AggregateRequest
{
    public long ConnectionId { get; set; }
    public string QueryNameOrSql { get; set; } = "";
    public string AggregateColumn { get; set; } = "";
    public string AggregateFunction { get; set; } = "SUM"; // SUM, AVG, COUNT, MIN, MAX
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

public class LookupRequest
{
    public long ConnectionId { get; set; }
    public string QueryNameOrSql { get; set; } = "";
    public string ReturnColumn { get; set; } = "";
    public string LookupColumn { get; set; } = "";
    public object? LookupValue { get; set; }
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

public class GlBalanceRequest
{
    public long ConnectionId { get; set; }
    public string Account { get; set; } = "";
    public int Period { get; set; }
    public int Year { get; set; }
    public string BalanceType { get; set; } = "YTD"; // PTD, YTD, QTD, BAL, BEG
}

public class GlDetailRequest
{
    public long ConnectionId { get; set; }
    public string Account { get; set; } = "";
    public int Period { get; set; }
    public int Year { get; set; }
    public int MaxRows { get; set; } = 1000;
}

public class GlRangeRequest
{
    public long ConnectionId { get; set; }
    public string AccountFrom { get; set; } = "";
    public string AccountTo { get; set; } = "";
    public int Period { get; set; }
    public int Year { get; set; }
    public string BalanceType { get; set; } = "YTD";
}

public class DrilldownRequest
{
    public long ConnectionId { get; set; }
    public string SourceQuery { get; set; } = "";
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public string? DrilldownColumn { get; set; }
    public object? DrilldownValue { get; set; }
    public int MaxRows { get; set; } = 1000;
}
