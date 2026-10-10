using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alibalabala.Core.Taobao;

/// <summary>
/// 淘宝商品客户端, 提供商品搜索, 商品详情和 SKU 查询
/// 服务端返回新 Cookie 时, 只更新传入的内存配置
/// </summary>
public sealed class TaobaoClient : IDisposable
{
    const string AppKey = "12574478";

    const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 Chrome/154.0.0.0 " +
        "Safari/537.36 Edg/154.0.0.0";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    readonly HttpClient http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    readonly SemaphoreSlim gate = new(1, 1);

    public TaobaoClient(TaobaoConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public TaobaoConfig Config { get; }

    public async Task RefreshCookieAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await RefreshTokenAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 使用原始 JSON 数据字符串调用淘宝接口
    /// Token 失效时自动刷新 Cookie 并重试一次
    /// </summary>
    public async Task<string> RequestAsync(
        string api,
        string version,
        string data,
        string host = "https://h5api.m.taobao.com",
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var raw = await SendAsync(api, version, data, host, cancellationToken);
            using (var response = ParseResponse(raw))
            {
                var ret = Text(response.RootElement, "ret");
                if (ret.Contains("TOKEN_EX", StringComparison.OrdinalIgnoreCase) ||
                    ret.Contains("TOKEN_EMPTY", StringComparison.OrdinalIgnoreCase))
                {
                    await RefreshTokenAsync(cancellationToken);
                    raw = await SendAsync(api, version, data, host, cancellationToken);
                }
            }

            using var checkedResponse = ParseResponse(raw);
            EnsureSuccess(checkedResponse.RootElement);
            return raw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<TaobaoProduct>> SearchAsync(
        string keyword,
        int page = 1,
        int? maxResults = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new ArgumentException("搜索关键词不能为空", nameof(keyword));
        }

        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        if (minPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minPrice), "价格不能小于 0");
        }

        if (maxPrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPrice), "价格不能小于 0");
        }

        if (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice)
        {
            throw new ArgumentException("最低价不能高于最高价");
        }

        var parameters = new Dictionary<string, object?>
        {
            ["isBeta"] = "false",
            ["grayHair"] = "false",
            ["from"] = "nt_history",
            ["info"] = "wifi",
            ["index"] = "4",
            ["rainbow"] = "",
            ["schemaType"] = "auction",
            ["elderHome"] = "false",
            ["isEnterSrpSearch"] = "true",
            ["newSearch"] = "false",
            ["network"] = "wifi",
            ["subtype"] = "",
            ["hasPreposeFilter"] = "false",
            ["prepositionVersion"] = "v2",
            ["client_os"] = "Android",
            ["gpsEnabled"] = "false",
            ["searchDoorFrom"] = "srp",
            ["debug_rerankNewOpenCard"] = "false",
            ["homePageVersion"] = "v7",
            ["searchElderHomeOpen"] = "false",
            ["search_action"] = "initiative",
            ["sugg"] = "_4_1",
            ["sversion"] = "13.6",
            ["style"] = "list",
            ["ttid"] = "600000@taobao_pc_10.7.0",
            ["needTabs"] = "true",
            ["areaCode"] = "CN",
            ["vm"] = "nw",
            ["countryNum"] = "156",
            ["m"] = "pc",
            ["page"] = page,
            ["q"] = Uri.EscapeDataString(keyword),
            ["qSource"] = "url",
            ["channelSrp"] = "",
            ["tab"] = "all",
            ["sourceS"] = "0",
            ["sort"] = "_coefp",
            ["bcoffset"] = "",
            ["ntoffset"] = "",
            ["filterTag"] = "",
            ["service"] = "",
            ["prop"] = "",
            ["loc"] = "",
            ["start_price"] = FormatPrice(minPrice),
            ["end_price"] = FormatPrice(maxPrice),
            ["startPrice"] = FormatPrice(minPrice),
            ["endPrice"] = FormatPrice(maxPrice),
            ["itemIds"] = null,
            ["p4pIds"] = null,
            ["p4pS"] = null,
            ["categoryp"] = "",
            ["ha3Kvpairs"] = null,
            ["couponFilter"] = 0,
            ["myCNA"] = "",
            ["userAgent"] = UserAgent,
            ["couponUnikey"] = "",
            ["subTabId"] = "",
            ["np"] = "",
            ["clientType"] = "h5",
            ["isNewDomainAb"] = "false",
            ["forceOldDomain"] = "false"
        };

        var innerParameters = JsonSerializer.Serialize(parameters, JsonOptions);
        var data = JsonSerializer.Serialize(new
        {
            appId = "34385",
            @params = innerParameters
        }, JsonOptions);

        var raw = await RequestAsync(
            "mtop.relationrecommend.wirelessrecommend.recommend",
            "2.0",
            data,
            cancellationToken: cancellationToken);

        using var response = ParseResponse(raw);
        var items = Child(Child(response.RootElement, "data"), "itemsArray");
        if (items.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("搜索响应缺少 itemsArray");
        }

        var products = new List<TaobaoProduct>();
        foreach (var item in items.EnumerateArray())
        {
            var itemId = Text(item, "item_id");
            if (itemId.Length == 0)
            {
                continue;
            }

            var price = Text(Child(item, "priceShow"), "price");
            if (price.Length == 0)
            {
                price = Text(item, "price");
            }

            if (!MatchesPriceRange(price, minPrice, maxPrice))
            {
                continue;
            }

            products.Add(new TaobaoProduct(
                itemId,
                CleanText(Text(item, "title")),
                price,
                Text(item, "realSales")));

            if (maxResults is not null && products.Count >= maxResults.Value)
            {
                break;
            }
        }

        return products;
    }

    static string? FormatPrice(decimal? value)
    {
        return value?.ToString("0.##", CultureInfo.InvariantCulture);
    }

    static bool MatchesPriceRange(string price, decimal? minPrice, decimal? maxPrice)
    {
        if (!minPrice.HasValue && !maxPrice.HasValue)
        {
            return true;
        }

        var match = Regex.Match(price, @"\d+(?:\.\d+)?");
        if (!match.Success ||
            !decimal.TryParse(
                match.Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return true;
        }

        return (!minPrice.HasValue || value >= minPrice.Value) &&
               (!maxPrice.HasValue || value <= maxPrice.Value);
    }

    public async Task<TaobaoProductDetail> DetailAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(itemId, "^\\d+$"))
        {
            throw new ArgumentException("itemId 必须为数字", nameof(itemId));
        }

        var basic = await RequestAsync(
            "mtop.taobao.detail.getdetail",
            "6.0",
            JsonSerializer.Serialize(new { itemNumId = itemId }),
            cancellationToken: cancellationToken);

        using var basicResponse = ParseResponse(basic);
        var title = CleanText(Text(
            Child(Child(basicResponse.RootElement, "data"), "item"),
            "title"));

        var full = await RequestAsync(
            "mtop.taobao.pcdetail.data.get",
            "1.0",
            JsonSerializer.Serialize(new { id = itemId }),
            cancellationToken: cancellationToken);

        using var response = ParseResponse(full);
        var data = Child(response.RootElement, "data");
        var skuBase = Child(data, "skuBase");
        var skuInfo = Child(Child(data, "skuCore"), "sku2info");
        var propertyNames = new Dictionary<string, string>();
        var propertyValues = new Dictionary<string, string>();

        ReadSkuProperties(skuBase, propertyNames, propertyValues);

        var skus = new List<TaobaoSku>();
        var skuEntries = Child(skuBase, "skus");
        if (skuEntries.ValueKind == JsonValueKind.Array)
        {
            foreach (var sku in skuEntries.EnumerateArray())
            {
                var skuId = Text(sku, "skuId");
                var attributes = FormatAttributes(
                    Text(sku, "propPath"),
                    propertyNames,
                    propertyValues);
                skus.Add(ParseSku(skuId, attributes, Child(skuInfo, skuId)));
            }
        }
        else if (skuInfo.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in skuInfo.EnumerateObject())
            {
                skus.Add(ParseSku(entry.Name, "默认规格", entry.Value));
            }
        }

        return new TaobaoProductDetail(itemId, title, skus);
    }

    public static string Sign(string token, string timestamp, string appKey, string data)
    {
        var input = $"{token}&{timestamp}&{appKey}&{data}";
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    }

    public void Dispose()
    {
        http.Dispose();
        gate.Dispose();
    }

    async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        var cookies = ParseCookies(Config.Cookie);
        cookies.Remove("_m_h5_tk");
        cookies.Remove("_m_h5_tk_enc");

        const string api = "mtop.taobao.baichuan.smb.get";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var sign = Sign(string.Empty, timestamp, AppKey, "{}");
        var url = $"https://h5api.m.taobao.com/h5/{api}/1.0/" +
                  $"?jsv=2.7.4&appKey={AppKey}&t={timestamp}&sign={sign}" +
                  $"&api={api}&v=1.0&data=%7B%7D";

        using var request = CreateRequest(url, FormatCookies(cookies));
        using var response = await http.SendAsync(request, cancellationToken);
        var received = ReadCookies(response);
        if (!received.ContainsKey("_m_h5_tk") ||
            !received.ContainsKey("_m_h5_tk_enc"))
        {
            throw new InvalidOperationException("淘宝未返回新 Token; 登录会话可能已失效");
        }

        foreach (var pair in received)
        {
            cookies[pair.Key] = pair.Value;
        }

        Config.Cookie = FormatCookies(cookies);
    }

    async Task<string> SendAsync(
        string api,
        string version,
        string data,
        string host,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var cookies = ParseCookies(Config.Cookie);
        var token = cookies.GetValueOrDefault("_m_h5_tk", string.Empty)
            .Split('_')[0];
        var sign = Sign(token, timestamp, AppKey, data);
        var responseFormat = api.Equals(
            "mtop.relationrecommend.wirelessrecommend.recommend",
            StringComparison.OrdinalIgnoreCase)
            ? "&type=jsonp&dataType=jsonp&callback=mtopjsonp22"
            : "&type=originaljson";
        var url = $"{host.TrimEnd('/')}/h5/{Uri.EscapeDataString(api)}/{Uri.EscapeDataString(version)}/" +
                  $"?jsv=2.7.4&appKey={AppKey}&t={timestamp}&sign={sign}" +
                  $"&api={Uri.EscapeDataString(api)}&v={Uri.EscapeDataString(version)}" +
                  $"{responseFormat}&data={Uri.EscapeDataString(data)}";

        using var request = CreateRequest(url, Config.Cookie);
        AddOptionalHeader(request, "bx-ua", Config.BxUa);
        AddOptionalHeader(request, "bx-umidtoken", Config.BxUmidToken);
        AddOptionalHeader(request, "bx_et", Config.BxEt);

        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var received = ReadCookies(response);
        foreach (var pair in received)
        {
            cookies[pair.Key] = pair.Value;
        }

        if (received.Count > 0)
        {
            Config.Cookie = FormatCookies(cookies);
        }

        if (!response.IsSuccessStatusCode)
        {
            var preview = raw[..Math.Min(raw.Length, 500)];
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {preview}");
        }

        return raw;
    }

    static void ReadSkuProperties(
        JsonElement skuBase,
        IDictionary<string, string> propertyNames,
        IDictionary<string, string> propertyValues)
    {
        var properties = Child(skuBase, "props");
        if (properties.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var property in properties.EnumerateArray())
        {
            var propertyId = Text(property, "pid");
            propertyNames[propertyId] = Text(property, "name");
            var values = Child(property, "values");
            if (values.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var value in values.EnumerateArray())
            {
                var valueId = Text(value, "vid");
                propertyValues[$"{propertyId}:{valueId}"] = Text(value, "name");
            }
        }
    }

    static string FormatAttributes(
        string propPath,
        IReadOnlyDictionary<string, string> propertyNames,
        IReadOnlyDictionary<string, string> propertyValues)
    {
        var labels = propPath
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var propertyId = pair.Split(':')[0];
                return propertyValues.TryGetValue(pair, out var value)
                    ? $"{propertyNames.GetValueOrDefault(propertyId, propertyId)}={value}"
                    : pair;
            });

        return string.Join("; ", labels);
    }

    static TaobaoSku ParseSku(string skuId, string attributes, JsonElement info)
    {
        var price = Text(Child(info, "price"), "priceText");
        var quantity = Text(info, "quantity");
        return new TaobaoSku(skuId, attributes, price, quantity);
    }

    static HttpRequestMessage CreateRequest(string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.Referrer = new Uri("https://s.taobao.com/");
        return request;
    }

    static void AddOptionalHeader(
        HttpRequestMessage request,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    static JsonElement Child(JsonElement value, string name)
    {
        return value.ValueKind == JsonValueKind.Object &&
               value.TryGetProperty(name, out var child)
            ? child
            : default;
    }

    static string Text(JsonElement value, string name)
    {
        var child = Child(value, name);
        return child.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? string.Empty
            : child.ToString();
    }

    static string CleanText(string value)
    {
        var decoded = WebUtility.HtmlDecode(value);
        return WebUtility.HtmlDecode(Regex.Replace(decoded, "<[^>]+>", string.Empty))
            .Trim();
    }

    static JsonDocument ParseResponse(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("mtopjsonp", StringComparison.OrdinalIgnoreCase))
        {
            var start = text.IndexOf('(');
            var end = text.LastIndexOf(')');
            if (start < 0 || end <= start)
            {
                throw new InvalidOperationException("JSONP 响应不完整");
            }

            text = text[(start + 1)..end];
        }

        return JsonDocument.Parse(text);
    }

    static void EnsureSuccess(JsonElement root)
    {
        var ret = Child(root, "ret");
        if (ret.ValueKind != JsonValueKind.Array || ret.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("淘宝接口响应缺少 ret");
        }

        if (!ret[0].ToString().StartsWith("SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(ret[0].ToString());
        }
    }

    static Dictionary<string, string> ParseCookies(string cookie)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in cookie.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length == 2)
            {
                result[pair[0]] = pair[1];
            }
        }

        return result;
    }

    static Dictionary<string, string> ReadCookies(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return result;
        }

        foreach (var header in headers)
        {
            var pair = header.Split(';')[0].Split('=', 2);
            if (pair.Length == 2)
            {
                result[pair[0].Trim()] = pair[1];
            }
        }

        return result;
    }

    static string FormatCookies(IReadOnlyDictionary<string, string> values)
    {
        return string.Join("; ", values.Select(pair => $"{pair.Key}={pair.Value}"));
    }
}



