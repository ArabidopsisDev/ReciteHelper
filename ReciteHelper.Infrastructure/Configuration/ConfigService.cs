using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.Exceptions;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace ReciteHelper.Infrastructure.Configuration;

public class ConfigService : IConfigService
{
    private readonly string _configPath;

    public ConfigService(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config.xml");
    }

    public async Task<ConfigOptions> LoadAsync()
    {
        try
        {
            var config = await ReadRawAsync();
            config.DeepSeekKey = ResolveKey(config.DeepSeekKey, "DEEPSEEK_API_KEY");
            config.QwenKey = ResolveKey(config.QwenKey, "QWEN_API_KEY");
            config.OpenRouterKey = ResolveKey(config.OpenRouterKey, "OPENROUTER_API_KEY");
            return config;
        }
        catch (Exception ex)
        {

            throw new ConfigurationException($"Failed to load configuration: {ex.Message}.");
        }
    }

    public async Task SaveAsync(ConfigOptions config)
    {
        var serializer = new XmlSerializer(typeof(ConfigOptions));
        using var cloneStream = new MemoryStream();
        serializer.Serialize(cloneStream, config);
        cloneStream.Position = 0;
        var saved = (ConfigOptions)serializer.Deserialize(cloneStream)!;
        var original = await ReadRawAsync();
        saved.DeepSeekKey = PreserveSource(saved.DeepSeekKey, original.DeepSeekKey, "DEEPSEEK_API_KEY");
        saved.QwenKey = PreserveSource(saved.QwenKey, original.QwenKey, "QWEN_API_KEY");
        saved.OpenRouterKey = PreserveSource(saved.OpenRouterKey, original.OpenRouterKey, "OPENROUTER_API_KEY");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_configPath))!);
        var temporary = _configPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary)) serializer.Serialize(stream, saved);
            File.Move(temporary, _configPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<ConfigOptions> ReadRawAsync()
    {
        if (!File.Exists(_configPath)) return new ConfigOptions();
        await using var stream = File.OpenRead(_configPath);
        return (ConfigOptions?)new XmlSerializer(typeof(ConfigOptions)).Deserialize(stream) ?? new ConfigOptions();
    }

    private static string? ResolveKey(string? value, string variable) =>
        string.IsNullOrWhiteSpace(value) ? Environment.GetEnvironmentVariable(variable) : ResolveConfigText(value);

    private static string? PreserveSource(string? value, string? original, string variable) =>
        value == ResolveKey(original, variable) ? original : value;

    private static string? ResolveConfigText(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('%'))
            return text;

        var match = Regex.Match(
            text,
            "^%\\s*Environment\\.GetEnvironmentVariable\\(\"(?<name>[A-Za-z_][A-Za-z0-9_]*)\"\\)\\s*%$");

        if (!match.Success)
            return text;

        return Environment.GetEnvironmentVariable(match.Groups["name"].Value);
    }
}
