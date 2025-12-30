using ReciteHelper.Utils;
using System.IO;
using System.Xml.Serialization;

namespace ReciteHelper.Model;

public class Config
{
    public string Version { get; set; } = "v2";
    public string? DeepSeekKey { get; set; }
    public string? OCRAccess { get; set; }
    public string? OCRSecret { get; set; }

    public MissingStrategy Strategy { get; set; }

    // Deadlock? Who JB cares?
    public static Config? Configure { get; set; } = Create().GetAwaiter().GetResult();

    private static async Task<Config?> Create()
    {
        var serializer = new XmlSerializer(typeof(Config));
        using var reader = new StreamReader("Config.xml");

        var originConfig = (Config?)serializer.Deserialize(reader);
        originConfig!.DeepSeekKey = await Parser.ParseConfigText(originConfig.DeepSeekKey);
        return originConfig;
    }

    public enum MissingStrategy
    {
        Ignore,
        Replay
    }
}
