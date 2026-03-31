namespace DaisyReport.Api.SpreadsheetServer.GlEngine.Models;

public class GlTableMapping
{
    public string? BalanceTable { get; set; }      // e.g., GL_BALANCE, GLBALANCE
    public string? DetailTable { get; set; }       // e.g., GL_DETAIL, GLTRANS
    public string? AccountTable { get; set; }      // e.g., GL_ACCOUNT, GLACCOUNT
    public string? PeriodTable { get; set; }       // e.g., GL_PERIOD, FISCAL_PERIOD
    public string? AccountColumn { get; set; }     // e.g., ACCOUNT, ACCT_NO
    public string? PeriodColumn { get; set; }      // e.g., PERIOD, FISCAL_PERIOD
    public string? YearColumn { get; set; }        // e.g., FISCAL_YEAR, FY
    public string? AmountColumn { get; set; }      // e.g., AMOUNT, BALANCE, NET_AMOUNT
    public string? DebitColumn { get; set; }       // e.g., DEBIT, DR_AMT
    public string? CreditColumn { get; set; }      // e.g., CREDIT, CR_AMT
    public float Confidence { get; set; }          // 0.0 to 1.0
}
