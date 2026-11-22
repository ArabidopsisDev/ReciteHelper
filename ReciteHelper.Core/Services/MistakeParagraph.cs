using ReciteHelper.Core.Entities;
using System.Text.RegularExpressions;

namespace ReciteHelper.Core.Services;

public enum MistakeTextStyle
{
    Plain,
    Missing,
    Incorrect
}

public readonly record struct MistakeTextRun(string Text, MistakeTextStyle Style = MistakeTextStyle.Plain);

public static partial class MistakeParagraph
{
    public const string MissingForeground = "79540A";
    public const string MissingBackground = "FFF1B8";
    public const string IncorrectForeground = "A32638";

    public static IEnumerable<MistakeTextRun> FormatAnswer(IReadOnlyList<MistakeAnswerSpan> answer)
        => answer.Select(span => new MistakeTextRun(span.Text, span.Status switch
        {
            "incorrect" => MistakeTextStyle.Incorrect,
            "partial" => MistakeTextStyle.Missing,
            _ => MistakeTextStyle.Plain
        }));

    public static IEnumerable<MistakeTextRun> Format(IReadOnlyList<MistakeCorrection> corrections)
    {
        foreach (var group in corrections.GroupBy(correction => correction.Part).OrderBy(group => PartOrder(group.Key)))
        {
            var points = group.Select(correction => new MistakeTextRun(Sentence(correction.Text), correction.Action switch
            {
                "补充" => MistakeTextStyle.Missing,
                "订正" => MistakeTextStyle.Incorrect,
                _ => MistakeTextStyle.Plain
            })).Where(point => point.Text.Length > 0).ToArray();
            if (points.Length == 0) continue;

            if (!string.IsNullOrWhiteSpace(group.Key))
                yield return new MistakeTextRun($"（{group.Key}）");
            foreach (var point in points)
                yield return point;
        }
    }

    private static string Sentence(string text)
    {
        var line = Whitespace().Replace(text, " ").Trim().TrimEnd('，', ',', '；', ';').TrimEnd();
        return line.Length == 0 || "。？！.!?".Contains(line[^1]) ? line : line + "。";
    }

    private static int PartOrder(string? part)
        => part is not null && int.TryParse(PartNumber().Match(part).Value, out var number) ? number : 0;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\d+")]
    private static partial Regex PartNumber();
}
