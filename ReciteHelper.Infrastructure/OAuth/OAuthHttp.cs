using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReciteHelper.Infrastructure.OAuth;

internal sealed record OAuthReply(HttpStatusCode Status, JsonObject Body)
{
    internal bool IsSuccess => (int)Status is >= 200 and < 300;
    internal string? Error => OAuthHttp.Text(Body, "error") ?? OAuthHttp.Text(Body["error"], "code");
    internal JsonObject RequireSuccess()
    {
        if (!IsSuccess) throw OAuthHttp.Failure(Status, Error);
        return Body;
    }
}

internal sealed class OAuthHttp(HttpClient client)
{
    internal async Task<OAuthReply> SendAsync(string url, HttpMethod method, object? body,
        bool form, CancellationToken token, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(method, TrustedHttps(url));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("ReciteHelper/5.0");
        if (body is not null)
            request.Content = form ? new FormUrlEncodedContent((IEnumerable<KeyValuePair<string, string>>)body)
                : JsonContent.Create(body, options: PiModelCatalog.JsonOptions);
        if (headers is not null)
            foreach (var (name, value) in headers)
            {
                request.Headers.Remove(name);
                request.Headers.TryAddWithoutValidation(name, value);
            }
        using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        JsonObject? json = null;
        try { json = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)) as JsonObject; }
        catch (JsonException) { }
        if (json is null && response.IsSuccessStatusCode)
            throw new InvalidOperationException("模型服务返回了无效的 JSON 数据。");
        return new OAuthReply(response.StatusCode, json ?? []);
    }

    internal static InvalidOperationException Failure(HttpStatusCode status, string? error = null) =>
        new(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || error == "invalid_grant"
            ? $"账号授权已失效或没有访问权限，请重新登录（HTTP {(int)status}）。"
            : $"模型服务请求失败（HTTP {(int)status}），请检查服务状态、账号权限与配额。");

    internal static string? Text(JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    internal static string Required(JsonNode node, string key) => Text(node, key) is { Length: > 0 } text ? text
        : throw new InvalidOperationException($"授权响应缺少有效的 {key} 字段。");
    internal static double Number(JsonNode? node, string key, double fallback = 0)
    {
        if (node?[key] is not JsonValue value) return fallback;
        if (value.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
        if (value.TryGetValue<long>(out var integer)) return integer;
        if (value.TryGetValue<int>(out var smallInteger)) return smallInteger;
        if (value.TryGetValue<decimal>(out var decimalNumber)) return (double)decimalNumber;
        return value.TryGetValue<string>(out var text) && double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out number) && double.IsFinite(number) ? number : fallback;
    }
    internal static Uri TrustedHttps(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? uri
        : throw new InvalidOperationException("服务返回了不可信的 HTTPS 地址。");
    internal static Dictionary<string, string> Fields(params string[] values)
    {
        var fields = new Dictionary<string, string>();
        for (var index = 0; index < values.Length; index += 2) fields.Add(values[index], values[index + 1]);
        return fields;
    }
    internal static string Query(string url, IReadOnlyDictionary<string, string> fields) => new UriBuilder(url)
    {
        Query = string.Join("&", fields.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)))
    }.Uri.AbsoluteUri;
    internal static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Split('=', 2)).GroupBy(pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')))
        .ToDictionary(group => group.Key, group => Uri.UnescapeDataString(group.First().ElementAtOrDefault(1)?.Replace('+', ' ') ?? ""));
    internal static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }
    internal static JsonObject Token(JsonObject reply, TimeProvider clock, string? previousRefresh = null, int skewSeconds = 0)
    {
        var access = Required(reply, "access_token");
        var refresh = Text(reply, "refresh_token") ?? previousRefresh;
        var seconds = Number(reply, "expires_in");
        if (string.IsNullOrEmpty(refresh) || seconds <= 0)
            throw new InvalidOperationException("授权响应缺少有效的 refresh_token 或 expires_in。");
        return Credential(access, refresh, clock.GetUtcNow().ToUnixTimeMilliseconds() + (long)((seconds - skewSeconds) * 1000));
    }
    internal static JsonObject Credential(string access, string refresh, long expires) =>
        new() { ["type"] = "oauth", ["access"] = access, ["refresh"] = refresh, ["expires"] = expires };

    internal static string AccountId(string access)
    {
        try
        {
            var payload = access.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            var json = JsonNode.Parse(Convert.FromBase64String(payload));
            return Required(json!["https://api.openai.com/auth"]!, "chatgpt_account_id");
        }
        catch (Exception ex) when (ex is JsonException or FormatException or IndexOutOfRangeException or InvalidOperationException or NullReferenceException)
        { throw new InvalidOperationException("Codex 授权没有有效的账号 ID，请重新登录。"); }
    }
}
