using System.Net;
using System.Net.Http;
using ReciteHelper.Infrastructure.OAuth;

namespace ReciteHelper.OAuth.Tests;

public class RefreshProtocolTests
{
    [Theory]
    [InlineData("anthropic", "platform.claude.com", "/v1/oauth/token")]
    [InlineData("openai", "auth.openai.com", "/api/accounts/oauth/token")]
    [InlineData("openai-codex", "auth.openai.com", "/oauth/token")]
    [InlineData("kimi-coding", "auth.kimi.com", "/api/oauth/token")]
    [InlineData("meta", "api.meta.ai", "/muse-code/key")]
    [InlineData("xai", "auth.x.ai", "/oauth2/token")]
    [InlineData("radius", "radius.pi.dev", "/v1/oauth/token")]
    [InlineData("github-copilot", "api.github.com", "/copilot_internal/v2/token")]
    public async Task EveryExpiringProviderUsesItsNativeRefreshContract(string provider, string host, string path)
    {
        var clock = new TestClock();
        var current = TestStore.Credential();
        current["clientId"] = "issued-client-id";
        using var client = new HttpClient(new MockHttp(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/models") return MockHttp.Json(new { data = Array.Empty<object>() });
            Assert.Equal(host, request.RequestUri.Host);
            Assert.Equal(path, request.RequestUri.AbsolutePath);
            if (provider is "meta" or "github-copilot")
            {
                Assert.Equal("synthetic-refresh-token", request.Headers.Authorization!.Parameter);
                return provider == "meta" ? MockHttp.Json(new { api_key = "refreshed-api-key" })
                    : MockHttp.Json(new { token = "tid=test;proxy-ep=proxy.individual.githubcopilot.com;exp=test", expires_at = clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds() });
            }
            if (provider == "anthropic")
            {
                var body = await MockHttp.Body(request);
                Assert.Equal("refresh_token", OAuthHttp.Required(body, "grant_type"));
                Assert.Equal("synthetic-refresh-token", OAuthHttp.Required(body, "refresh_token"));
            }
            else
            {
                var fields = await MockHttp.Form(request);
                Assert.Equal("refresh_token", fields["grant_type"]);
                Assert.Equal("synthetic-refresh-token", fields["refresh_token"]);
                if (provider == "openai")
                {
                    Assert.Equal("issued-client-id", fields["client_id"]);
                    Assert.Equal("https://api.openai.com/v1", fields["resource"]);
                }
            }
            return MockHttp.Json(new { access_token = provider == "openai-codex" ? TestStore.Jwt() : "refreshed-access", refresh_token = "refreshed-refresh",
                expires_in = 3600, scope = "openid chatgpt.tokens.use.direct" });
        }));
        var result = await new PiOAuthClient(client, clock, clock.Delay).RefreshAsync(provider, current, CancellationToken.None);
        Assert.NotNull(result["access"]);
        Assert.True(OAuthHttp.Number(result, "expires") > clock.GetUtcNow().ToUnixTimeMilliseconds());
        if (provider == "openai") Assert.Equal("issued-client-id", OAuthHttp.Text(result, "clientId"));
        if (provider == "openai-codex") Assert.Equal("synthetic-account", OAuthHttp.Text(result, "accountId"));
    }

    [Fact]
    public async Task OpenRouterPermanentKeyDoesNotMakeRefreshRequests()
    {
        using var client = new HttpClient(new MockHttp((_, _) => throw new NotSupportedException()));
        var current = OAuthHttp.Credential("permanent-key", "", 9007199254740991);
        var result = await new PiOAuthClient(client, new TestClock()).RefreshAsync("openrouter", current, CancellationToken.None);
        Assert.Equal("permanent-key", OAuthHttp.Text(result, "access"));
    }
    [Fact]
    public async Task XaiPreservesRefreshTokenWhenServerDoesNotRotateIt()
    {
        using var client = new HttpClient(new MockHttp((_, _) => Task.FromResult(MockHttp.Json(new { access_token = "new-access" }))));
        var result = await new PiOAuthClient(client, new TestClock()).RefreshAsync("xai", TestStore.Credential(), CancellationToken.None);
        Assert.Equal("synthetic-refresh-token", OAuthHttp.Text(result, "refresh"));
    }
    [Fact]
    public async Task KimiRetriesTransientRefreshFailures()
    {
        var attempts = 0;
        var clock = new TestClock();
        using var client = new HttpClient(new MockHttp((_, _) => Task.FromResult(++attempts == 1
            ? MockHttp.Json(new { error = "temporary" }, HttpStatusCode.ServiceUnavailable)
            : MockHttp.Json(new { access_token = "new-access", refresh_token = "new-refresh", expires_in = 3600 }))));
        await new PiOAuthClient(client, clock, clock.Delay).RefreshAsync("kimi-coding", TestStore.Credential(), CancellationToken.None);
        Assert.Equal(2, attempts);
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Delays.Single());
    }
    [Fact]
    public async Task OpenAiRejectsRefreshGrantWithoutDirectInferenceScope()
    {
        using var client = new HttpClient(new MockHttp((_, _) => Task.FromResult(MockHttp.Json(new
        { access_token = "new-access", refresh_token = "new-refresh", expires_in = 3600, scope = "openid" }))));
        var current = TestStore.Credential(); current["clientId"] = "issued";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PiOAuthClient(client, new TestClock()).RefreshAsync("openai", current, CancellationToken.None));
        Assert.Contains("权限", error.Message);
    }
}
