using Alibalabala.Core.Taobao;
using System.Globalization;
using System.Text.Json;

var configOption = ReadConfigOption(args);
var configPath = ResolveConfigPath(configOption.Path);
using var client = new TaobaoClient(LoadConfig(configPath));

try
{
    await client.RefreshCookieAsync();
    SaveConfig(configPath, client.Config);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Cookie 刷新失败: {exception.Message}");
}

var commandLine = configOption.Remaining;
if (commandLine.Length == 0)
{
    PrintUsage();
    return;
}

switch (commandLine[0].ToLowerInvariant())
{
    case "search":
        await SearchAsync(commandLine);
        break;
    case "detail":
        await DetailAsync(commandLine);
        break;
    default:
        PrintUsage();
        break;
}

async Task SearchAsync(string[] commandLine)
{
    if (commandLine.Length < 2)
    {
        PrintUsage();
        return;
    }

    var keyword = commandLine[1];
    var page = commandLine.Length >= 3 && int.TryParse(commandLine[2], out var value)
        ? value
        : 1;
    decimal? minPrice = commandLine.Length >= 4
        ? ParsePrice(commandLine[3], "最低价")
        : null;
    decimal? maxPrice = commandLine.Length >= 5
        ? ParsePrice(commandLine[4], "最高价")
        : null;

    var products = await client.SearchAsync(
        keyword,
        page,
        minPrice: minPrice,
        maxPrice: maxPrice);
    SaveConfig(configPath, client.Config);
    foreach (var (product, index) in products.Select((item, index) => (item, index + 1)))
    {
        Console.WriteLine($"{index}\t{product.ItemId}\t{product.Price}\t{product.Sales}\t{product.Title}");
    }
}

async Task DetailAsync(string[] commandLine)
{
    if (commandLine.Length < 2)
    {
        PrintUsage();
        return;
    }

    var detail = await client.DetailAsync(commandLine[1]);
    SaveConfig(configPath, client.Config);
    Console.WriteLine(detail.Title);
    foreach (var sku in detail.Skus)
    {
        Console.WriteLine($"{sku.SkuId}\t{sku.Price}\t{sku.Quantity}\t{sku.Attributes}");
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("用法: ");
    Console.Error.WriteLine("  Alibalabala.CommandLine search <关键词> [页码] [最低价] [最高价]");
    Console.Error.WriteLine("  Alibalabala.CommandLine detail <商品ID>");
}

static decimal ParsePrice(string value, string name)
{
    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) && price >= 0)
    {
        return price;
    }

    throw new ArgumentException($"{name}格式无效: {value}");
}

static (string? Path, string[] Remaining) ReadConfigOption(string[] arguments)
{
    var remaining = new List<string>();
    string? path = null;
    for (var index = 0; index < arguments.Length; index++)
    {
        if (arguments[index].Equals("--config", StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Length)
        {
            path = arguments[++index];
            continue;
        }
        remaining.Add(arguments[index]);
    }
    return (path, remaining.ToArray());
}

static TaobaoConfig LoadConfig(string path)
{
    return JsonSerializer.Deserialize<TaobaoConfig>(File.ReadAllText(path))
        ?? throw new InvalidOperationException($"无法读取配置文件: {path}");
}

static void SaveConfig(string path, TaobaoConfig config)
{
    var options = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(path, JsonSerializer.Serialize(config, options));
}

static string ResolveConfigPath(string? explicitPath)
{
    if (explicitPath is not null) return Path.GetFullPath(explicitPath);
    var path = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
    if (File.Exists(path)) return path;

    var basePath = Path.Combine(AppContext.BaseDirectory, "config.json");
    if (File.Exists(basePath)) return basePath;

    throw new FileNotFoundException("未找到 config.json, 请使用 --config 指定路径", path);
}



