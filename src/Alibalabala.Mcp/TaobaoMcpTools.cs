using Alibalabala.Core.Taobao;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Alibalabala.Mcp;

[McpServerToolType]
public sealed class TaobaoMcpTools(TaobaoClient client, McpConfigStore configStore)
{
    [McpServerTool, Description("搜索淘宝商品, 返回标题, 价格, 销量和商品 ID")]
    public async Task<IReadOnlyList<TaobaoProduct>> SearchProducts(
        [Description("搜索关键词")] string keyword,
        [Description("页码, 从 1 开始")] int page = 1,
        [Description("最低价格, 不填表示不限")] decimal? minPrice = null,
        [Description("最高价格, 不填表示不限")] decimal? maxPrice = null)
    {
        var products = await client.SearchAsync(
            keyword,
            page,
            minPrice: minPrice,
            maxPrice: maxPrice);
        await configStore.SaveAsync(client.Config);
        return products;
    }

    [McpServerTool, Description("查询商品详情, 返回规格, 价格和库存")]
    public async Task<TaobaoProductDetail> ProductDetail(
        [Description("商品 ID")] string itemId)
    {
        var detail = await client.DetailAsync(itemId);
        await configStore.SaveAsync(client.Config);
        return detail;
    }
}



