using System;
using System.IO;
using System.Runtime.ConstrainedExecution;
using System.Xml.Serialization;

namespace ReciteHelper.Model;

public class Config
{
    public string Version { get; set; } = "v2";
    public string? DeepSeekKey { get; set; }
    public string? OCRAccess { get; set; }
    public string? OCRSecret { get; set; }

    public static Config? Configure { get; set; } = Create();

    private static Config? Create()
    {
        var serializer = new XmlSerializer(typeof(Config));
        using var reader = new StreamReader("Config.xml");

        return (Config?)serializer.Deserialize(reader);
    }
}
