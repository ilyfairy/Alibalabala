namespace Alibalabala.Core.Taobao;

public sealed record TaobaoProductDetail(
    string ItemId,
    string Title,
    IReadOnlyList<TaobaoSku> Skus);



