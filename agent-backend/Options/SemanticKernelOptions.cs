namespace WePilot.Agent.Options;

public sealed class SemanticKernelOptions
{
    public string Endpoint { get; set; } = "https://ark.cn-beijing.volces.com/api/v3";

    public string ApiKey { get; set; } = "";

    public string ModelId { get; set; } = "";

    public int TimeoutMs { get; set; } = 120000;

    public int MaxOutputTokens { get; set; } = 1600;

    public double Temperature { get; set; } = 0.2;
}
