using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed record StudyProfile(Guid Id, string Name, byte[]? Avatar);

public sealed record StudyProject(string FilePath, DateTimeOffset? ImportedAt = null);

public sealed record StudyAccount(StudyProfile Profile, IReadOnlyList<StudyProject> Projects);

public sealed class LocalStudyAccount(string? filePath = null)
{
    private readonly string _path = filePath ?? Path.Combine(AppContext.BaseDirectory, "study_account.json");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StudyAccount? _current;

    public Task<StudyAccount> LoadAsync() => UpdateAsync(account => account);

    public Task<StudyAccount> RegisterAsync(IEnumerable<StudyProject> projects) => UpdateAsync(account =>
    {
        var registered = account.Projects.Select(project => project.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = projects
            .Select(project => project with { FilePath = Path.GetFullPath(project.FilePath) })
            .Where(project => registered.Add(project.FilePath)).ToArray();
        return added.Length == 0 ? account : account with { Projects = [.. account.Projects, .. added] };
    });

    public Task<StudyAccount> SaveProfileAsync(string name, byte[]? avatar) => UpdateAsync(account =>
        account with { Profile = account.Profile with { Name = name.Trim(), Avatar = avatar } });

    private async Task<StudyAccount> UpdateAsync(Func<StudyAccount, StudyAccount> update)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_current is null && File.Exists(_path))
            {
                await using var file = File.OpenRead(_path);
                _current = await JsonSerializer.DeserializeAsync<StudyAccount>(file).ConfigureAwait(false);
            }
            var account = _current ?? new StudyAccount(NewProfile(), []);
            var updated = update(account);
            if (!ReferenceEquals(_current, updated))
            {
                var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                    await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(updated)).ConfigureAwait(false);
                    File.Move(temporary, _path, true);
                }
                finally { File.Delete(temporary); }
            }
            return _current = updated;
        }
        finally { _gate.Release(); }
    }

    private static StudyProfile NewProfile()
    {
        string[] names = ["银杏", "鸢尾", "木槿", "福禄考", "山茶", "凤仙", "桔梗", "雪松"];
        return new(Guid.NewGuid(), $"{names[Random.Shared.Next(names.Length)]} {Random.Shared.Next(1000, 10000)}", null);
    }
}
