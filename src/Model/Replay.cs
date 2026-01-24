using System.Collections.Concurrent;

namespace ReciteHelper.Model;

/// <summary>
/// Represents a replay session containing a collection of data chunks and associated chapters.
/// </summary>
/// <remarks>Use this class to access or modify the chunks and chapters that comprise a replay. The Chunks
/// property provides ordered segments of replay data, while the Chapters property contains groups of chapters, which
/// may be accessed concurrently. This class is not thread-safe for modifications to the Chunks property; however, the
/// Chapters property supports concurrent additions.</remarks>
class Replay(List<Chunk> chunks, ConcurrentBag<List<Chapter>> chapters)
{
    public List<Chunk> Chunks { get; set; } = chunks;
    public ConcurrentBag<List<Chapter>> Chapters { get; set; } = chapters;
}
