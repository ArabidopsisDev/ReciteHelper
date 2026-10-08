using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReciteHelper.Infrastructure.OAuth;

// Provider metadata and catalog derived from pi-ai 1.1.0 (MIT); see PI-LICENSE.txt.
internal sealed record PiProvider(string Id, string Name);
internal sealed record PiModelDefinition(
    string Id, string Name, string Api, string Provider, string BaseUrl,
    int MaxTokens, int ContextWindow, bool Reasoning,
    JsonObject? Compat = null, Dictionary<string, string>? Headers = null,
    JsonObject? ThinkingLevelMap = null, JsonObject? SamplingParams = null,
    JsonObject? SamplingParamsByThinkingLevel = null);

internal static class PiModelCatalog
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static readonly PiProvider[] Providers =
    [
        new("anthropic", "Anthropic (Claude Pro/Max)"),
        new("github-copilot", "GitHub Copilot"),
        new("kimi-coding", "Kimi Code (subscription)"),
        new("meta", "Meta (Muse subscription)"),
        new("openai", "OpenAI (ChatGPT subscription)"),
        new("openai-codex", "OpenAI (ChatGPT Plus/Pro)"),
        new("openrouter", "OpenRouter OAuth"),
        new("radius", "Radius"),
        new("xai", "xAI (Grok/X subscription)")
    ];
    private static readonly Dictionary<string, PiModelDefinition[]> Models = Load();

    internal static PiProvider GetProvider(string id) => Providers.FirstOrDefault(p => p.Id == id)
        ?? throw new InvalidOperationException("所选服务不支持模型账号 OAuth。");
    internal static IReadOnlyList<PiModelDefinition> GetModels(string id) => Models[id];

    private static Dictionary<string, PiModelDefinition[]> Load()
    {
        using var stream = typeof(PiModelCatalog).Assembly.GetManifestResourceStream("ReciteHelper.pi-models.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, PiModelDefinition[]>>(stream, JsonOptions)!;
    }
}
