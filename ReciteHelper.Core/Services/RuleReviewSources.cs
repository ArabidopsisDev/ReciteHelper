using ReciteHelper.Core.Entities;

namespace ReciteHelper.Core.Services;

public sealed class RuleReviewSources(IEnumerable<ReviewMaterial> materials)
{
    private readonly ReviewMaterial[] _materials = materials.DistinctBy(material => material.SourceId).ToArray();
    private string[]? _normalized;

    public static RuleReviewSources ForChapter(Chapter chapter)
        => new(chapter.ReviewMaterials.Concat(chapter.PriorReviewMaterials ?? []));

    public List<string> LinkExcerpts(Question question, IReadOnlySet<string>? indicatedSources = null)
    {
        if (question.SourceExcerpts.Count == 0 || _materials.Length == 0) return [];

        _normalized ??= _materials.Select(material => SourceEvidenceOrder.Normalize(material.Content)).ToArray();
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var excerpt in question.SourceExcerpts)
        {
            var quote = SourceEvidenceOrder.Normalize(excerpt);
            if (quote.Length == 0) continue;
            var matches = Enumerable.Range(0, _normalized.Length)
                .Where(index => _normalized[index].Contains(quote, StringComparison.Ordinal))
                .Select(index => _materials[index].SourceId).ToArray();
            // A generic quote shared by multiple files cannot identify its own source.
            if (matches.Length == 1)
                linked.Add(matches[0]);
            else if (indicatedSources is not null)
            {
                var indicated = matches.Where(indicatedSources.Contains).Take(2).ToArray();
                if (indicated.Length == 1) linked.Add(indicated[0]);
            }
        }
        return [.. linked];
    }

    public IEnumerable<ReviewMaterial> Resolve(Question question)
    {
        var linked = question.SourceMaterialIds ?? LinkExcerpts(question);
        return _materials.Where(material => linked.Contains(material.SourceId));
    }
}
