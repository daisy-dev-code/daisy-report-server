namespace DaisyReport.Api.Models;

public class Dadget
{
    public long Id { get; set; }
    public long DashboardId { get; set; }
    public string Dtype { get; set; } = string.Empty;
    public int ColPosition { get; set; }
    public int RowPosition { get; set; }
    public int WidthSpan { get; set; } = 1;
    public int? Height { get; set; }
    public string Config { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class DadgetPosition
{
    public long DadgetId { get; set; }
    public int ColPosition { get; set; }
    public int RowPosition { get; set; }
    public int WidthSpan { get; set; }
    public int? Height { get; set; }
}
