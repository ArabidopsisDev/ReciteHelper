using System.Net;
using System.Text.Json.Nodes;
using ReciteHelper.Core.Interfaces.Services;

namespace ReciteHelper.Infrastructure.OAuth;

// Protocol port of pi-ai 1.1.0 auth/oauth (MIT). No JavaScript runtime is used.
internal sealed partial class PiOAuthClient(HttpClient client, TimeProvider clock, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly OAuthHttp _http = new(client);
    private const string AnthropicClient = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string CodexClient = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string CopilotClient = "Iv1.b507a08c87ecfe98";
    private const string KimiClient = "17e5f671-d194-4dfb-9706-5516cb48c098";
    private const string MetaClient = "1031625952748946";
    private const string XaiClient = "b1a00492-073a-47ea-816f-4c329264a828";
    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";
    private const string RadiusGateway = "https://radius.pi.dev";

    internal Task<JsonObject> LoginAsync(string provider, string deviceId, OAuthInteraction ui) => provider switch
    {
        "anthropic" or "openai" or "openai-codex" or "openrouter" => BrowserLoginAsync(provider, deviceId, ui),
        "github-copilot" => CopilotLoginAsync(ui),
        "radius" => RadiusLoginAsync(ui),
        _ => DeviceLoginAsync(provider, ui)
    };

    internal async Task<JsonObject> RefreshAsync(string provider, JsonObject current, CancellationToken token)
    {
        if (provider == "openrouter") return (JsonObject)current.DeepClone();
        if (provider == "github-copilot") return await CopilotRefreshAsync(current, token).ConfigureAwait(false);
        if (provider == "meta") return await MintMetaKeyAsync(OAuthHttp.Required(current, "refresh"), token).ConfigureAwait(false);
        var refresh = OAuthHttp.Required(current, "refresh");
        if (provider == "anthropic")
        {
            var reply = await _http.SendAsync("https://platform.claude.com/v1/oauth/token", HttpMethod.Post,
                new { grant_type = "refresh_token", client_id = AnthropicClient, refresh_token = refresh }, false, token).ConfigureAwait(false);
            return OAuthHttp.Token(reply.RequireSuccess(), clock, skewSeconds: 300);
        }
        var (url, id) = provider switch
        {
            "openai" => ("https://auth.openai.com/api/accounts/oauth/token", OAuthHttp.Required(current, "clientId")),
            "openai-codex" => ("https://auth.openai.com/oauth/token", CodexClient),
            "kimi-coding" => (KimiHost + "/api/oauth/token", KimiClient),
            "xai" => ("https://auth.x.ai/oauth2/token", XaiClient),
            "radius" => (RadiusGateway + "/v1/oauth/token", "pi-gateway"),
            _ => throw new InvalidOperationException("未知 OAuth 服务。")
        };
        var fields = OAuthHttp.Fields("grant_type", "refresh_token", "client_id", id, "refresh_token", refresh);
        if (provider == "openai") fields["resource"] = "https://api.openai.com/v1";
        var response = provider == "kimi-coding"
            ? await KimiRefreshRequestAsync(url, fields, token).ConfigureAwait(false)
            : await _http.SendAsync(url, HttpMethod.Post, fields, true, token).ConfigureAwait(false);
        return ParseCredential(provider, response.RequireSuccess(), id, provider == "xai" ? refresh : null);
    }

    private JsonObject ParseCredential(string provider, JsonObject reply, string? clientId = null, string? previousRefresh = null)
    {
        if (provider == "xai" && reply["expires_in"] is null) reply["expires_in"] = 3600;
        var credential = OAuthHttp.Token(reply, clock, previousRefresh, provider switch
        { "openai" => 180, "xai" or "anthropic" => 300, "radius" => 60, _ => 0 });
        if (provider == "openai")
        {
            var scope = OAuthHttp.Required(reply, "scope");
            var scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!scopes.Contains("chatgpt.tokens.use.direct")) throw new InvalidOperationException("ChatGPT 授权缺少直接调用模型的权限。");
            credential["clientId"] = clientId;
            credential["scopes"] = new JsonArray(scopes.Select(s => JsonValue.Create(s)).ToArray());
        }
        if (provider == "openai-codex") credential["accountId"] = OAuthHttp.AccountId(OAuthHttp.Required(credential, "access"));
        if (provider == "radius") credential["scope"] = reply["scope"]?.DeepClone();
        return credential;
    }

    private async Task<OAuthReply> KimiRefreshRequestAsync(string url, Dictionary<string, string> fields, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (attempt > 0) await DelayAsync(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), token).ConfigureAwait(false);
            OAuthReply reply;
            try { reply = await _http.SendAsync(url, HttpMethod.Post, fields, true, token).ConfigureAwait(false); }
            catch (HttpRequestException) when (attempt < 3) { continue; }
            if (attempt < 3 && ((int)reply.Status >= 500 || reply.Status == HttpStatusCode.TooManyRequests)) continue;
            return reply;
        }
    }

    private sealed record PollResult(JsonObject? Credential = null, bool SlowDown = false, double? IntervalSeconds = null);
    private async Task<JsonObject> PollAsync(Func<CancellationToken, Task<PollResult>> poll,
        double intervalSeconds, double expiresSeconds, bool waitFirst, CancellationToken token)
    {
        var deadline = clock.GetUtcNow().AddSeconds(expiresSeconds > 0 ? expiresSeconds : 900);
        var interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds > 0 ? intervalSeconds : 5));
        if (waitFirst) await DelayPollAsync(interval, deadline, token).ConfigureAwait(false);
        while (clock.GetUtcNow() < deadline)
        {
            token.ThrowIfCancellationRequested();
            var result = await poll(token).ConfigureAwait(false);
            if (result.Credential is not null) return result.Credential;
            if (result.SlowDown) interval = result.IntervalSeconds is > 0
                ? TimeSpan.FromSeconds(Math.Max(1, result.IntervalSeconds.Value)) : interval.Add(TimeSpan.FromSeconds(5));
            await DelayPollAsync(interval, deadline, token).ConfigureAwait(false);
        }
        throw new InvalidOperationException("设备码授权已超时，请重新登录。");
    }
    private Task DelayPollAsync(TimeSpan interval, DateTimeOffset deadline, CancellationToken token)
    {
        var remaining = deadline - clock.GetUtcNow();
        return remaining <= TimeSpan.Zero ? Task.CompletedTask : DelayAsync(remaining < interval ? remaining : interval, token);
    }
    private Task DelayAsync(TimeSpan duration, CancellationToken token) => delay?.Invoke(duration, token) ?? Task.Delay(duration, clock, token);
    private static PollResult PendingOrThrow(OAuthReply reply)
    {
        if (reply.Error == "authorization_pending") return new();
        if (reply.Error == "slow_down") return new(SlowDown: true, IntervalSeconds: OAuthHttp.Number(reply.Body, "interval"));
        if (reply.Error is "access_denied" or "authorization_denied") throw new InvalidOperationException("账号授权被拒绝。");
        if (reply.Error == "expired_token") throw new InvalidOperationException("设备码已过期，请重新登录。");
        throw OAuthHttp.Failure(reply.Status, reply.Error);
    }
    private static PiAuthEvent Event(string type, string? message = null, string? url = null, string? code = null)
        => new(type, message, url, type == "auth_url" ? "完成网页授权后会自动返回 ReciteHelper，无需输入或粘贴凭据。" : null, code, type == "device_code" ? url : null, null);
}

internal sealed record OAuthInteraction(
    Func<PiAuthPrompt, CancellationToken, Task<string>> Prompt, Action<PiAuthEvent> Notify, CancellationToken Token);
