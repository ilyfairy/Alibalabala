using Alibalabala.Core.Taobao;
using Spectre.Console;
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
    AnsiConsole.MarkupLine($"[yellow]Cookie 刷新失败: {Markup.Escape(exception.Message)}[/]");
}

AnsiConsole.MarkupLine("[bold orange1]淘宝商品搜索[/]");
AnsiConsole.MarkupLine("输入关键词, 可选页码, 最低价和最高价; 输入 [yellow]exit[/] 退出.\n");

while (true)
{
    var input = AnsiConsole.Ask<string>("[green]search>[/]");
    if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
    {
        continue;
    }

    try
    {
        var page = parts.Length > 1 && int.TryParse(parts[1], out var pageValue)
            ? pageValue
            : 1;
        var minPrice = parts.Length > 2 &&
                       decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var minValue)
            ? minValue
            : (decimal?)null;
        var maxPrice = parts.Length > 3 &&
                       decimal.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var maxValue)
            ? maxValue
            : (decimal?)null;
        var products = (await client.SearchAsync(
            parts[0],
            page,
            maxResults: 10,
            minPrice: minPrice,
            maxPrice: maxPrice)).ToList();
        SaveConfig(configPath, client.Config);
        if (products.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]没有找到商品.[/]");
            continue;
        }

        var selected = SelectProduct(products);
        if (selected is null)
        {
            continue;
        }

        var detail = await client.DetailAsync(products[selected.Value].ItemId);
        SaveConfig(configPath, client.Config);
        ShowDetail(detail);
    }
    catch (Exception exception)
    {
        SaveConfig(configPath, client.Config);
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");
    }
}

static int? SelectProduct(IReadOnlyList<TaobaoProduct> products)
{
    var selectedIndex = 0;

    while (true)
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[bold orange1]淘宝商品搜索[/]");
        AnsiConsole.MarkupLine("方向键上下选择商品, Enter 查看商品详情, Esc 返回搜索.\n");

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("#");
        table.AddColumn("商品");
        table.AddColumn("价格");
        table.AddColumn("销量");
        table.AddColumn("itemId");

        for (var index = 0; index < products.Count; index++)
        {
            var active = index == selectedIndex;
            var product = products[index];
            table.AddRow(
                Cell((index + 1).ToString(), active),
                Cell(product.Title, active),
                Cell(product.Price, active),
                Cell(product.Sales, active),
                Cell(product.ItemId, active));
        }

        AnsiConsole.Write(table);
        var key = Console.ReadKey(true).Key;

        if (key == ConsoleKey.UpArrow)
        {
            selectedIndex = selectedIndex == 0 ? products.Count - 1 : selectedIndex - 1;
        }
        else if (key == ConsoleKey.DownArrow)
        {
            selectedIndex = selectedIndex == products.Count - 1 ? 0 : selectedIndex + 1;
        }
        else if (key == ConsoleKey.Enter)
        {
            return selectedIndex;
        }
        else if (key == ConsoleKey.Escape)
        {
            return null;
        }
    }
}

static void ShowDetail(TaobaoProductDetail detail)
{
    AnsiConsole.Clear();
    AnsiConsole.MarkupLine($"[bold]{Markup.Escape(detail.Title)}[/]");

    var table = new Table().Border(TableBorder.Rounded);
    table.AddColumn("#");
    table.AddColumn("skuId");
    table.AddColumn("属性");
    table.AddColumn("价格");
    table.AddColumn("库存");

    foreach (var (sku, index) in detail.Skus.Select((item, index) => (item, index + 1)))
    {
        table.AddRow(index.ToString(), sku.SkuId, Markup.Escape(sku.Attributes), sku.Price, sku.Quantity);
    }

    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine("按任意键返回搜索");
    Console.ReadKey(true);
}

static Markup Cell(string value, bool active)
{
    var escaped = Markup.Escape(value ?? string.Empty);
    return new Markup(active ? $"[black on blue]{escaped}[/]" : escaped);
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



