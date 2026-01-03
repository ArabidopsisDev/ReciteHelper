namespace ReciteHelper.Model;

/// <summary>
/// Represents a file to be merged, including its contents and associated cluster type.
/// </summary>
public class MergeFile
{
    public List<string> Contents { get; set; } = new();

    public FileClusterType ClusterType { get; set; }
}

/// <summary>
/// Specifies the strategy used to cluster file contents when processing multiple files.
/// </summary>
/// <remarks>Use this enumeration to select whether clustering is performed after merging all file contents
/// (Discrete) or if partial clustering is performed on each file before merging (Sequential). The choice of clustering
/// type can affect the accuracy and performance of the clustering process.</remarks>
public enum FileClusterType
{
    /// <summary>
    /// After merging all the file contents, perform multiple rounds of full clustering
    /// </summary>
    Discrete,

    /// <summary>
    /// Perform partial clustering on each file and then merge them directly
    /// </summary>
    Sequential
}
