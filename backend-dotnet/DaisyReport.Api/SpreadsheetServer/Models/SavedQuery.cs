namespace DaisyReport.Api.SpreadsheetServer.Models;

public class SavedQuery
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public long DatasourceId { get; set; }
    public string QueryType { get; set; } = "SQL"; // SQL or VISUAL
    public string? SqlText { get; set; }
    public string? VisualModel { get; set; } // JSON
    public string? Parameters { get; set; } // JSON array
    public long? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SavedQuerySummary
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public long DatasourceId { get; set; }
    public string QueryType { get; set; } = "";
}

public class ErpConnectorConfig
{
    public long Id { get; set; }
    public long DatasourceId { get; set; }
    public string ErpType { get; set; } = "GENERIC";
    public string? GlBalanceQuery { get; set; }
    public string? GlDetailQuery { get; set; }
    public string? GlRangeQuery { get; set; }
    public string? AccountTable { get; set; }
    public string? AccountColumn { get; set; }
    public string? PeriodTable { get; set; }
    public int FiscalYearStartMonth { get; set; } = 1;
    public string? SegmentFormat { get; set; }
    public string? Config { get; set; }
}
