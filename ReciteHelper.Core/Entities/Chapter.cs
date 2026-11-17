using ReciteHelper.Core.ValueObjects;
using ReciteHelper.SharedKernel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Entities;

public class Chapter : Entity, IJsonOnDeserialized
{
    [JsonConstructor]
    public Chapter() { }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("generation_prompt")]
    public string? GenerationPrompt { get; set; }

    [JsonPropertyName("additional_generation_prompts")]
    public List<string> AdditionalGenerationPrompts { get; set; } = [];

    [JsonPropertyName("review_materials")]
    public List<ReviewMaterial> ReviewMaterials { get; set; } = [];

    [JsonPropertyName("bank")]
    public List<Question>? Questions { get; set; }

    [JsonPropertyName("know")]
    public List<KnowledgePoint>? KnowledgePoints { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public void OnDeserialized()
    {
        Questions ??= ReadList<Question>("questions")
            ?? ReadList<Question>("Questions");
        KnowledgePoints ??= ReadList<KnowledgePoint>("knowledge_points")
            ?? ReadList<KnowledgePoint>("knowledgePoints")
            ?? ReadList<KnowledgePoint>("KnowledgePoints");

        ExtensionData = null;
    }

    private List<T>? ReadList<T>(string propertyName)
    {
        if (ExtensionData is null || !ExtensionData.TryGetValue(propertyName, out var element))
            return null;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        try
        {
            return element.Deserialize<List<T>>();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
