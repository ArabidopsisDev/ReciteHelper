using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.ValueObjects;
using System.IO.Compression;
using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed class ProjectFileService : IProjectFileService
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public bool ProjectExists(string projectPath)
    {
        return File.Exists(projectPath);
    }

    public async Task<Project?> OpenProjectAsync(string projectPath)
    {
        if (!File.Exists(projectPath))
            return null;

        await using var stream = File.Open(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var project = await JsonSerializer.DeserializeAsync<Project>(stream);
        project?.UpdateLastAccessed();

        if (project?.KnowledgeBasePath is not null)
        {
            var knowledgeBasePath = ResolveProjectRelativePath(projectPath, project.KnowledgeBasePath);
            if (File.Exists(knowledgeBasePath))
            {
                try
                {
                    project.LoadKnowledgeBase(new FileVectorStore(knowledgeBasePath));
                }
                catch (JsonException ex)
                {
                    project.MarkKnowledgeBaseBuildFailed($"知识库文件无法读取：{ex.Message}");
                }
            }
        }

        return project;
    }

    public async Task SaveProjectAsync(Project project)
    {
        if (project.StoragePath is null || project.ProjectName is null)
            throw new ArgumentException("Project storage path or project name is missing.");

        var projectPath = Path.Combine(project.StoragePath, project.ProjectName, $"{project.ProjectName}.rhproj");
        var stagedPath = $"{projectPath}.{Guid.NewGuid():N}.tmp";

        await _saveGate.WaitAsync();
        try
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
                var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(stagedPath, json);
                File.Move(stagedPath, projectPath, overwrite: true);
            }
            finally
            {
                File.Delete(stagedPath);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<ImportedProject> ImportProjectArchiveAsync(string archivePath, string destinationDirectory)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("找不到项目包文件。", archivePath);
        if (!Path.GetExtension(archivePath).Equals(".rhp", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("项目包必须是 .rhp 文件。");
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new ArgumentException("导入目录不能为空。", nameof(destinationDirectory));

        Directory.CreateDirectory(destinationDirectory);

        var packageName = SanitizeFileName(Path.GetFileNameWithoutExtension(archivePath));
        var destinationRoot = GetUniqueDirectoryPath(Path.Combine(destinationDirectory, packageName));
        Directory.CreateDirectory(destinationRoot);

        try
        {
            ZipFile.ExtractToDirectory(archivePath, destinationRoot);
            var projectPath = FindImportedProjectFile(destinationRoot);
            var project = await OpenProjectFileWithoutKnowledgeBaseAsync(projectPath)
                ?? throw new InvalidDataException("项目包中的项目文件无法读取。");

            project.StoragePath = destinationDirectory;
            project.ProjectName = Path.GetFileName(destinationRoot);

            var normalizedProjectPath = Path.Combine(destinationRoot, $"{project.ProjectName}.rhproj");
            if (!PathsEqual(projectPath, normalizedProjectPath))
            {
                if (File.Exists(normalizedProjectPath))
                    File.Delete(normalizedProjectPath);
                File.Move(projectPath, normalizedProjectPath);
                projectPath = normalizedProjectPath;
            }

            await SaveProjectAsync(project);
            return new ImportedProject(projectPath, project.ProjectName);
        }
        catch
        {
            TryDeleteDirectory(destinationRoot);
            throw;
        }
    }

    public async Task<string> ExportProjectArchiveAsync(Project project, string destinationArchivePath, string? version)
    {
        if (project.StoragePath is null || project.ProjectName is null)
            throw new ArgumentException("Project storage path or project name is missing.");
        if (string.IsNullOrWhiteSpace(destinationArchivePath))
            throw new ArgumentException("导出文件路径不能为空。", nameof(destinationArchivePath));

        var folderPath = Path.Combine(project.StoragePath, project.ProjectName);
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"项目文件夹不存在：{folderPath}");

        var archivePath = Path.ChangeExtension(destinationArchivePath, ".rhp");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        await SaveProjectAsync(project);

        var tempFolder = Path.Combine(Path.GetTempPath(), "ReciteHelperExport", $"{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempFolder);
            CopyDirectory(folderPath, tempFolder, path => ShouldExportPath(path, folderPath, archivePath));

            var manifest = new ProjectPackageManifest
            {
                Version = version,
                ProjectFile = $"{project.ProjectName}.rhproj",
                ProjectName = project.ProjectName,
                ExportedAtUtc = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(
                Path.Combine(tempFolder, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            if (File.Exists(archivePath))
                File.Delete(archivePath);
            ZipFile.CreateFromDirectory(tempFolder, archivePath, CompressionLevel.Optimal, false);
            return archivePath;
        }
        finally
        {
            TryDeleteDirectory(tempFolder);
        }
    }

    private static async Task<Project?> OpenProjectFileWithoutKnowledgeBaseAsync(string projectPath)
    {
        await using var stream = File.OpenRead(projectPath);
        return await JsonSerializer.DeserializeAsync<Project>(stream);
    }

    private static string ResolveProjectRelativePath(string projectPath, string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(Path.GetDirectoryName(projectPath)!, path);
    }

    private static string FindImportedProjectFile(string rootDirectory)
    {
        var files = Directory.EnumerateFiles(rootDirectory, "*.rhproj", SearchOption.AllDirectories)
            .Where(path => !IsInsideDirectory(path, Path.Combine(rootDirectory, "output")))
            .ToList();
        return files.Count switch
        {
            0 => throw new InvalidDataException("项目包中没有找到 .rhproj 项目文件。"),
            1 => files[0],
            _ => files.FirstOrDefault(path =>
                    Path.GetDirectoryName(path)?.Equals(rootDirectory, StringComparison.OrdinalIgnoreCase) is true)
                ?? throw new InvalidDataException("项目包中包含多个 .rhproj 文件，无法确定主项目文件。")
        };
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        Func<string, bool>? shouldCopy = null)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            if (shouldCopy is not null && !shouldCopy(filePath))
                continue;

            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, true);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            if (shouldCopy is not null && !shouldCopy(childDirectory))
                continue;

            var childDestination = Path.Combine(destinationDirectory, Path.GetFileName(childDirectory));
            CopyDirectory(childDirectory, childDestination, shouldCopy);
        }
    }

    private static bool ShouldExportPath(string path, string projectFolder, string archivePath)
    {
        if (PathsEqual(path, archivePath))
            return false;

        var outputDirectory = Path.Combine(projectFolder, "output");
        return !IsInsideDirectory(path, outputDirectory);
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueDirectoryPath(string directoryPath)
    {
        if (!Directory.Exists(directoryPath) && !File.Exists(directoryPath))
            return directoryPath;

        for (var index = 1; index < 1000; index++)
        {
            var candidate = $"{directoryPath} ({index})";
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
                return candidate;
        }

        throw new IOException("无法创建唯一的导入目录。");
    }

    private static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(name
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "ReciteHelperProject" : sanitized;
    }

    private static bool PathsEqual(string first, string second)
    {
        return string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch
        {
            // Best-effort cleanup only; the original import/export error is more useful to the caller.
        }
    }

    private sealed class ProjectPackageManifest
    {
        public string? Version { get; set; }
        public string? ProjectFile { get; set; }
        public string? ProjectName { get; set; }
        public DateTime ExportedAtUtc { get; set; }
    }
}
