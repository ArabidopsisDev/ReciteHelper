using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

internal static class AiJson
{
    public static T Read<T>(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var start = text.IndexOf('\n');
            var end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (start < 0 || end <= start) throw new InvalidDataException("模型返回的 JSON 格式有误，请重试。");
            text = text[(start + 1)..end].Trim();
        }
        try
        {
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("模型返回了空结果，请重试。");
        }
        catch (JsonException ex) { throw new InvalidDataException("模型返回的 JSON 格式有误，请重试。", ex); }
    }
}
