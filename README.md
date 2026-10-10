# Alibalabala

Alibalabala 是一个面向阿里系商品接口的 .NET 项目, 当前实现淘宝商品搜索和商品详情查询

## 项目结构

- `Alibalabala.Core` - 核心库
- `Alibalabala.Demo` - 交互式控制台, 支持方向键选择商品和查看商品详情
- `Alibalabala.CommandLine` - 使用命令行参数执行搜索和详情查询
- `Alibalabala.Mcp` - 使用官方 MCP SDK 的工具适配器, 支持 stdio 和 HTTP

## 配置

先复制模板并填写 Cookie:

```powershell
Copy-Item config.example.json config.json
```

`config.json` 是运行时状态文件, 已加入 `.gitignore`. Demo, CommandLine 和 MCP 各自负责读取和保存这个文件. 客户端收到新的 Cookie 后, 宿主程序会把内存中的配置写回文件

在其他程序中可以直接创建客户端:

```csharp
var config = new TaobaoConfig
{
    Cookie = "your-cookie"
};

using var client = new TaobaoClient(config);
var products = await client.SearchAsync("笔记本电脑");
```

## Demo

```powershell
dotnet run --project src/Alibalabala.Demo
```

输入关键词, 可选输入页码, 最低价和最高价. 搜索结果最多显示 10 行, 使用方向键移动高亮项, 按 Enter 查看商品详情, 按 Esc 返回搜索

输入格式为 `关键词 [页码] [最低价] [最高价]`

可以使用 `--config` 指定配置文件:

```powershell
dotnet run --project src/Alibalabala.Demo -- --config D:\path\config.json
```

## CommandLine

搜索商品:

```powershell
dotnet run --project src/Alibalabala.CommandLine -- search 4060 1
```

查询详情:

```powershell
dotnet run --project src/Alibalabala.CommandLine -- detail 823541228896
```

搜索时也可以指定价格范围:

```powershell
dotnet run --project src/Alibalabala.CommandLine -- search 4060 1 4000 8000
```

CommandLine 返回搜索到的全部商品

## MCP

MCP 默认使用 stdio:

```powershell
dotnet run --project src/Alibalabala.Mcp
```

传入 `--http` 后切换为 HTTP, 默认监听 `http://127.0.0.1:5070`:

```powershell
dotnet run --project src/Alibalabala.Mcp -- --http
```

也可以指定监听地址:

```powershell
dotnet run --project src/Alibalabala.Mcp -- --http http://127.0.0.1:8080
```

HTTP MCP 端点为 `POST /mcp`, 根路径 `GET /` 用于健康检查

MCP 提供以下工具:

- `search_products` - 搜索商品并返回全部解析结果
- `product_detail` - 查询商品标题, 规格, 价格和库存

MCP 工具直接返回结构化对象, 搜索支持最低价格和最高价格

MCP 项目的 `appsettings.json` 只用于 ASP.NET Core 的日志和主机配置, 运行时 Cookie 仍保存在根目录的 `config.json`

## 核心接口

核心客户端当前提供:

- `TaobaoClient.SearchAsync` - 商品搜索
- `TaobaoClient.DetailAsync` - 商品详情和规格查询
- `TaobaoClient.RequestAsync` - 调用淘宝接口并返回原始字符串
- `TaobaoClient.RefreshCookieAsync` - 刷新内存中的 Cookie

`SearchAsync` 默认返回全部结果. 需要限制显示数量时, 由调用方传入 `maxResults`
也可以传入 `minPrice` 和 `maxPrice` 限制价格范围


