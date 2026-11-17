using System.Text.RegularExpressions;
using ReciteHelper.Core.Entities;

namespace ReciteHelper.Infrastructure.Services;

/// <summary>Keep relevant source passages within one generation request; AI extracts the final quoted evidence.</summary>
internal static class RuleReviewMaterialSelector
{
    public static IReadOnlyList<ReviewMaterial> Select(IReadOnlyList<ReviewMaterial> materials, string scope)
    {
        const int budget = 24000;
        if (materials.Sum(m => m.Content.Length) <= budget) return materials;
        var terms = Regex.Matches(scope, @"[\p{IsCJKUnifiedIdeographs}]{2,}|[a-zA-Z]{3,}")
            .SelectMany(m => m.Value.Length > 3 && m.Value.Any(c => c > 255)
                ? Enumerable.Range(0, m.Value.Length - 1).Select(i => m.Value.Substring(i, 2)) : [m.Value])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var chunks = materials.SelectMany((m, file) => Enumerable.Range(0, (m.Content.Length + 799) / 800)
            .Select(i => new { File = file, Index = i, Text = m.Content.Substring(i * 800, Math.Min(1000, m.Content.Length - i * 800)) }))
            .Select(c => new { Chunk = c, Score = terms.Count(t => c.Text.Contains(t, StringComparison.OrdinalIgnoreCase)) })
            .OrderByDescending(c => c.Score).ThenBy(c => c.Chunk.File).ThenBy(c => c.Chunk.Index)
            .Take(budget / 1000).Select(c => c.Chunk).OrderBy(c => c.File).ThenBy(c => c.Index);
        return chunks.GroupBy(c => c.File).Select(group => new ReviewMaterial(materials[group.Key].Name,
            string.Join("\n\n", group.Select(c => c.Text)))).ToList();
    }
}
