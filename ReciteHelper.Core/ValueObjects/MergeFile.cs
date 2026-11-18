using ReciteHelper.Core.Enums;
using ReciteHelper.SharedKernel;
using System.Text.Json.Serialization;

namespace ReciteHelper.Core.ValueObjects;

public class MergeFile : ValueObject
{
    [JsonConstructor]
    public MergeFile() { }

    protected MergeFile(List<string> contents,FileClusterType clusterType) 
    {
        Contents = contents;
        ClusterType = clusterType;
    }

    [JsonPropertyName("content")]
    [JsonInclude]
    public List<string> Contents { get; private set; } = new();

    [JsonPropertyName("cluster_name")]
    [JsonInclude]

    public FileClusterType ClusterType { get; private set; }

    public override T Clone<T>()
    {
       return (T)(object)new MergeFile(Contents, ClusterType);
    }

    public static MergeFile Create(List<string> contents, FileClusterType clusterType)
    {
        return Create(() =>
        {
            return new MergeFile(contents, clusterType);
        });
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Contents;
    }

    protected override void Validate()
    {
        return;
    }
}
