using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using WePilot.Agent.Options;
using WePilot.Agent.Tools;

namespace WePilot.Agent.Infrastructure.Ai;
public sealed class KernelFactory
{
    private readonly SemanticKernelOptions _options;
    private readonly SampleTools _tools;
    private readonly ApsAnalysisTools _aps;
    private readonly BusinessOrderDispatch _orderDispatch;
    private readonly BusinessMaterialReadiness _materials;
    private readonly IHttpClientFactory _clients;
    public KernelFactory(IOptions<SemanticKernelOptions> options, SampleTools tools, ApsAnalysisTools aps, BusinessOrderDispatch orderDispatch, BusinessMaterialReadiness materials, IHttpClientFactory clients)
        => (_options, _tools, _aps, _orderDispatch, _materials, _clients) = (options.Value, tools, aps, orderDispatch, materials, clients);
    public Kernel CreateKernel()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ModelId))
            throw new InvalidOperationException("Model credentials are not configured.");
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId: _options.ModelId, endpoint: new Uri(_options.Endpoint), apiKey: _options.ApiKey,
            httpClient: _clients.CreateClient("model"));
        builder.Plugins.AddFromObject(_tools, "tools");
        builder.Plugins.AddFromObject(_aps, "aps");
        builder.Plugins.AddFromObject(_orderDispatch, "orderDispatch");
        builder.Plugins.AddFromObject(_materials, "materials");
        return builder.Build();
    }
}
