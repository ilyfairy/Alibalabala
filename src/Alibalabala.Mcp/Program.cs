using Alibalabala.Core.Taobao;
using Alibalabala.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var httpMode = args.Any(argument => argument.Equals("--http", StringComparison.OrdinalIgnoreCase));
var configPath = ReadConfigPath(args);
if (httpMode)
{
    await RunHttpAsync(args, configPath);
}
else
{
    await RunStdioAsync(args, configPath);
}

static async Task RunStdioAsync(string[] args, string? configPath)
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    RegisterCore(builder.Services, configPath);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
    using var host = builder.Build();
    await RefreshConfigAsync(host.Services);
    await host.RunAsync();
}

static async Task RunHttpAsync(string[] args, string? configPath)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls(GetHttpUrl(args));
    RegisterCore(builder.Services, configPath);
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithToolsFromAssembly();
    var app = builder.Build();
    app.MapGet("/", () => Results.Ok(new { name = "Alibalabala", transport = "http", endpoint = "/mcp" }));
    app.MapMcp();
    await RefreshConfigAsync(app.Services);
    await app.RunAsync();
}

static async Task RefreshConfigAsync(IServiceProvider services)
{
    var client = services.GetRequiredService<TaobaoClient>();
    var configStore = services.GetRequiredService<McpConfigStore>();
    try
    {
        await client.RefreshCookieAsync();
        await configStore.SaveAsync(client.Config);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Cookie 刷新失败: {exception.Message}");
    }
}

static void RegisterCore(IServiceCollection services, string? configPath)
{
    var path = McpConfigLoader.ResolvePath(configPath);
    var config = McpConfigLoader.Load(path);
    services.AddSingleton(config);
    services.AddSingleton(new McpConfigStore(path));
    services.AddSingleton(_ => new TaobaoClient(config));
}

static string? ReadConfigPath(string[] arguments)
{
    var index = Array.FindIndex(arguments, argument => argument.Equals("--config", StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

static string GetHttpUrl(string[] args)
{
    var index = Array.FindIndex(args, argument => argument.Equals("--http", StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length && Uri.TryCreate(args[index + 1], UriKind.Absolute, out _)
        ? args[index + 1]
        : "http://127.0.0.1:5070";
}




