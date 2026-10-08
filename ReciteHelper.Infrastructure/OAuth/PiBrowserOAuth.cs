using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ReciteHelper.Core.Interfaces.Services;

namespace ReciteHelper.Infrastructure.OAuth;

internal sealed partial class PiOAuthClient
{
    private async Task<JsonObject> BrowserLoginAsync(string provider, string deviceId, OAuthInteraction ui)
    {
        var (verifier, challenge) = OAuthHttp.CreatePkce();
        var state = provider == "anthropic" ? verifier : OAuthHttp.Base64Url(RandomNumberGenerator.GetBytes(32));
        var path = provider == "openrouter" ? "/oauth/callback/" + Guid.NewGuid().ToString("N")
            : provider == "anthropic" ? "/callback" : "/auth/callback";
        OAuthLoopbackServer? callback = null;
        try
        {
            try
            {
                callback = new OAuthLoopbackServer(provider == "openrouter" ? 0 : provider == "anthropic" ? 53692 : 1455,
                    path, provider == "openrouter" ? null : state,
                    provider is "anthropic" or "openai-codex" ? "localhost" : "127.0.0.1", provider == "openai");
            }
            catch (SocketException) when (provider == "anthropic")
            {
                try { callback = new OAuthLoopbackServer(0, path, state, "localhost"); }
                catch (SocketException) { throw new InvalidOperationException("无法启动本地授权回调，请检查端口与本机网络设置。"); }
            }
            catch (SocketException) when (provider == "openai-codex")
            { return await CodexDeviceAsync(ui).ConfigureAwait(false); }
            catch (SocketException)
            { throw new InvalidOperationException("授权回调端口已被占用，请取消其他登录后重试。"); }
            var redirect = callback!.RedirectUri;
            string authorize;
            Dictionary<string, string> fields;
            if (provider == "openrouter")
            {
                authorize = "https://openrouter.ai/auth";
                fields = OAuthHttp.Fields("callback_url", redirect, "code_challenge", challenge, "code_challenge_method", "S256");
            }
            else
            {
                var id = provider == "anthropic" ? AnthropicClient : provider == "openai" ? "dynamic_agent_client" : CodexClient;
                var scope = provider == "anthropic"
                    ? "org:create_api_key user:profile user:inference user:sessions:claude_code user:mcp_servers user:file_upload"
                    : provider == "openai" ? "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct"
                    : "openid profile email offline_access";
                authorize = provider == "anthropic" ? "https://claude.ai/oauth/authorize"
                    : provider == "openai" ? "https://auth.openai.com/api/accounts/authorize" : "https://auth.openai.com/oauth/authorize";
                fields = OAuthHttp.Fields("client_id", id, "response_type", "code", "redirect_uri", redirect,
                    "scope", scope, "code_challenge", challenge, "code_challenge_method", "S256", "state", state);
                if (provider == "anthropic") fields["code"] = "true";
                if (provider == "openai")
                {
                    if (!Guid.TryParse(deviceId, out var device)) throw new InvalidOperationException("安装 ID 无效。");
                    fields["agent_name_hint"] = "ReciteHelper";
                    fields["ext_agent_host_id"] = "urn:uuid:" + device.ToString();
                    fields["resource"] = "https://api.openai.com/v1";
                    fields["nonce"] = OAuthHttp.Base64Url(RandomNumberGenerator.GetBytes(32));
                }
                if (provider == "openai-codex")
                {
                    fields["id_token_add_organizations"] = "true";
                    fields["codex_cli_simplified_flow"] = "true";
                    fields["originator"] = "ReciteHelper";
                }
            }
            ui.Notify(Event("auth_url", url: OAuthHttp.Query(authorize, fields)));
            var result = OAuthHttp.ParseQuery((await callback.Completion.WaitAsync(ui.Token).ConfigureAwait(false)).Query);
            ui.Notify(Event("progress", "正在交换授权结果…"));
            return await ExchangeBrowserAsync(provider, result, verifier, redirect, state, ui.Token).ConfigureAwait(false);
        }
        finally { if (callback is not null) await callback.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task<JsonObject> ExchangeBrowserAsync(string provider, Dictionary<string, string> result,
        string verifier, string redirect, string state, CancellationToken token)
    {
        var code = result["code"];
        if (provider == "openrouter")
        {
            var reply = await _http.SendAsync("https://openrouter.ai/api/v1/auth/keys", HttpMethod.Post,
                new { code, code_verifier = verifier, code_challenge_method = "S256" }, false, token).ConfigureAwait(false);
            return OAuthHttp.Credential(OAuthHttp.Required(reply.RequireSuccess(), "key"), "", 9007199254740991);
        }
        if (provider == "anthropic")
        {
            var reply = await _http.SendAsync("https://platform.claude.com/v1/oauth/token", HttpMethod.Post,
                new { grant_type = "authorization_code", client_id = AnthropicClient, code, state, redirect_uri = redirect, code_verifier = verifier }, false, token).ConfigureAwait(false);
            return ParseCredential(provider, reply.RequireSuccess());
        }
        var clientId = provider == "openai" ? result["client_id"] : CodexClient;
        var fields = OAuthHttp.Fields("grant_type", "authorization_code", "client_id", clientId, "code", code,
            "code_verifier", verifier, "redirect_uri", redirect);
        if (provider == "openai") fields["resource"] = "https://api.openai.com/v1";
        var url = provider == "openai" ? "https://auth.openai.com/api/accounts/oauth/token" : "https://auth.openai.com/oauth/token";
        var response = await _http.SendAsync(url, HttpMethod.Post, fields, true, token).ConfigureAwait(false);
        var body = response.RequireSuccess();
        if (provider == "openai") _ = OAuthHttp.Required(body, "id_token");
        return ParseCredential(provider, body, clientId);
    }

    private async Task<JsonObject> RadiusLoginAsync(OAuthInteraction ui)
    {
        var discovery = (await _http.SendAsync(RadiusGateway + "/v1/oauth", HttpMethod.Get, null, false, ui.Token).ConfigureAwait(false)).RequireSuccess();
        var authorize = OAuthHttp.TrustedHttps(OAuthHttp.Required(discovery, "authorizationEndpoint"));
        var (verifier, challenge) = OAuthHttp.CreatePkce();
        var state = Guid.NewGuid().ToString();
        OAuthLoopbackServer callback;
        try { callback = new OAuthLoopbackServer(1456, "/oauth/callback", state); }
        catch (SocketException) { return await DeviceLoginAsync("radius", ui).ConfigureAwait(false); }
        await using var callbackLifetime = callback;
        ui.Notify(Event("auth_url", url: OAuthHttp.Query(authorize.AbsoluteUri,
            OAuthHttp.Fields("response_type", "code", "client_id", "pi-gateway", "redirect_uri", callback.RedirectUri,
                "scope", "gateway offline_access", "code_challenge", challenge, "code_challenge_method", "S256", "handoff", "url", "state", state))));
        var result = OAuthHttp.ParseQuery((await callback.Completion.WaitAsync(ui.Token).ConfigureAwait(false)).Query);
        var reply = await _http.SendAsync(RadiusGateway + "/v1/oauth/token", HttpMethod.Post,
            OAuthHttp.Fields("grant_type", "authorization_code", "client_id", "pi-gateway", "redirect_uri", callback.RedirectUri,
                "code", result["code"], "code_verifier", verifier), true, ui.Token).ConfigureAwait(false);
        return ParseCredential("radius", reply.RequireSuccess());
    }

    private async Task<JsonObject> CodexDeviceAsync(OAuthInteraction ui)
    {
        var device = (await _http.SendAsync("https://auth.openai.com/api/accounts/deviceauth/usercode", HttpMethod.Post,
            new { client_id = CodexClient }, false, ui.Token).ConfigureAwait(false)).RequireSuccess();
        var id = OAuthHttp.Required(device, "device_auth_id");
        var code = OAuthHttp.Required(device, "user_code");
        ui.Notify(Event("device_code", url: "https://auth.openai.com/codex/device", code: code));
        return await PollAsync(async token =>
        {
            var reply = await _http.SendAsync("https://auth.openai.com/api/accounts/deviceauth/token", HttpMethod.Post,
                new { device_auth_id = id, user_code = code }, false, token).ConfigureAwait(false);
            if (!reply.IsSuccess)
            {
                if ((int)reply.Status is 403 or 404 || reply.Error == "deviceauth_authorization_pending") return new();
                return PendingOrThrow(reply);
            }
            var tokenReply = await _http.SendAsync("https://auth.openai.com/oauth/token", HttpMethod.Post,
                OAuthHttp.Fields("grant_type", "authorization_code", "client_id", CodexClient,
                    "code", OAuthHttp.Required(reply.Body, "authorization_code"), "code_verifier", OAuthHttp.Required(reply.Body, "code_verifier"),
                    "redirect_uri", "https://auth.openai.com/deviceauth/callback"), true, token).ConfigureAwait(false);
            return new(ParseCredential("openai-codex", tokenReply.RequireSuccess()));
        }, OAuthHttp.Number(device, "interval", 5), 900, false, ui.Token).ConfigureAwait(false);
    }
}
