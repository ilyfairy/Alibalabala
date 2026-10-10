using Alibalabala.Core.Taobao;
using System.Text.Json;

namespace Alibalabala.Mcp;

public static class McpConfigLoader
{
    static readonly JsonSerializerOptions JsonOptions = new();

    public static TaobaoConfig Load(string path)
    {
        return JsonSerializer.Deserialize<TaobaoConfig>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"无法读取配置文件: {path}");
    }

    public static string ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var currentPath = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
        if (File.Exists(currentPath))
        {
            return currentPath;
        }

        var basePath = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (File.Exists(basePath))
        {
            return basePath;
        }

        throw new FileNotFoundException("未找到 config.json, 请使用 --config 指定路径", currentPath);
    }
}



