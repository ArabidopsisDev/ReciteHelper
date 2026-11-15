using AquaAvgFramework.StoryLineComponents;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Infrastructure.Utilities;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReciteHelper.Infrastructure.Services;

public sealed class GalGameService : IGalGameService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.Preserve,
        WriteIndented = true
    };

    public bool Exists(Project project)
    {
        return File.Exists(GetGamePath(project));
    }

    public async Task<object> CompileStoryAsync(string storyCode)
    {
        return await Parser.CompileStoryAsync(storyCode);
    }

    public async Task<IReadOnlyList<object>> LoadStoryLinesAsync(Project project)
    {
        var gamePath = GetGamePath(project);
        var text = await File.ReadAllTextAsync(gamePath);

        try
        {
            var storyLines = JsonSerializer.Deserialize<List<StoryLine>>(text, JsonOptions);
            if (storyLines is not null)
                return storyLines.Cast<object>().ToList();
        }
        catch (JsonException)
        {
            // Older game files stored a single StoryLine instead of a collection.
        }

        var storyLine = JsonSerializer.Deserialize<StoryLine>(text, JsonOptions);
        return storyLine is null ? [] : [storyLine];
    }

    public async Task SaveStoryLinesAsync(Project project, IEnumerable<object> storyLines)
    {
        var typedStoryLines = storyLines.Cast<StoryLine>().ToList();
        var gamePath = GetGamePath(project);
        Directory.CreateDirectory(Path.GetDirectoryName(gamePath)!);

        var json = JsonSerializer.Serialize(typedStoryLines, JsonOptions);
        await File.WriteAllTextAsync(gamePath, json);
    }

    private static string GetGamePath(Project project)
    {
        if (project.StoragePath is null || project.ProjectName is null)
            throw new ArgumentException("Project storage path or project name is missing.");

        return Path.Combine(project.StoragePath, project.ProjectName, "game.rhgal");
    }
}
