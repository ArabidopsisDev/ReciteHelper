using System.Xml.Serialization;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public class ModelAccessTests
{
    [Fact]
    public void LegacyConfigurationRetainsItsAccessMode()
    {
        var serializer = new XmlSerializer(typeof(ConfigOptions));
        var config = (ConfigOptions)serializer.Deserialize(new StringReader("<Config><DeepSeekKey>d</DeepSeekKey><QwenKey>q</QwenKey></Config>"))!;
        Assert.Equal(ModelAccessMode.DeepSeekAndQwen, ModelAccess.Resolve(config));
        Assert.Equal(ModelAccessMode.DeepSeekAndQwen, ModelAccess.ResolveEmbedding(config));
        Assert.Null(config.PiOAuthProvider);
    }

    [Fact]
    public void SelectedOAuthChatOverridesExistingKeysWithoutChangingEmbeddings()
    {
        var config = new ConfigOptions { PiOAuthProvider = "anthropic", PiOAuthModel = "study", DeepSeekKey = "d", QwenKey = "q", OpenRouterKey = "o" };
        Assert.Equal(ModelAccessMode.PiOAuth, ModelAccess.Resolve(config));
        Assert.Equal(ModelAccessMode.DeepSeekAndQwen, ModelAccess.ResolveEmbedding(config));
        Assert.True(ModelAccess.HasTextGeneration(config));
    }

    [Theory]
    [InlineData("q", null, null, ModelAccessMode.DeepSeekAndQwen)]
    [InlineData(null, "o", null, ModelAccessMode.OpenRouter)]
    [InlineData(null, null, "h", ModelAccessMode.Hosted)]
    [InlineData(null, null, null, ModelAccessMode.None)]
    public void OAuthUsesAnIndependentEmbeddingService(string? qwen, string? openRouter, string? hosted, ModelAccessMode expected)
    {
        var config = new ConfigOptions { PiOAuthProvider = "openai", PiOAuthModel = "study", QwenKey = qwen, OpenRouterKey = openRouter, HostedLicenseId = hosted };
        Assert.Equal(expected, ModelAccess.ResolveEmbedding(config));
        Assert.True(ModelAccess.HasTextGeneration(config));
    }

    [Fact]
    public void IncompleteOAuthSelectionFallsBackToLegacyAccess()
    {
        Assert.Equal(ModelAccessMode.OpenRouter, ModelAccess.Resolve(new ConfigOptions { PiOAuthProvider = "openai", OpenRouterKey = "o" }));
        Assert.Equal(ModelAccessMode.None, ModelAccess.Resolve(null));
    }

    [Fact]
    public async Task AiChatUsesConfiguredPiProviderModelAndInstructions()
    {
        var config = new StubConfig(new ConfigOptions { PiOAuthProvider = "openai", PiOAuthModel = "study" });
        var pi = new StubPi();
        var service = new AiChatService(new HostedModelService(config), config, pi);
        Assert.Equal("answer", await service.RunAsync("legacy-key", "prompt", "instructions"));
        Assert.Equal(("openai", "study", "prompt", "instructions"), pi.Call);
    }

    [Fact]
    public async Task OAuthWithoutEmbeddingsDoesNotAttemptHostedKnowledgeBaseCalls()
    {
        var config = new StubConfig(new ConfigOptions { PiOAuthProvider = "openai", PiOAuthModel = "study" });
        var hosted = new HostedModelService(config);
        var service = new KnowledgeBaseService(config, new AiChatService(hosted, config, new StubPi()), hosted);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Build("unused", "测试知识点"));
        Assert.Contains("独立", error.Message);
    }

    private sealed class StubConfig(ConfigOptions config) : IConfigService
    {
        public Task<ConfigOptions> LoadAsync() => Task.FromResult(config);
        public Task SaveAsync(ConfigOptions value) => Task.CompletedTask;
    }

    private sealed class StubPi : IPiModelService
    {
        public (string, string, string, string?) Call { get; private set; }
        public Task<string> RunChatAsync(string provider, string model, string prompt, string? instructions, CancellationToken cancellationToken = default)
        { Call = (provider, model, prompt, instructions); return Task.FromResult("answer"); }
        public Task<IReadOnlyList<PiOAuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PiChatModel>> GetModelsAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LoginAsync(string provider, Func<PiAuthPrompt, CancellationToken, Task<string>> prompt, Action<PiAuthEvent> notify, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(string provider, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
