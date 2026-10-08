using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ReciteHelper.Infrastructure.OAuth;

namespace ReciteHelper.OAuth.Tests;

[CollectionDefinition("OAuth callback ports", DisableParallelization = true)]
public class CallbackPortCollection { }

[Collection("OAuth callback ports")]
public class BrowserFlowTests
{
    [Theory]
    [InlineData("anthropic")]
    [InlineData("openai")]
    [InlineData("openai-codex")]
    [InlineData("openrouter")]
    [InlineData("radius")]
    public async Task BrowserAuthorizationReturnsAutomaticallyWithoutCredentialPrompts(string provider)
    {
        var clock = new TestClock();
        Dictionary<string, string>? authorization = null;
        Task<HttpResponseMessage>? callbackResponse = null;
        string? redirect = null;
        using var local = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var client = new HttpClient(new MockHttp(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/oauth") return MockHttp.Json(new { authorizationEndpoint = "https://radius.example/authorize" });
            string verifier;
            if (request.Content!.Headers.ContentType!.MediaType == "application/json")
            {
                var body = await MockHttp.Body(request);
                verifier = OAuthHttp.Required(body, "code_verifier");
                Assert.Equal("synthetic-code", OAuthHttp.Required(body, "code"));
                if (provider == "anthropic")
                {
                    Assert.Equal(authorization!["state"], OAuthHttp.Required(body, "state"));
                    Assert.Equal(redirect, OAuthHttp.Required(body, "redirect_uri"));
                }
            }
            else
            {
                var form = await MockHttp.Form(request);
                verifier = form["code_verifier"];
                Assert.Equal("authorization_code", form["grant_type"]);
                Assert.Equal("synthetic-code", form["code"]);
                Assert.Equal(redirect, form["redirect_uri"]);
                if (provider == "openai")
                {
                    Assert.Equal("issued-client-id", form["client_id"]);
                    Assert.Equal("https://api.openai.com/v1", form["resource"]);
                }
            }
            Assert.Equal(authorization!["code_challenge"], OAuthHttp.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
            if (provider == "openrouter") return MockHttp.Json(new { key = "synthetic-router-key" });
            return MockHttp.Json(new
            {
                access_token = provider == "openai-codex" ? TestStore.Jwt() : "synthetic-access-token",
                refresh_token = "synthetic-refresh-token", expires_in = 3600,
                scope = "openid chatgpt.tokens.use.direct", id_token = "synthetic-id-token"
            });
        }));
        var oauth = new PiOAuthClient(client, clock, clock.Delay);
        var ui = new OAuthInteraction((_, _) => throw new InvalidOperationException("Browser login must not prompt for credentials"), evt =>
        {
            if (evt.Type != "auth_url") return;
            authorization = OAuthHttp.ParseQuery(new Uri(evt.Url!).Query);
            redirect = authorization.GetValueOrDefault("redirect_uri") ?? authorization["callback_url"];
            var fields = OAuthHttp.Fields("code", "synthetic-code");
            if (authorization.TryGetValue("state", out var state)) fields["state"] = state;
            if (provider == "openai")
            {
                Assert.Equal("dynamic_agent_client", authorization["client_id"]);
                Assert.StartsWith("urn:uuid:", authorization["ext_agent_host_id"]);
                fields["client_id"] = "issued-client-id";
            }
            callbackResponse = local.GetAsync(OAuthHttp.Query(redirect, fields));
        }, CancellationToken.None);
        var credential = await oauth.LoginAsync(provider, Guid.NewGuid().ToString(), ui).WaitAsync(TimeSpan.FromSeconds(8));
        using var response = await callbackResponse!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("automatically", await response.Content.ReadAsStringAsync());
        Assert.NotNull(credential["access"]);
        if (provider == "openai") Assert.Equal("issued-client-id", OAuthHttp.Text(credential, "clientId"));
        if (provider == "openai-codex") Assert.Equal("synthetic-account", OAuthHttp.Text(credential, "accountId"));
        var listener = new TcpListener(IPAddress.Loopback, new Uri(redirect!).Port);
        listener.Start();
        listener.Stop();
    }

    [Fact]
    public async Task CallbackRejectsWrongStateMissingIssuedClientIdAndDuplicateRequests()
    {
        await using var server = new OAuthLoopbackServer(0, "/auth/callback", "expected-state", issuedClientId: true);
        using var local = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var wrong = await local.GetAsync(server.RedirectUri + "?code=c&state=wrong&client_id=issued");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.False(server.Completion.IsCompleted);
        using var missing = await local.GetAsync(server.RedirectUri + "?code=c&state=expected-state");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.False(server.Completion.IsCompleted);
        using var valid = await local.GetAsync(server.RedirectUri + "?code=c&state=expected-state&client_id=issued");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var duplicate = await local.GetAsync(server.RedirectUri + "?code=c&state=expected-state&client_id=issued");
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task CancellingBrowserLoginReleasesCallbackPort()
    {
        var clock = new TestClock();
        using var cancel = new CancellationTokenSource();
        string? redirect = null;
        using var client = new HttpClient(new MockHttp((_, _) => throw new InvalidOperationException("Cancelled login must not exchange tokens")));
        var oauth = new PiOAuthClient(client, clock);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oauth.LoginAsync("openrouter", Guid.NewGuid().ToString(),
            new((_, _) => throw new NotSupportedException(), evt =>
            {
                if (evt.Type != "auth_url") return;
                redirect = OAuthHttp.ParseQuery(new Uri(evt.Url!).Query)["callback_url"];
                cancel.Cancel();
            }, cancel.Token)));
        var listener = new TcpListener(IPAddress.Loopback, new Uri(redirect!).Port);
        listener.Start();
        listener.Stop();
    }

    [Fact]
    public async Task OpenAiPortConflictDoesNotRouteAuthorizationToAnotherApp()
    {
        var listener = new TcpListener(IPAddress.Loopback, 1455);
        listener.Start();
        try
        {
            var events = new List<string>();
            using var client = new HttpClient();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PiOAuthClient(client, new TestClock()).LoginAsync("openai", Guid.NewGuid().ToString(),
                new((_, _) => throw new NotSupportedException(), evt => events.Add(evt.Type), CancellationToken.None)));
            Assert.Contains("端口", error.Message);
            Assert.Empty(events);
        }
        finally { listener.Stop(); }
    }
}
