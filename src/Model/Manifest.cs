namespace ReciteHelper.Model;

/// <summary>
/// Represents metadata information for a project, including references to associated files and version details.
/// </summary>
class Manifest
{
    public string? BankFile { get; set; }
    public string? Version { get; set; }
    public string? ProjectFile { get; set; }
}
