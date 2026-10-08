using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using ReciteHelper.Infrastructure.OAuth;

namespace ReciteHelper.OAuth.Tests;

[Collection("OAuth callback ports")]
public class DeviceFlowTests
{
    [Theory]
    [InlineData("kimi-coding", "auth.kimi.com", "/api/oauth/device_authorization", "/api/oauth/token")]
    [InlineData("meta", "auth.meta.com", "/oidc/device/authorization/", "/oidc/device/token/")]
    [InlineData("xai", "auth.x.ai", "/oauth2/device/code", "/oauth2/token")]
    public async Task DeviceAuthorizationUsesProviderContractAndReturnsWithoutCredentialInput(string provider, string host, string start, string tokenPath)
    {
        var clock = new TestClock();
        var events = new List<string>();
        using var client = new HttpClient(new MockHttp(async (request, _) =>
        {
            if (request.RequestUri!.Host == "api.meta.ai")
            {
                Assert.Equal("synthetic-identity", request.Headers.Authorization!.Parameter);
                Assert.Equal("1.0.0", request.Headers.GetValues("x-api-version").Single());
                return MockHttp.Json(new { api_key = "synthetic-meta-key" });
            }
            Assert.Equal(host, request.RequestUri.Host);
            var form = await MockHttp.Form(request);
            Assert.True(form.ContainsKey("client_id"));
            if (request.RequestUri.AbsolutePath == start)
            {
                if (provider == "xai") Assert.Contains("api:access", form["scope"]);
                return MockHttp.Json(new { device_code = "device", user_code = "TEST", verification_uri = "https://provider.example/device",
                    verification_uri_complete = "https://provider.example/device?code=TEST", interval = 1, expires_in = 600 });
            }
            Assert.Equal(tokenPath, request.RequestUri.AbsolutePath);
            Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", form["grant_type"]);
            Assert.Equal("device", form["device_code"]);
            return MockHttp.Json(new { access_token = provider == "meta" ? "synthetic-identity" : "synthetic-access", refresh_token = "synthetic-refresh", expires_in = 3600 });
        }));
        var credential = await new PiOAuthClient(client, clock, clock.Delay).LoginAsync(provider, Guid.NewGuid().ToString(),
            new((_, _) => throw new InvalidOperationException("Device login must not prompt for credentials"), evt =>
            {
                events.Add(evt.Type);
                if (evt.Type == "device_code") Assert.Contains("code=TEST", evt.VerificationUri);
            }, CancellationToken.None));
        Assert.Contains("device_code", events);
        Assert.NotNull(credential["access"]);
        if (provider == "meta") Assert.Equal("synthetic-identity", OAuthHttp.Text(credential, "refresh"));
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Delays.Single());
    }

    [Fact]
    public async Task SlowDownIncreasesPollingIntervalAndExpiredCodeStops()
    {
        var clock = new TestClock();
        var polls = 0;
        using var client = new HttpClient(new MockHttp((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/device/code")) return Task.FromResult(MockHttp.Json(new
            { device_code = "device", user_code = "TEST", verification_uri = "https://provider.example/device", interval = 1, expires_in = 100 }));
            polls++;
            return Task.FromResult(MockHttp.Json(new { error = polls == 1 ? "slow_down" : "expired_token" }, HttpStatusCode.BadRequest));
        }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PiOAuthClient(client, clock, clock.Delay).LoginAsync("xai", Guid.NewGuid().ToString(),
            new((_, _) => throw new NotSupportedException(), _ => { }, CancellationToken.None)));
        Assert.Contains("过期", error.Message);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(6)], clock.Delays);
        Assert.Equal(2, polls);
    }

    [Fact]
    public async Task CodexFallsBackToAutomaticDeviceFlowWhenBrowserPortIsOccupied()
    {
        var listener = new TcpListener(IPAddress.Loopback, 1455);
        listener.Start();
        try
        {
            var clock = new TestClock();
            using var client = new HttpClient(new MockHttp(async (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/usercode")) return MockHttp.Json(new { device_auth_id = "device", user_code = "TEST", interval = "1" });
                if (path.Contains("/deviceauth/token")) return MockHttp.Json(new { authorization_code = "code", code_verifier = "verifier" });
                var form = await MockHttp.Form(request);
                Assert.Equal("https://auth.openai.com/deviceauth/callback", form["redirect_uri"]);
                Assert.Equal("verifier", form["code_verifier"]);
                return MockHttp.Json(new { access_token = TestStore.Jwt(), refresh_token = "refresh", expires_in = 3600 });
            }));
            var credential = await new PiOAuthClient(client, clock, clock.Delay).LoginAsync("openai-codex", Guid.NewGuid().ToString(),
                new((_, _) => throw new InvalidOperationException("No credential prompt"), _ => { }, CancellationToken.None));
            Assert.Equal("synthetic-account", OAuthHttp.Text(credential, "accountId"));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task CopilotEnterpriseDeviceGrantSelectsAccountProxyAndFiltersModels()
    {
        var clock = new TestClock();
        var modelId = PiModelCatalog.GetModels("github-copilot")[0].Id;
        using var client = new HttpClient(new MockHttp(async (request, _) =>
        {
            var host = request.RequestUri!.Host;
            var path = request.RequestUri.AbsolutePath;
            if (path == "/login/device/code")
            {
                Assert.Equal("company.ghe.com", host);
                return MockHttp.Json(new { device_code = "device", user_code = "TEST", verification_uri = "https://company.ghe.com/device", interval = 1, expires_in = 600 });
            }
            if (path == "/login/oauth/access_token")
            {
                Assert.Equal("device", (await MockHttp.Form(request))["device_code"]);
                return MockHttp.Json(new { access_token = "github-identity" });
            }
            if (path.Contains("copilot_internal"))
            {
                Assert.Equal("api.company.ghe.com", host);
                Assert.Equal("github-identity", request.Headers.Authorization!.Parameter);
                return MockHttp.Json(new { token = "tid=test;proxy-ep=proxy.business.githubcopilot.com;exp=test", expires_at = clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds() });
            }
            Assert.Equal("api.business.githubcopilot.com", host);
            Assert.Equal("2026-06-01", request.Headers.GetValues("X-GitHub-Api-Version").Single());
            return MockHttp.Json(new { data = new[] { new { id = modelId, model_picker_enabled = true, policy = new { state = "enabled" }, capabilities = new { supports = new { tool_calls = true } } } } });
        }));
        var credential = await new PiOAuthClient(client, clock, clock.Delay).LoginAsync("github-copilot", Guid.NewGuid().ToString(),
            new((prompt, _) => { Assert.Equal("text", prompt.Type); return Task.FromResult("company.ghe.com"); }, _ => { }, CancellationToken.None));
        Assert.Equal("company.ghe.com", OAuthHttp.Text(credential, "enterpriseUrl"));
        Assert.Equal(modelId, credential["availableModelIds"]![0]!.GetValue<string>());
    }
}
