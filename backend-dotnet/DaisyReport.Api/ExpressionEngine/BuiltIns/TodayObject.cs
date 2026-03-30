using System.Globalization;

namespace DaisyReport.Api.ExpressionEngine.BuiltIns;

/// <summary>
/// Provides the "today" built-in object with 18 chainable date methods.
/// Each mutation returns a new DateTime; the original is never modified.
/// Date arithmetic clamps to the last valid day of the target month.
/// </summary>
public class TodayObject
{
    private DateTime _date;

    public TodayObject()
    {
        _date = DateTime.Today;
    }

    public TodayObject(DateTime date)
    {
        _date = date;
    }

    public DateTime Date => _date;

    public object? Invoke(string method, List<object?> args)
    {
        return method.ToLowerInvariant() switch
        {
            "adddays" => new TodayObject(AddDaysSafe(RequireInt(args, 0, method))),
            "addmonths" => new TodayObject(AddMonthsSafe(RequireInt(args, 0, method))),
            "addyears" => new TodayObject(AddYearsSafe(RequireInt(args, 0, method))),
            "firstday" => new TodayObject(new DateTime(_date.Year, _date.Month, 1, _date.Hour, _date.Minute, _date.Second)),
            "lastday" => new TodayObject(new DateTime(_date.Year, _date.Month, DateTime.DaysInMonth(_date.Year, _date.Month), _date.Hour, _date.Minute, _date.Second)),
            "setday" => new TodayObject(SetDayClamped(RequireInt(args, 0, method))),
            "setmonth" => new TodayObject(SetMonthClamped(RequireInt(args, 0, method))),
            "setyear" => new TodayObject(SetYearClamped(RequireInt(args, 0, method))),
            "cleartime" => new TodayObject(_date.Date),
            "addhours" => new TodayObject(_date.AddHours(RequireDouble(args, 0, method))),
            "addminutes" => new TodayObject(_date.AddMinutes(RequireDouble(args, 0, method))),
            "addseconds" => new TodayObject(_date.AddSeconds(RequireDouble(args, 0, method))),
            "sethours" => new TodayObject(new DateTime(_date.Year, _date.Month, _date.Day, ClampInt(RequireInt(args, 0, method), 0, 23), _date.Minute, _date.Second)),
            "setminutes" => new TodayObject(new DateTime(_date.Year, _date.Month, _date.Day, _date.Hour, ClampInt(RequireInt(args, 0, method), 0, 59), _date.Second)),
            "setseconds" => new TodayObject(new DateTime(_date.Year, _date.Month, _date.Day, _date.Hour, _date.Minute, ClampInt(RequireInt(args, 0, method), 0, 59))),
            "format" => _date.ToString(RequireString(args, 0, method), CultureInfo.InvariantCulture),
            "getday" => (double)_date.Day,
            "getmonth" => (double)_date.Month,
            "getyear" => (double)_date.Year,
            _ => throw new InvalidOperationException($"Unknown method 'today.{method}'")
        };
    }

    private DateTime AddDaysSafe(int days)
    {
        try { return _date.AddDays(days); }
        catch (ArgumentOutOfRangeException) { return days > 0 ? DateTime.MaxValue : DateTime.MinValue; }
    }

    private DateTime AddMonthsSafe(int months)
    {
        int targetMonth = _date.Month + months;
        int targetYear = _date.Year;

        // Normalize month/year
        targetYear += (targetMonth - 1) / 12;
        targetMonth = ((targetMonth - 1) % 12) + 1;
        if (targetMonth <= 0)
        {
            targetMonth += 12;
            targetYear--;
        }

        // Handle negative months wrapping
        while (targetMonth <= 0)
        {
            targetMonth += 12;
            targetYear--;
        }
        while (targetMonth > 12)
        {
            targetMonth -= 12;
            targetYear++;
        }

        targetYear = ClampInt(targetYear, 1, 9999);
        int maxDay = DateTime.DaysInMonth(targetYear, targetMonth);
        int day = Math.Min(_date.Day, maxDay);

        return new DateTime(targetYear, targetMonth, day, _date.Hour, _date.Minute, _date.Second);
    }

    private DateTime AddYearsSafe(int years)
    {
        int targetYear = ClampInt(_date.Year + years, 1, 9999);
        int month = _date.Month;
        int maxDay = DateTime.DaysInMonth(targetYear, month);
        int day = Math.Min(_date.Day, maxDay);
        return new DateTime(targetYear, month, day, _date.Hour, _date.Minute, _date.Second);
    }

    private DateTime SetDayClamped(int day)
    {
        int maxDay = DateTime.DaysInMonth(_date.Year, _date.Month);
        day = ClampInt(day, 1, maxDay);
        return new DateTime(_date.Year, _date.Month, day, _date.Hour, _date.Minute, _date.Second);
    }

    private DateTime SetMonthClamped(int month)
    {
        month = ClampInt(month, 1, 12);
        int maxDay = DateTime.DaysInMonth(_date.Year, month);
        int day = Math.Min(_date.Day, maxDay);
        return new DateTime(_date.Year, month, day, _date.Hour, _date.Minute, _date.Second);
    }

    private DateTime SetYearClamped(int year)
    {
        year = ClampInt(year, 1, 9999);
        int maxDay = DateTime.DaysInMonth(year, _date.Month);
        int day = Math.Min(_date.Day, maxDay);
        return new DateTime(year, _date.Month, day, _date.Hour, _date.Minute, _date.Second);
    }

    private static int ClampInt(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    private static int RequireInt(List<object?> args, int index, string method)
    {
        if (args.Count <= index || args[index] is null)
            throw new InvalidOperationException($"Method 'today.{method}' requires argument at position {index}");

        return Convert.ToInt32(ToDouble(args[index]!));
    }

    private static double RequireDouble(List<object?> args, int index, string method)
    {
        if (args.Count <= index || args[index] is null)
            throw new InvalidOperationException($"Method 'today.{method}' requires argument at position {index}");

        return ToDouble(args[index]!);
    }

    private static string RequireString(List<object?> args, int index, string method)
    {
        if (args.Count <= index || args[index] is null)
            throw new InvalidOperationException($"Method 'today.{method}' requires a string argument at position {index}");

        return args[index]!.ToString()!;
    }

    private static double ToDouble(object value) => value switch
    {
        double d => d,
        int i => i,
        float f => f,
        long l => l,
        _ => Convert.ToDouble(value)
    };
}
