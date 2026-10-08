using System.Net;
using System.Text.Json.Nodes;
using ReciteHelper.Core.Interfaces.Services;

namespace ReciteHelper.Infrastructure.OAuth;

internal sealed partial class PiOAuthClient
{
    private static string KimiHost => (Environment.GetEnvironmentVariable("KIMI_CODE_OAUTH_HOST")
        ?? Environment.GetEnvironmentVariable("KIMI_OAUTH_HOST") ?? "https://auth.kimi.com").TrimEnd('/');
    private static readonly Dictionary<string, string> CopilotHeaders = new()
    {
        ["User-Agent"] = "GitHubCopilotChat/0.35.0",
        ["Editor-Version"] = "vscode/1.107.0",
        ["Editor-Plugin-Version"] = "copilot-chat/0.35.0",
        ["Copilot-Integration-Id"] = "vscode-chat"
    };

    private async Task<JsonObject> DeviceLoginAsync(string provider, OAuthInteraction ui)
    {
        var (startUrl, tokenUrl, id) = provider switch
        {
            "kimi-coding" => (KimiHost + "/api/oauth/device_authorization", KimiHost + "/api/oauth/token", KimiClient),
            "meta" => ("https://auth.meta.com/oidc/device/authorization/", "https://auth.meta.com/oidc/device/token/", MetaClient),
            "xai" => ("https://auth.x.ai/oauth2/device/code", "https://auth.x.ai/oauth2/token", XaiClient),
            "radius" => (RadiusGateway + "/v1/oauth/device", RadiusGateway + "/v1/oauth/token", "pi-gateway"),
            _ => throw new InvalidOperationException("未知设备码服务。")
        };
        var fields = OAuthHttp.Fields("client_id", id);
        if (provider == "xai")
        {
            fields["scope"] = "openid profile email offline_access grok-cli:access api:access";
            fields["referrer"] = "pi";
        }
        if (provider == "radius") fields["scope"] = "gateway offline_access";
        var device = (await _http.SendAsync(startUrl, HttpMethod.Post, fields, true, ui.Token).ConfigureAwait(false)).RequireSuccess();
        var deviceCode = OAuthHttp.Required(device, "device_code");
        var userCode = OAuthHttp.Required(device, "user_code");
        var verification = OAuthHttp.TrustedHttps(OAuthHttp.Text(device, "verification_uri_complete") ?? OAuthHttp.Required(device, "verification_uri"));
        ui.Notify(Event("device_code", url: verification.AbsoluteUri, code: userCode));
        return await PollAsync(async token =>
        {
            var reply = await _http.SendAsync(tokenUrl, HttpMethod.Post,
                OAuthHttp.Fields("grant_type", DeviceGrant, "client_id", id, "device_code", deviceCode), true, token).ConfigureAwait(false);
            if (!reply.IsSuccess || string.IsNullOrEmpty(OAuthHttp.Text(reply.Body, "access_token"))) return PendingOrThrow(reply);
            if (provider == "meta") return new(await MintMetaKeyAsync(OAuthHttp.Required(reply.Body, "access_token"), token).ConfigureAwait(false));
            return new(ParseCredential(provider, reply.Body));
        }, OAuthHttp.Number(device, "interval", 5), OAuthHttp.Number(device, "expires_in", 900), provider != "radius", ui.Token).ConfigureAwait(false);
    }

    private async Task<JsonObject> MintMetaKeyAsync(string identity, CancellationToken token)
    {
        var reply = await _http.SendAsync("https://api.meta.ai/muse-code/key", HttpMethod.Post, new { }, false, token,
            new Dictionary<string, string> { ["Authorization"] = "Bearer " + identity, ["x-api-version"] = "1.0.0" }).ConfigureAwait(false);
        return OAuthHttp.Credential(OAuthHttp.Required(reply.RequireSuccess(), "api_key"), identity,
            clock.GetUtcNow().AddDays(1).ToUnixTimeMilliseconds());
    }

    private async Task<JsonObject> CopilotLoginAsync(OAuthInteraction ui)
    {
        var input = await ui.Prompt(new("text", "GitHub Enterprise 地址（留空使用 github.com）：", "company.ghe.com", null), ui.Token).ConfigureAwait(false);
        var domain = string.IsNullOrWhiteSpace(input) ? "github.com" : NormalizeEnterprise(input);
        var device = (await _http.SendAsync($"https://{domain}/login/device/code", HttpMethod.Post,
            OAuthHttp.Fields("client_id", CopilotClient, "scope", "read:user"), true, ui.Token, CopilotHeaders).ConfigureAwait(false)).RequireSuccess();
        var deviceCode = OAuthHttp.Required(device, "device_code");
        var verification = OAuthHttp.TrustedHttps(OAuthHttp.Text(device, "verification_uri_complete") ?? OAuthHttp.Required(device, "verification_uri"));
        ui.Notify(Event("device_code", url: verification.AbsoluteUri, code: OAuthHttp.Required(device, "user_code")));
        var github = await PollAsync(async token =>
        {
            var reply = await _http.SendAsync($"https://{domain}/login/oauth/access_token", HttpMethod.Post,
                OAuthHttp.Fields("client_id", CopilotClient, "device_code", deviceCode, "grant_type", DeviceGrant), true, token, CopilotHeaders).ConfigureAwait(false);
            if (reply.IsSuccess && OAuthHttp.Text(reply.Body, "access_token") is { Length: > 0 } access)
                return new(OAuthHttp.Credential(access, access, long.MaxValue));
            return PendingOrThrow(reply);
        }, OAuthHttp.Number(device, "interval", 5), OAuthHttp.Number(device, "expires_in", 900), true, ui.Token).ConfigureAwait(false);
        github["enterpriseUrl"] = domain == "github.com" ? null : domain;
        return await CopilotRefreshAsync(github, ui.Token, enablePolicies: true).ConfigureAwait(false);
    }

    private async Task<JsonObject> CopilotRefreshAsync(JsonObject current, CancellationToken token, bool enablePolicies = false)
    {
        var domain = OAuthHttp.Text(current, "enterpriseUrl") is { Length: > 0 } enterprise ? NormalizeEnterprise(enterprise) : "github.com";
        var refresh = OAuthHttp.Required(current, "refresh");
        var reply = (await _http.SendAsync($"https://api.{domain}/copilot_internal/v2/token", HttpMethod.Get, null, false, token,
            AuthorizedCopilot(refresh)).ConfigureAwait(false)).RequireSuccess();
        var expires = OAuthHttp.Number(reply, "expires_at");
        if (expires <= 0) throw new InvalidOperationException("Copilot 授权响应缺少有效期。");
        var credential = OAuthHttp.Credential(OAuthHttp.Required(reply, "token"), refresh, (long)(expires * 1000) - 300000);
        credential["enterpriseUrl"] = domain == "github.com" ? null : domain;
        var endpoint = CopilotBaseUrl(credential);
        var headers = AuthorizedCopilot(OAuthHttp.Required(credential, "access"));
        headers["X-GitHub-Api-Version"] = "2026-06-01";
        var catalog = (await CopilotRequestAsync(endpoint + "/models", HttpMethod.Get, null, token, headers).ConfigureAwait(false)).RequireSuccess();
        if (catalog["data"] is not JsonArray data) throw new InvalidOperationException("Copilot 返回了无效的模型目录。");
        var candidates = data.OfType<JsonObject>().Where(m => !string.IsNullOrEmpty(OAuthHttp.Text(m, "id"))
            && m["capabilities"]?["supports"]?["tool_calls"]?.GetValue<bool>() != false).ToList();
        var picker = candidates.Where(m => m["model_picker_enabled"]?.GetValue<bool>() == true
            && OAuthHttp.Text(m["policy"], "state") != "disabled").ToList();
        var fallback = picker.Count == 0 && endpoint == "https://api.individual.githubcopilot.com";
        var available = (fallback ? candidates.Where(m => OAuthHttp.Text(m["policy"], "state") == "enabled") : picker)
            .Select(m => OAuthHttp.Required(m, "id")).ToHashSet();
        if (enablePolicies)
        {
            var known = PiModelCatalog.GetModels("github-copilot").Select(m => m.Id).ToHashSet();
            foreach (var model in candidates.Where(m => OAuthHttp.Text(m["policy"], "state") == "unconfigured"
                && known.Contains(OAuthHttp.Required(m, "id")) && (fallback || m["model_picker_enabled"]?.GetValue<bool>() == true)))
            {
                var policyHeaders = AuthorizedCopilot(OAuthHttp.Required(credential, "access"));
                policyHeaders["openai-intent"] = policyHeaders["x-interaction-type"] = "chat-policy";
                try
                {
                    var policy = await CopilotRequestAsync(endpoint + "/models/" + Uri.EscapeDataString(OAuthHttp.Required(model, "id")) + "/policy",
                        HttpMethod.Post, new { state = "enabled" }, token, policyHeaders).ConfigureAwait(false);
                    if (policy.IsSuccess) available.Add(OAuthHttp.Required(model, "id"));
                    if (policy.Status == HttpStatusCode.TooManyRequests) break;
                }
                catch (HttpRequestException) { }
            }
        }
        credential["availableModelIds"] = new JsonArray(available.Order().Select(id => JsonValue.Create(id)).ToArray());
        return credential;
    }

    private async Task<OAuthReply> CopilotRequestAsync(string url, HttpMethod method, object? body,
        CancellationToken token, IReadOnlyDictionary<string, string> headers)
    {
        for (var attempt = 0; ; attempt++)
        {
            var reply = await _http.SendAsync(url, method, body, false, token, headers).ConfigureAwait(false);
            if (reply.Status != HttpStatusCode.TooManyRequests || attempt >= 2) return reply;
            await DelayAsync(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)), token).ConfigureAwait(false);
        }
    }
    private static Dictionary<string, string> AuthorizedCopilot(string access)
    {
        var headers = new Dictionary<string, string>(CopilotHeaders) { ["Authorization"] = "Bearer " + access };
        return headers;
    }
    internal static string NormalizeEnterprise(string input)
    {
        var uri = OAuthHttp.TrustedHttps(input.Contains("://") ? input.Trim() : "https://" + input.Trim());
        if (!uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("GitHub Enterprise 地址无效。");
        return uri.IdnHost;
    }
    internal static string CopilotBaseUrl(JsonObject credential)
    {
        var access = OAuthHttp.Required(credential, "access");
        var proxy = access.Split(';').FirstOrDefault(part => part.StartsWith("proxy-ep=", StringComparison.Ordinal))?[9..];
        if (!string.IsNullOrEmpty(proxy))
        {
            var host = proxy.StartsWith("proxy.", StringComparison.Ordinal) ? "api." + proxy[6..] : proxy;
            var uri = OAuthHttp.TrustedHttps("https://" + host);
            if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new InvalidOperationException("Copilot 授权包含无效的代理地址。");
            return uri.AbsoluteUri.TrimEnd('/');
        }
        return OAuthHttp.Text(credential, "enterpriseUrl") is { Length: > 0 } enterprise
            ? "https://copilot-api." + NormalizeEnterprise(enterprise) : "https://api.individual.githubcopilot.com";
    }

    internal async Task<JsonObject> RadiusCatalogAsync(JsonObject credential, CancellationToken token)
    {
        var reply = await _http.SendAsync(RadiusGateway + "/v1/config", HttpMethod.Get, null, false, token,
            new Dictionary<string, string> { ["Authorization"] = "Bearer " + OAuthHttp.Required(credential, "access") }).ConfigureAwait(false);
        var config = reply.RequireSuccess();
        _ = OAuthHttp.TrustedHttps(OAuthHttp.Required(config, "baseUrl"));
        if (config["models"] is not JsonArray) throw new InvalidOperationException("Radius 返回了无效的模型目录。");
        return config;
    }
}
