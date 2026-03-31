namespace DaisyReport.Api.SpreadsheetServer.GlEngine.Models;

public class FiscalPeriod
{
    public int PeriodNumber { get; set; }
    public int FiscalYear { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? PeriodName { get; set; }
    public bool IsClosed { get; set; }
    public bool IsAdjustment { get; set; }
}
