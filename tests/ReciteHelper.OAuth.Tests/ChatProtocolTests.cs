using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using ReciteHelper.Infrastructure.OAuth;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public class ChatProtocolTests
{
    [Fact]
    public async Task EveryEmbeddedChatModelBuildsANativeRequest()
    {
        using var http = new HttpClient();
        var client = new PiChatClient(http, new TestClock());
        var models = PiModelCatalog.Providers.SelectMany(p => PiModelCatalog.GetModels(p.Id)).ToArray();
        Assert.Equal(546, models.Length);
        foreach (var model in models)
        {
            var credential = TestStore.Credential(model.Provider == "openai-codex" ? TestStore.Jwt() : "synthetic-access");
            using var request = client.BuildRequest(model, credential, "学习材料", "教学说明");
            Assert.Equal("https", request.RequestUri!.Scheme);
            var body = await MockHttp.Body(request);
            Assert.Equal(model.Id, OAuthHttp.Required(body, "model"));
        }
    }

    [Theory]
    [InlineData("anthropic", "anthropic-messages", "/v1/messages")]
    [InlineData("kimi-coding", "anthropic-messages", "/coding/v1/messages")]
    [InlineData("meta", "openai-responses", "/v1/responses")]
    [InlineData("xai", "openai-responses", "/v1/responses")]
    [InlineData("openai", "openai-responses", "/v1/responses")]
    [InlineData("openai-codex", "openai-codex-responses", "/backend-api/codex/responses")]
    [InlineData("openrouter", "openai-completions", "/api/v1/chat/completions")]
    [InlineData("openrouter", "anthropic-messages", "/api/v1/messages")]
    [InlineData("radius", "pi-messages", "/v1/messages")]
    public async Task ProviderWirePayloadsAndAuthMatchPi(string provider, string api, string path)
    {
        using var http = new HttpClient();
        var model = PiModelCatalog.GetModels(provider).First(m => m.Api == api);
        var credential = TestStore.Credential(provider == "openai-codex" ? TestStore.Jwt() : "synthetic-access");
        using var request = new PiChatClient(http, new TestClock()).BuildRequest(model, credential, "prompt", "instructions");
        Assert.Equal(path, request.RequestUri!.AbsolutePath);
        var body = await MockHttp.Body(request);
        if (provider == "openrouter" && api == "anthropic-messages") Assert.Equal("synthetic-access", request.Headers.GetValues("x-api-key").Single());
        else Assert.Equal(OAuthHttp.Required(credential, "access"), request.Headers.Authorization!.Parameter);
        if (provider == "anthropic")
        {
            Assert.Contains("oauth-2025-04-20", request.Headers.GetValues("anthropic-beta").Single());
            Assert.Contains("Claude Code", body["system"]![0]!["text"]!.GetValue<string>());
        }
        if (provider == "openai")
        {
            Assert.False(body.ContainsKey("max_output_tokens"));
            Assert.False(body.ContainsKey("temperature"));
            Assert.False(body.ContainsKey("prompt_cache_retention"));
        }
        if (provider == "openai-codex")
        {
            Assert.Equal("synthetic-account", request.Headers.GetValues("chatgpt-account-id").Single());
            Assert.False(body["store"]!.GetValue<bool>());
            Assert.Equal("instructions", OAuthHttp.Text(body, "instructions"));
        }
        if (provider == "radius") Assert.Equal("instructions", body["context"]!["messages"]![0]!["content"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("anthropic-messages", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"中文答案\"}}", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}", "{\"type\":\"message_stop\"}")]
    [InlineData("openai-responses", "{\"type\":\"response.output_text.delta\",\"delta\":\"中文答案\"}", "{\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}", null)]
    [InlineData("openai-codex-responses", "{\"type\":\"response.output_text.delta\",\"delta\":\"中文答案\"}", "{\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}", null)]
    [InlineData("openai-completions", "{\"choices\":[{\"delta\":{\"content\":\"中文答案\"}}]}", "{\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}", "[DONE]")]
    [InlineData("pi-messages", "{\"type\":\"text_delta\",\"contentIndex\":0,\"delta\":\"中文答案\"}", "{\"type\":\"done\",\"reason\":\"stop\"}", null)]
    public async Task NativeSseDecoderHandlesAllFiveProtocols(string api, string text, string terminal, string? done)
    {
        var body = ": ping\r\nevent: message\r\ndata: " + text + "\r\n\r\ndata: " + terminal + "\r\n\r\n" + (done is null ? "" : "data: " + done + "\r\n\r\n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        Assert.Equal("中文答案", await PiChatClient.ParseStreamAsync(stream, api, CancellationToken.None));
    }

    [Theory]
    [InlineData("anthropic-messages", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}\n\ndata: {\"type\":\"message_stop\"}")]
    [InlineData("openai-responses", "{\"type\":\"response.incomplete\"}")]
    [InlineData("openai-codex-responses", "{\"type\":\"response.failed\"}")]
    [InlineData("openai-completions", "{\"choices\":[{\"finish_reason\":\"length\"}]}\n\ndata: [DONE]")]
    [InlineData("pi-messages", "{\"type\":\"done\",\"reason\":\"length\"}")]
    public async Task TruncatedOrFailedResponsesAreNotReturnedAsAnswers(string api, string packet)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("data: " + packet + "\n\n"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PiChatClient.ParseStreamAsync(stream, api, CancellationToken.None));
    }

    [Theory]
    [InlineData("anthropic-messages")]
    [InlineData("openai-completions")]
    [InlineData("openai-responses")]
    [InlineData("openai-codex-responses")]
    [InlineData("pi-messages")]
    public async Task DisconnectedStreamsFailWithoutTerminalEvent(string api)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(": no terminal event\n\n"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PiChatClient.ParseStreamAsync(stream, api, CancellationToken.None));
        Assert.Contains("中断", error.Message);
    }

    [Fact]
    public async Task LargeUnicodeMultiLineSseIsNotTruncated()
    {
        var text = string.Concat(Enumerable.Repeat("知识🎓", 20000));
        var packet = new JsonObject { ["type"] = "response.output_text.delta", ["delta"] = text }.ToJsonString();
        var source = "data: " + packet + "\n\ndata: {\"type\":\"response.completed\",\n" + "data: \"response\":{\"status\":\"completed\"}}\n\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        Assert.Equal(text, await PiChatClient.ParseStreamAsync(stream, "openai-responses", CancellationToken.None));
    }

    [Fact]
    public async Task CopilotInferenceUsesAccountProxyAndAvailabilityFilter()
    {
        using var fixture = new TestStore();
        var model = PiModelCatalog.GetModels("github-copilot").First(m => m.Api == "openai-completions");
        var credential = TestStore.Credential("tid=test;proxy-ep=proxy.business.githubcopilot.com;exp=test");
        credential["availableModelIds"] = new JsonArray(model.Id);
        await fixture.Save("github-copilot", credential);
        using var client = new HttpClient(new MockHttp((request, _) =>
        {
            Assert.Equal("api.business.githubcopilot.com", request.RequestUri!.Host);
            Assert.Equal("user", request.Headers.GetValues("X-Initiator").Single());
            return Task.FromResult(MockHttp.Json(new { choices = new[] { new { message = new { content = "answer" }, finish_reason = "stop" } } }));
        }));
        using var service = new PiModelService(fixture.Store, client);
        Assert.Single(await service.GetModelsAsync("github-copilot"));
        Assert.Equal("answer", await service.RunChatAsync("github-copilot", model.Id, "prompt", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunChatAsync("github-copilot", "not-entitled", "prompt", null));
    }

    [Fact]
    public async Task RadiusCatalogReplacesBaselineAndIsPersistedForOfflineFallback()
    {
        using var fixture = new TestStore();
        await fixture.Save("radius");
        using var client = new HttpClient(new MockHttp((request, _) => Task.FromResult(MockHttp.Json(new
        { baseUrl = "https://radius.pi.dev/v1", models = new[] { new { id = "account-study", name = "Account study", reasoning = false, maxTokens = 8192, contextWindow = 128000 } } }))));
        using var service = new PiModelService(fixture.Store, client);
        Assert.Equal("account-study", (await service.GetModelsAsync("radius")).Single().Id);
        Assert.NotNull(fixture.Read("radius")["gatewayConfig"]);
        using var offline = new HttpClient(new MockHttp((_, _) => throw new HttpRequestException("Offline")));
        using var second = new PiModelService(fixture.Store, offline);
        Assert.Equal("account-study", (await second.GetModelsAsync("radius")).Single().Id);
    }
}
