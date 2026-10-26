using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using WePilot.Agent.Options;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Infrastructure.Ai;
public sealed class KernelFactory : IDisposable
{
    private readonly SemanticKernelOptions _options;
    private readonly SampleTools _tools;
    private readonly ApsAnalysisTools _aps;
    private readonly BusinessOrderDispatch _orderDispatch;
    private readonly BusinessMaterialReadiness _materials;
    private readonly IHttpClientFactory _clients;
    private readonly ToolExecutionContext _context;
    private readonly ILogger<DeepSeekCompatibilityHandler> _logger;
    private HttpClient? _modelClient;
    public KernelFactory(IOptions<SemanticKernelOptions> options, SampleTools tools, ApsAnalysisTools aps, BusinessOrderDispatch orderDispatch, BusinessMaterialReadiness materials, IHttpClientFactory clients,ToolExecutionContext context, ILogger<DeepSeekCompatibilityHandler> logger)
        => (_options, _tools, _aps, _orderDispatch, _materials, _clients,_context,_logger) = (options.Value, tools, aps, orderDispatch, materials, clients,context,logger);
    public Kernel CreateKernel()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ModelId))
            throw new InvalidOperationException("Model credentials are not configured.");
        var builder = Kernel.CreateBuilder();
        if (_modelClient == null)
        {
            var upstream = _clients.CreateClient("model");
            _modelClient = new Uri(_options.Endpoint).Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase)
                ? new HttpClient(new DeepSeekCompatibilityHandler(upstream, _options, _logger)) { Timeout = Timeout.InfiniteTimeSpan }
                : upstream;
        }
        builder.AddOpenAIChatCompletion(modelId: _options.ModelId, endpoint: new Uri(_options.Endpoint), apiKey: _options.ApiKey,
            httpClient: _modelClient);
        builder.Plugins.AddFromObject(_tools, "tools");
        builder.Plugins.AddFromObject(_aps, "aps");
        builder.Plugins.AddFromObject(_orderDispatch, "orderDispatch");
        builder.Plugins.AddFromObject(_materials, "materials");
        if(_context.DemoPlan?.SchemaVersion==4)builder.Plugins.AddFromObject(new NativeDemoModelTools(_context),"demos");
        else builder.Plugins.AddFromObject(new InteractiveDemoModelTools(_context), "demos");
        return builder.Build();
    }
    public void Dispose() => _modelClient?.Dispose();
}
