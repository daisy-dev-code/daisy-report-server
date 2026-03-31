using System.Data;
using DaisyReport.Api.DataProviders.Abstractions;
using DaisyReport.Api.SpreadsheetServer.GlEngine.Models;

namespace DaisyReport.Api.SpreadsheetServer.GlEngine;

/// <summary>
/// Auto-detects GL tables and columns in a database by scanning for common naming patterns
/// used across ERP systems (Sage, SAP, GP, Pastel, Xero, generic, etc.).
/// </summary>
public class GlTableDetector
{
    // Common table name patterns grouped by purpose
    private static readonly string[][] BalanceTablePatterns =
    {
        new[] { "GL_BALANCE", "GLBALANCE", "GL_BALANCES", "GLBAL" },
        new[] { "GENERAL_LEDGER_BALANCE", "LEDGER_BALANCE", "GL_ACCT_BALANCE" },
        new[] { "GL_SUMMARY", "GLSUMMARY", "GL_PERIOD_BALANCE" },
    };

    private static readonly string[][] DetailTablePatterns =
    {
        new[] { "GL_DETAIL", "GLDETAIL", "GL_TRANS", "GLTRANS" },
        new[] { "GL_TRANSACTION", "GLTRANSACTION", "GL_ENTRY", "GLENTRY" },
        new[] { "GL_JOURNAL", "GLJOURNAL", "JOURNAL_ENTRY", "JOURNAL_LINE" },
        new[] { "GENERAL_LEDGER", "GENERAL_LEDGER_DETAIL", "LEDGER_TRANS" },
    };

    private static readonly string[][] AccountTablePatterns =
    {
        new[] { "GL_ACCOUNT", "GLACCOUNT", "GL_ACCOUNTS", "GLACCOUNTS" },
        new[] { "CHART_OF_ACCOUNTS", "COA", "ACCOUNT_MASTER" },
        new[] { "GL_CHART", "GLCHART", "ACCOUNT", "ACCOUNTS" },
    };

    private static readonly string[][] PeriodTablePatterns =
    {
        new[] { "GL_PERIOD", "GLPERIOD", "FISCAL_PERIOD", "FISCAL_PERIODS" },
        new[] { "ACCOUNTING_PERIOD", "FIN_PERIOD", "GL_CALENDAR" },
    };

    // Column name patterns
    private static readonly string[] AccountColumnPatterns =
        { "ACCOUNT", "ACCT_NO", "ACCOUNT_NO", "ACCOUNT_NUMBER", "ACCTNO", "GL_ACCOUNT", "GLACCOUNT", "ACCT_CODE", "ACCOUNT_CODE" };

    private static readonly string[] PeriodColumnPatterns =
        { "PERIOD", "FISCAL_PERIOD", "PERIOD_NO", "PERIOD_NUMBER", "FIN_PERIOD", "ACCT_PERIOD", "POSTING_PERIOD" };

    private static readonly string[] YearColumnPatterns =
        { "FISCAL_YEAR", "FY", "YEAR", "ACCT_YEAR", "FIN_YEAR", "POSTING_YEAR", "FISCAL_YR" };

    private static readonly string[] AmountColumnPatterns =
        { "AMOUNT", "BALANCE", "NET_AMOUNT", "NET_BALANCE", "TOTAL", "NET_AMT", "PERIOD_BALANCE", "PERIOD_AMOUNT" };

    private static readonly string[] DebitColumnPatterns =
        { "DEBIT", "DR_AMT", "DEBIT_AMT", "DEBIT_AMOUNT", "DR", "DEBITS" };

    private static readonly string[] CreditColumnPatterns =
        { "CREDIT", "CR_AMT", "CREDIT_AMT", "CREDIT_AMOUNT", "CR", "CREDITS" };

    /// <summary>
    /// Scan the database for common GL table patterns and return a mapping with confidence score.
    /// Returns null if no GL tables could be detected.
    /// </summary>
    public async Task<GlTableMapping?> DetectAsync(IDbConnection connection, IDataProvider provider)
    {
        // Get all tables in the database
        var tables = await provider.GetTablesAsync(connection);
        if (tables.Count == 0)
            return null;

        var tableNames = tables.Select(t => t.Name).ToList();
        var tableNamesUpper = tableNames.Select(t => t.ToUpperInvariant()).ToList();

        var mapping = new GlTableMapping();
        float totalScore = 0;
        float maxScore = 4; // balance + detail + account + period tables

        // Match tables
        mapping.BalanceTable = FindTable(tableNames, tableNamesUpper, BalanceTablePatterns);
        if (mapping.BalanceTable != null) totalScore += 1;

        mapping.DetailTable = FindTable(tableNames, tableNamesUpper, DetailTablePatterns);
        if (mapping.DetailTable != null) totalScore += 1;

        mapping.AccountTable = FindTable(tableNames, tableNamesUpper, AccountTablePatterns);
        if (mapping.AccountTable != null) totalScore += 1;

        mapping.PeriodTable = FindTable(tableNames, tableNamesUpper, PeriodTablePatterns);
        if (mapping.PeriodTable != null) totalScore += 1;

        // Need at least a balance or detail table to be useful
        if (mapping.BalanceTable == null && mapping.DetailTable == null)
            return null;

        // Detect columns in the primary table (balance table preferred, then detail)
        var primaryTable = mapping.BalanceTable ?? mapping.DetailTable!;
        var columns = await provider.GetColumnsAsync(connection, primaryTable);
        var colNames = columns.Select(c => c.Name).ToList();
        var colNamesUpper = colNames.Select(c => c.ToUpperInvariant()).ToList();

        mapping.AccountColumn = FindColumn(colNames, colNamesUpper, AccountColumnPatterns);
        mapping.PeriodColumn = FindColumn(colNames, colNamesUpper, PeriodColumnPatterns);
        mapping.YearColumn = FindColumn(colNames, colNamesUpper, YearColumnPatterns);
        mapping.AmountColumn = FindColumn(colNames, colNamesUpper, AmountColumnPatterns);
        mapping.DebitColumn = FindColumn(colNames, colNamesUpper, DebitColumnPatterns);
        mapping.CreditColumn = FindColumn(colNames, colNamesUpper, CreditColumnPatterns);

        // Add column confidence
        float colScore = 0;
        float colMax = 4; // account, period, year, amount/debit-credit
        if (mapping.AccountColumn != null) colScore += 1;
        if (mapping.PeriodColumn != null) colScore += 1;
        if (mapping.YearColumn != null) colScore += 1;
        if (mapping.AmountColumn != null || (mapping.DebitColumn != null && mapping.CreditColumn != null))
            colScore += 1;

        mapping.Confidence = (totalScore / maxScore * 0.5f) + (colScore / colMax * 0.5f);

        return mapping;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string? FindTable(List<string> tableNames, List<string> tableNamesUpper, string[][] patterns)
    {
        // Try exact match first (case-insensitive), in priority order
        foreach (var group in patterns)
        {
            foreach (var pattern in group)
            {
                var idx = tableNamesUpper.IndexOf(pattern);
                if (idx >= 0)
                    return tableNames[idx]; // Return with original casing
            }
        }

        // Try contains match for partial matches (e.g., "dbo_GL_BALANCE" or "erp_glbalance")
        foreach (var group in patterns)
        {
            foreach (var pattern in group)
            {
                for (int i = 0; i < tableNamesUpper.Count; i++)
                {
                    if (tableNamesUpper[i].Contains(pattern))
                        return tableNames[i];
                }
            }
        }

        return null;
    }

    private static string? FindColumn(List<string> colNames, List<string> colNamesUpper, string[] patterns)
    {
        // Exact match first
        foreach (var pattern in patterns)
        {
            var idx = colNamesUpper.IndexOf(pattern);
            if (idx >= 0)
                return colNames[idx];
        }

        // Contains match
        foreach (var pattern in patterns)
        {
            for (int i = 0; i < colNamesUpper.Count; i++)
            {
                if (colNamesUpper[i].Contains(pattern))
                    return colNames[i];
            }
        }

        return null;
    }
}
