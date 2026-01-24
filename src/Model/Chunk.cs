namespace ReciteHelper.Model
{
    /// <summary>
    /// Represents a segment of content and its success status.
    /// </summary>
    class Chunk(int index, string content)
    {
        public int Index { get; set; } = index;
        public string Content { get; set; } = content;
        public bool IsSuccess { get; set; } = false;

        public override string ToString()
        {
            return $"Index:{Index}\nContent:{Content}\nStatus:{IsSuccess}";
        }
    }
}
