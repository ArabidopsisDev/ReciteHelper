using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ReciteHelper.Core.Entities;

public sealed record ReviewMaterial(string Name, string Content)
{
    [JsonIgnore]
    public string SourceId { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Name + "\0" + Content)));
}
