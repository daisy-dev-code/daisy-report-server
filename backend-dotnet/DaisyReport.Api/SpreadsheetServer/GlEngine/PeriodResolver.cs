using System.Text.RegularExpressions;

namespace DaisyReport.Api.SpreadsheetServer.GlEngine;

/// <summary>
/// Resolves GL period references and date ranges for fiscal period calculations.
/// </summary>
public class PeriodResolver
{
    /// <summary>
    /// Resolve period references like "CUR", "CUR-1", "CUR+2", or plain integers.
    /// </summary>
    public int ResolvePeriod(string periodRef, int currentPeriod)
    {
        if (string.IsNullOrWhiteSpace(periodRef))
            return currentPeriod;

        var trimmed = periodRef.Trim().ToUpperInvariant();

        // Plain integer
        if (int.TryParse(trimmed, out var plainPeriod))
            return plainPeriod;

        // CUR with optional offset: CUR, CUR-1, CUR+3
        var match = Regex.Match(trimmed, @"^CUR(?:\s*([+-])\s*(\d+))?$");
        if (match.Success)
        {
            if (!match.Groups[1].Success)
                return currentPeriod;

            var offset = int.Parse(match.Groups[2].Value);
            return match.Groups[1].Value == "-"
                ? currentPeriod - offset
                : currentPeriod + offset;
        }

        // Fallback: return current
        return currentPeriod;
    }

    /// <summary>
    /// Get the start and end dates for a given fiscal period and year.
    /// fiscalYearStartMonth: 1 = calendar year, 7 = July fiscal year, etc.
    /// </summary>
    public (DateTime Start, DateTime End) GetPeriodDateRange(int period, int year, int fiscalYearStartMonth = 1)
    {
        if (period < 0 || period > 13)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be between 0 and 13.");

        // Period 0 is the beginning balance / opening entry -- return the day before fiscal year start
        if (period == 0)
        {
            var fyStart = GetFiscalYearStart(year, fiscalYearStartMonth);
            return (fyStart.AddDays(-1), fyStart.AddDays(-1));
        }

        // Period 13 is typically an adjustment period mapped to the last day of the fiscal year
        if (period == 13)
        {
            var fyEnd = GetFiscalYearEnd(year, fiscalYearStartMonth);
            return (fyEnd, fyEnd);
        }

        // Regular periods 1-12: map to months offset from fiscal year start
        var monthOffset = period - 1;
        var startMonth = ((fiscalYearStartMonth - 1 + monthOffset) % 12) + 1;
        var startYear = year;

        // If the fiscal year start is not January, periods may span into the next calendar year
        if (fiscalYearStartMonth > 1 && startMonth < fiscalYearStartMonth)
            startYear++;

        var start = new DateTime(startYear, startMonth, 1);
        var end = start.AddMonths(1).AddDays(-1);

        return (start, end);
    }

    /// <summary>
    /// Get the list of period numbers that should be summed for a given balance type.
    /// </summary>
    public List<int> GetPeriodsForBalanceType(string balanceType, int period, int year)
    {
        var bt = (balanceType ?? "PTD").Trim().ToUpperInvariant();

        return bt switch
        {
            // Period-to-date: just the specified period
            "PTD" => new List<int> { period },

            // Year-to-date: period 1 through specified period
            "YTD" => Enumerable.Range(1, period).ToList(),

            // Quarter-to-date: quarter start through specified period
            "QTD" => GetQuarterPeriods(period),

            // Balance: beginning balance (period 0) + YTD (periods 1..period)
            "BAL" => new List<int> { 0 }.Concat(Enumerable.Range(1, period)).ToList(),

            // Beginning balance only: period 0
            "BEG" => new List<int> { 0 },

            _ => new List<int> { period }
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static List<int> GetQuarterPeriods(int period)
    {
        // Quarters: 1-3, 4-6, 7-9, 10-12
        var quarterStart = ((period - 1) / 3) * 3 + 1;
        var count = period - quarterStart + 1;
        return Enumerable.Range(quarterStart, count).ToList();
    }

    private static DateTime GetFiscalYearStart(int fiscalYear, int fiscalYearStartMonth)
    {
        // If fiscal year starts in Jan, it's simply Jan 1 of that year.
        // If fiscal year starts in July (month=7) and fiscal year is 2026,
        // then start date is July 1, 2025.
        var calendarYear = fiscalYearStartMonth == 1
            ? fiscalYear
            : fiscalYear - 1;
        return new DateTime(calendarYear, fiscalYearStartMonth, 1);
    }

    private static DateTime GetFiscalYearEnd(int fiscalYear, int fiscalYearStartMonth)
    {
        var start = GetFiscalYearStart(fiscalYear, fiscalYearStartMonth);
        return start.AddYears(1).AddDays(-1);
    }
}
