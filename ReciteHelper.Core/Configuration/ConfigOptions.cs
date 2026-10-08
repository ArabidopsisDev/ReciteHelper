using ReciteHelper.Core.Enums;
using System.Xml.Serialization;

namespace ReciteHelper.Core.Configuration;

[XmlRoot("Config")]
public class ConfigOptions
{
    public const string SectionName = "ReciteHelper";

    public string Version { get; set; } = "v3";
    public string? DeepSeekKey { get; set; }
    public string? QwenKey { get; set; }
    public string? OpenRouterKey { get; set; }
    public string OpenRouterChatModel { get; set; } = "deepseek/deepseek-v3.2";
    public string OpenRouterEmbeddingModel { get; set; } = "baai/bge-m3";
    public string? PiOAuthProvider { get; set; }
    public string? PiOAuthModel { get; set; }
    public string? PiOAuthGitHubEnterpriseDomain { get; set; }
    public string ResourceCenterServerUrl { get; set; } = "http://localhost:5000";
    public string? HostedServiceUrl { get; set; }
    public string? HostedLicenseCode { get; set; }
    public string? HostedLicenseId { get; set; }
    public string? OCRAccess { get; set; }
    public string? OCRSecret { get; set; }
    public int RStandard { get; set; } = 60;
    public PhonkOptions PhonkOptions { get; set; } = new();
    [XmlElement("MissingStrategy")]
    public MissingStrategy Strategy { get; set; } = MissingStrategy.Ignore;
}
