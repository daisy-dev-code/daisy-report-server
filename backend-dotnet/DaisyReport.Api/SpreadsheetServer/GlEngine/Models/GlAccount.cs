namespace DaisyReport.Api.SpreadsheetServer.GlEngine.Models;

public class GlAccount
{
    public string AccountNumber { get; set; } = string.Empty;
    public string? AccountDescription { get; set; }
    public string? AccountType { get; set; }       // Asset, Liability, Equity, Revenue, Expense
    public string? AccountCategory { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, string> Segments { get; set; } = new();
}
