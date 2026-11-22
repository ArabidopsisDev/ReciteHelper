using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public sealed class StudyDashboardTests
{
    [Fact]
    public void RatingUsesUniqueQuestionsAndSeparateBestAndRecentWindows()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var attempts = Enumerable.Range(0, 12)
            .SelectMany(day => Practice(start.AddDays(day), "植物病理学", day < 3 ? 1 : 0.8, 30))
            .ToList();
        // Same subject and day, but a separate session. It must not take another B10 slot.
        attempts.AddRange(Practice(start.AddDays(11).AddHours(2), "植物病理学", 0.98, 30));
        // A distinct subject can qualify for B10 on the same day. R3 still selects only one session per day.
        attempts.AddRange(Practice(start.AddDays(11).AddHours(3), "农业昆虫学", 0.9, 30));
        var original = attempts[0];
        attempts.AddRange(Enumerable.Range(1, 20).Select(index => original with { Time = original.Time.AddSeconds(index) }));

        var summary = StudyAnalytics.Summarize(attempts, start.AddDays(12), TimeZoneInfo.Utc);

        Assert.Equal(12, summary.Days.Count);
        Assert.Equal(12, summary.CurrentStreak);
        Assert.Equal(62, summary.Totals.Attempts);
        Assert.Equal(2, summary.Days[^1].Subjects.Count);
        var repeated = summary.Sessions.Single(session => session.Date.Day == 1);
        Assert.Equal(23, repeated.Attempts);
        Assert.Equal(3, repeated.ScoredTopics);
        Assert.Equal(10, summary.Rating.Best.Count);
        Assert.Single(summary.Rating.Best, session => session.Date.Day == 12 && session.Subject == "植物病理学");
        Assert.Equal(3, summary.Rating.Recent.Count);
        Assert.Equal(3, summary.Rating.Recent.Select(session => session.Date).Distinct().Count());
        Assert.All(summary.Rating.Recent, session => Assert.True(session.Date.Day >= 4));
        Assert.Equal(summary.Rating.Best.Concat(summary.Rating.Recent).Sum(session => session.Performance!.Score) / 13,
            summary.Rating.Score, 8);

        var normal = StudyAnalytics.Summarize(Practice(start, "植物病理学", 1, 30), start, TimeZoneInfo.Utc);
        var rapid = StudyAnalytics.Summarize(Practice(start, "植物病理学", 1, 1), start, TimeZoneInfo.Utc);
        var slow = StudyAnalytics.Summarize(Practice(start, "植物病理学", 1, 600), start, TimeZoneInfo.Utc);
        var lowerQuality = StudyAnalytics.Summarize(Practice(start, "植物病理学", 0.3, 30), start, TimeZoneInfo.Utc);
        var unsuccessful = StudyAnalytics.Summarize(Practice(start, "植物病理学", 0, 30), start, TimeZoneInfo.Utc);
        Assert.Equal(Assert.Single(normal.Rating.Best).Performance!.Score, normal.Rating.Score, 8);
        Assert.True(normal.Rating.HasOpenSlots);
        Assert.Equal(normal.Rating.Score, rapid.Rating.Score, 8);
        Assert.InRange(normal.Rating.Score - slow.Rating.Score, 0.01, 2);
        Assert.True(rapid.Rating.Score > lowerQuality.Rating.Score);
        Assert.True(unsuccessful.Rating.HasScore);
        Assert.Equal(0, unsuccessful.Rating.Score);
        Assert.Empty(StudyAnalytics.Summarize([original, original], start, TimeZoneInfo.Utc).Rating.Best);
    }

    [Fact]
    public async Task AccountAndHistoryReloadWithoutCountingDerivedFacetsOrImportedPriorWork()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StudyDashboardTests", Guid.NewGuid().ToString("N"));
        var time = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var files = new ProjectFileService();
        var question = new Question
        {
            ReviewTag = [new() { Time = time, QValue = 4, Similarity = 80 }],
            ReviewFacets = [new() { Question = new Question
            {
                ReviewTag = [new() { Time = time, IsCorrect = true },
                    new() { Time = time.AddMinutes(2), IsCorrect = false, ScoreRatio = 0.35, ResponseSeconds = 18 }]
            }}]
        };
        var project = new Project
        {
            ProjectName = "植物病理学", StoragePath = directory,
            Chapters = [new() { Questions = [question] }]
        };
        var shared = new Project
        {
            ProjectName = "共享项目", StoragePath = directory,
            Chapters = [new() { Questions = [new Question
            {
                ReviewTag = [new() { Time = time.AddDays(-10), IsCorrect = true },
                    new() { Time = time.AddDays(1), IsCorrect = true }]
            }] }]
        };
        try
        {
            await files.SaveProjectAsync(project);
            await files.SaveProjectAsync(shared);
            await new JsonExamSetRepository().SaveAsync(project, new ExamSet
            {
                Title = "期末", SubjectName = "植物病理学",
                Questions = Enumerable.Range(0, 3).Select(index => new ExamSetQuestion
                {
                    Number = index + 1, Score = 4, Question = new Question
                    {
                        ExamHistory = [new() { Time = time.AddHours(1), IsCorrect = index < 2,
                            ScoreRatio = index < 2 ? 1 : 0.1, ExamDurationSeconds = 180 }]
                    }
                }).ToList()
            });
            var accountFile = Path.Combine(directory, "account.json");
            var account = new LocalStudyAccount(accountFile);
            var initial = await account.LoadAsync();
            Assert.False(string.IsNullOrWhiteSpace(initial.Profile.Name));
            Assert.Null(initial.Profile.Avatar);
            var path = Path.Combine(directory, "植物病理学", "植物病理学.rhproj");
            var sharedPath = Path.Combine(directory, "共享项目", "共享项目.rhproj");
            await account.RegisterAsync([new(path), new(sharedPath, new DateTimeOffset(time))]);
            await account.RegisterAsync([new(sharedPath)]);
            await account.SaveProfileAsync("  凤仙笔记  ", [1, 2, 3]);

            var loaded = await new LocalStudyAccount(accountFile).LoadAsync();
            Assert.Equal(initial.Profile.Id, loaded.Profile.Id);
            Assert.Equal("凤仙笔记", loaded.Profile.Name);
            Assert.Equal(new byte[] { 1, 2, 3 }, loaded.Profile.Avatar);
            var history = await StudyHistoryReader.ReadAsync(loaded.Projects);
            Assert.Equal(0, history.UnavailableProjects);
            Assert.Equal(6, history.Attempts.Count);
            Assert.Equal(0.8, history.Attempts.Single(attempt => attempt.Time == time).Quality);
            var summary = StudyAnalytics.Summarize(history.Attempts, time.AddDays(1), TimeZoneInfo.Utc);
            Assert.Equal(2, summary.Days.Count);
            Assert.Equal(4, summary.Totals.Correct);
            Assert.Equal(1, summary.Totals.Exams);
            Assert.Equal(180, summary.Totals.ExamSeconds);
            Assert.Equal(18, summary.Totals.PracticeSeconds);
            Assert.Single(summary.Rating.Best);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static StudyAttempt[] Practice(DateTimeOffset time, string subject, double quality, double seconds)
        => Enumerable.Range(0, 3).Select(index =>
        {
            var id = Guid.NewGuid();
            return new StudyAttempt(subject, subject, id, id, time.AddMinutes(index), false, quality >= 0.6,
                quality, seconds, null, 30);
        }).ToArray();
}
