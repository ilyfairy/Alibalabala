using Alibalabala.Core.Taobao;
using System.Text.Json;

namespace Alibalabala.Mcp;

public sealed class McpConfigStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    readonly SemaphoreSlim gate = new(1, 1);

    public McpConfigStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public async Task SaveAsync(TaobaoConfig config, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllTextAsync(Path, json, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        gate.Dispose();
    }
}



