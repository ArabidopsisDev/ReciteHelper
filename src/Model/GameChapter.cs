using AquaAvgFramework.StoryLineComponents;

namespace ReciteHelper.Model;

/// <summary>
/// Represents a chapter within a game that is associated with a specific textbook chapter and storyline.
/// </summary>
public class GameChapter
{
    public string? TextbookChapterName { get; set; }
    public string? GameChapterName { get; set; }
    public string? GameChapterOutline { get; set; }
    public StoryLine? GameStoryLine { get; set; }
}
