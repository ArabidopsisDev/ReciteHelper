using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Infrastructure.OAuth;

namespace ReciteHelper.Infrastructure.Services;

public sealed class PiModelService : IPiModelService, IDisposable
{
    private readonly PiCredentialStore _store;
    private readonly HttpClient _client;
    private readonly TimeProvider _clock;
    private readonly PiOAuthClient _oauth;
    private readonly PiChatClient _chat;
    private readonly bool _ownsClient;

    public PiModelService() : this(new PiCredentialStore(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReciteHelper")),
        new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan })
    { _ownsClient = true; }

    public PiModelService(PiCredentialStore store, HttpClient client, TimeProvider? clock = null)
    {
        _store = store;
        _client = client;
        _clock = clock ?? TimeProvider.System;
        _oauth = new PiOAuthClient(client, _clock);
        _chat = new PiChatClient(client, _clock);
    }

    public async Task<IReadOnlyList<PiOAuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        var data = await SnapshotAsync(cancellationToken).ConfigureAwait(false);
        return PiModelCatalog.Providers.Select(p => new PiOAuthProvider(p.Id, p.Name,
            data.Credentials.TryGetValue(p.Id, out var entry) && entry.TryGetProperty("access", out var access)
            && access.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(access.GetString()))).ToArray();
    }

    public async Task<IReadOnlyList<PiChatModel>> GetModelsAsync(string provider, CancellationToken cancellationToken = default)
    {
        _ = PiModelCatalog.GetProvider(provider);
        var data = await SnapshotAsync(cancellationToken).ConfigureAwait(false);
        var credential = data.Credentials.TryGetValue(provider, out var stored) ? JsonNode.Parse(stored.GetRawText()) as JsonObject : null;
        if (credential is not null && provider == "radius") credential = await ResolveCredentialAsync(provider, cancellationToken).ConfigureAwait(false);
        var models = await ModelsAsync(provider, credential, cancellationToken).ConfigureAwait(false);
        return models.Select(model => new PiChatModel(model.Id, model.Name)).ToArray();
    }

    public async Task LoginAsync(string provider, Func<PiAuthPrompt, CancellationToken, Task<string>> prompt,
        Action<PiAuthEvent> notify, CancellationToken cancellationToken = default)
    {
        _ = PiModelCatalog.GetProvider(provider);
        var data = await SnapshotAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        var credential = await _oauth.LoginAsync(provider, data.DeviceId, new(prompt, notify, timeout.Token)).ConfigureAwait(false);
        // An issued credential is persisted even if cancellation raced the token
        // response. The lock protects only this transaction, not browser waiting.
        await using (await _store.AcquireAsync(CancellationToken.None).ConfigureAwait(false))
        {
            var current = _store.Read();
            current.Credentials[provider] = JsonSerializer.SerializeToElement(credential, PiModelCatalog.JsonOptions);
            await _store.SaveAsync(current).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task LogoutAsync(string provider, CancellationToken cancellationToken = default)
    {
        _ = PiModelCatalog.GetProvider(provider);
        await using var credentialLock = await _store.AcquireAsync(cancellationToken).ConfigureAwait(false);
        var data = _store.Read();
        data.Credentials.Remove(provider);
        await _store.SaveAsync(data).ConfigureAwait(false);
    }

    public async Task<string> RunChatAsync(string provider, string model, string prompt, string? instructions,
        CancellationToken cancellationToken = default)
    {
        _ = PiModelCatalog.GetProvider(provider);
        var credential = await ResolveCredentialAsync(provider, cancellationToken).ConfigureAwait(false);
        var available = await ModelsAsync(provider, credential, cancellationToken).ConfigureAwait(false);
        var selected = available.FirstOrDefault(candidate => candidate.Id == model)
            ?? throw new InvalidOperationException("所选模型当前不可用，请重新选择模型或检查账号权限。");
        return await _chat.CompleteAsync(selected, credential, prompt, instructions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PiCredentialData> SnapshotAsync(CancellationToken token)
    {
        await using var credentialLock = await _store.AcquireAsync(token).ConfigureAwait(false);
        var data = _store.Read();
        if (!_store.Exists) await _store.SaveAsync(data).ConfigureAwait(false);
        return data;
    }

    private async Task<JsonObject> ResolveCredentialAsync(string provider, CancellationToken token)
    {
        await using var credentialLock = await _store.AcquireAsync(token).ConfigureAwait(false);
        var data = _store.Read();
        if (!data.Credentials.TryGetValue(provider, out var stored) || JsonNode.Parse(stored.GetRawText()) is not JsonObject credential)
            throw new InvalidOperationException("尚未授权所选模型账号，请先登录。");
        _ = OAuthHttp.Required(credential, "access");
        if (OAuthHttp.Number(credential, "expires") <= _clock.GetUtcNow().AddMinutes(5).ToUnixTimeMilliseconds())
        {
            // Once refresh starts the server may rotate its token. Caller
            // cancellation cannot abandon the resulting durable write.
            using var refreshTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var refreshed = await _oauth.RefreshAsync(provider, credential, refreshTimeout.Token).ConfigureAwait(false);
            foreach (var (key, value) in refreshed) credential[key] = value?.DeepClone();
            data.Credentials[provider] = JsonSerializer.SerializeToElement(credential, PiModelCatalog.JsonOptions);
            await _store.SaveAsync(data).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        return credential;
    }

    private async Task<IReadOnlyList<PiModelDefinition>> ModelsAsync(string provider, JsonObject? credential, CancellationToken token)
    {
        if (provider == "radius" && credential is not null)
        {
            JsonObject config;
            try
            {
                config = await _oauth.RadiusCatalogAsync(credential, token).ConfigureAwait(false);
                await using var credentialLock = await _store.AcquireAsync(token).ConfigureAwait(false);
                var data = _store.Read();
                if (data.Credentials.TryGetValue(provider, out var stored) && JsonNode.Parse(stored.GetRawText()) is JsonObject current
                    && OAuthHttp.Text(current, "access") == OAuthHttp.Text(credential, "access"))
                {
                    current["gatewayConfig"] = config.DeepClone();
                    data.Credentials[provider] = JsonSerializer.SerializeToElement(current, PiModelCatalog.JsonOptions);
                    await _store.SaveAsync(data).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when ((ex is HttpRequestException or InvalidOperationException) && credential["gatewayConfig"] is JsonObject)
            { config = (JsonObject)credential["gatewayConfig"]!; }
            var baseUrl = OAuthHttp.TrustedHttps(OAuthHttp.Required(config, "baseUrl")).AbsoluteUri.TrimEnd('/');
            return config["models"]!.AsArray().OfType<JsonObject>().Select(model => new PiModelDefinition(
                OAuthHttp.Required(model, "id"), OAuthHttp.Required(model, "name"), "pi-messages", provider, baseUrl,
                Math.Max(1, (int)OAuthHttp.Number(model, "maxTokens", 4096)), (int)OAuthHttp.Number(model, "contextWindow", 128000),
                model["reasoning"]?.GetValue<bool>() ?? false)).ToArray();
        }
        var models = PiModelCatalog.GetModels(provider);
        if (provider == "github-copilot" && credential?["availableModelIds"] is JsonArray ids)
        {
            var available = ids.OfType<JsonValue>().Select(id => id.GetValue<string>()).ToHashSet();
            return models.Where(model => available.Contains(model.Id)).ToArray();
        }
        return models;
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
