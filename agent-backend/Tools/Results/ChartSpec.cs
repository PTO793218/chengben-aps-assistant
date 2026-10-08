namespace WePilot.Agent.Tools.Results;

public sealed class ChartSpec
{
    public string ChartType { get; set; } = "bar";

    public string XField { get; set; } = "name";

    public string YField { get; set; } = "value";

    public List<Dictionary<string, object?>> Data { get; set; } = new();
}
