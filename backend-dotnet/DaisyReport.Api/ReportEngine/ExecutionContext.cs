using DaisyReport.Api.Models;
using MySqlConnector;

namespace DaisyReport.Api.ReportEngine;

public class ExecutionContext
{
    public Report? Report { get; set; }
    public Datasource? Datasource { get; set; }
    public MySqlConnection? Connection { get; set; }
    public Dictionary<string, object?> ResolvedParameters { get; set; } = new();
    public List<ReportParameter>? ParameterDefinitions { get; set; }
}
