using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.ValueObjects;

namespace ReciteHelper.Application.Services;

public sealed record StudyTotals(
    int Attempts, int Graded, int Correct, int Topics, int Exams, double? AverageQuality, double PracticeSeconds,
    int TimedPracticeAttempts, double ExamSeconds, int TimedExams)
{
    public double? Accuracy => Graded > 0 ? (double)Correct / Graded : null;
    public int PracticeAttempts { get; init; }
}

public sealed record SubjectStudy(string Name, StudyTotals Totals);
public sealed record StudyDay(DateOnly Date, StudyTotals Totals, IReadOnlyList<SubjectStudy> Subjects);

public sealed record StudyPerformance(double Score, double Quality, double Pace, double Volume);
public sealed record StudySession(string Subject, DateOnly Date, DateTimeOffset End, bool IsExam,
    int Attempts, int ScoredTopics, StudyPerformance? Performance);

public sealed record StudyRating(IReadOnlyList<StudySession> Best, IReadOnlyList<StudySession> Recent)
{
    public const int BestSlots = 10;
    public const int RecentSlots = 3;
    public const int RecentDays = 9;
    public double BestContribution => MeanScore(Best) * BestSlots / (BestSlots + RecentSlots);
    public double RecentContribution => MeanScore(Recent) * RecentSlots / (BestSlots + RecentSlots);
    public double Score => BestContribution + RecentContribution;
    public bool HasScore => Best.Count > 0 || Recent.Count > 0;
    public bool HasOpenSlots => Best.Count < BestSlots || Recent.Count < RecentSlots;

    private static double MeanScore(IReadOnlyList<StudySession> sessions) =>
        sessions.Count == 0 ? 0 : sessions.Average(session => session.Performance!.Score);
}

public sealed record StudySummary(StudyTotals Totals, IReadOnlyList<StudyDay> Days,
    IReadOnlyList<SubjectStudy> Subjects, IReadOnlyList<StudySession> Sessions, int CurrentStreak,
    int LongestStreak, StudyRating Rating);

public static class StudyAnalytics
{
    public const int MinimumTopics = 3;
    private const int SessionGapMinutes = 30;

    public static StudySummary Summarize(IReadOnlyList<StudyAttempt> attempts, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        DateOnly LocalDate(DateTimeOffset time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, timeZone).DateTime);
        var today = LocalDate(now);
        var days = attempts.GroupBy(attempt => LocalDate(attempt.Time)).OrderBy(day => day.Key).ToArray();
        var daily = days.Select(day => new StudyDay(day.Key, Totals(day), Subjects(day))).ToArray();
        var sessions = days.SelectMany(day => day.GroupBy(attempt => (attempt.ProjectPath, attempt.Subject, attempt.IsExam))
            .SelectMany(group => Sessions(day.Key, group.Key.IsExam, group.OrderBy(attempt => attempt.Time))))
            .OrderByDescending(session => session.End).ToArray();
        var (current, longest) = Streaks(daily, today);
        return new(Totals(attempts), daily, Subjects(attempts), sessions, current, longest, Rating(sessions));
    }

    private static SubjectStudy[] Subjects(IEnumerable<StudyAttempt> attempts) => attempts
        .GroupBy(attempt => attempt.Subject)
        .Select(subject => new SubjectStudy(subject.Key, Totals(subject)))
        .OrderByDescending(subject => subject.Totals.Attempts).ThenBy(subject => subject.Name)
        .ToArray();

    private static StudyTotals Totals(IEnumerable<StudyAttempt> source)
    {
        var attempts = source.ToArray();
        var practice = attempts.Where(attempt => !attempt.IsExam).ToArray();
        var exams = attempts.Where(attempt => attempt.IsExam)
            .GroupBy(attempt => (attempt.ProjectPath, attempt.Time))
            .Select(exam => exam.Max(attempt => attempt.ExamDurationSeconds)).ToArray();
        var practiceTimes = practice.Where(attempt => attempt.ResponseSeconds is not null)
            .Select(attempt => attempt.ResponseSeconds!.Value).ToArray();
        return new(
            attempts.Length,
            attempts.Count(attempt => attempt.IsCorrect is not null),
            attempts.Count(attempt => attempt.IsCorrect is true),
            attempts.Select(attempt => attempt.TopicId).Distinct().Count(),
            exams.Length, attempts.Average(attempt => attempt.Quality), practiceTimes.Sum(), practiceTimes.Length,
            exams.Sum() ?? 0, exams.Count(seconds => seconds is not null))
        {
            PracticeAttempts = practice.Length
        };
    }

    private static IEnumerable<StudySession> Sessions(DateOnly date, bool isExam, IEnumerable<StudyAttempt> attempts)
    {
        var batch = new List<StudyAttempt>();
        foreach (var attempt in attempts)
        {
            if (batch.Count > 0 && (isExam ? attempt.Time != batch[^1].Time
                    : attempt.Time - batch[^1].Time > TimeSpan.FromMinutes(SessionGapMinutes)))
            {
                yield return Score(date, batch);
                batch.Clear();
            }
            batch.Add(attempt);
        }
        if (batch.Count > 0) yield return Score(date, batch);
    }

    private static StudySession Score(DateOnly date, List<StudyAttempt> batch)
    {
        // The first graded answer per original question supplies the score for this session.
        var scored = batch.Where(attempt => attempt.Quality is not null)
            .DistinctBy(attempt => attempt.TopicId).ToArray();
        StudyPerformance? performance = null;
        if (scored.Length >= MinimumTopics)
        {
            var quality = scored.Average(attempt => attempt.HasFallacy
                ? Math.Min(attempt.Quality!.Value, 0.40) : attempt.Quality!.Value);
            var pace = scored.Average(attempt => attempt.IsCorrect is true && !attempt.HasFallacy &&
                attempt.ResponseSeconds is double seconds && seconds > 0
                ? Math.Min(1, attempt.ExpectedSeconds / seconds) : 0);
            var volume = 1 - Math.Exp(-scored.Length / 12d);
            var factor = 0.90 + 0.08 * volume + 0.02 * pace;
            var measured = (batch[0].IsExam ? batch.AsEnumerable() : scored)
                .Select(attempt => attempt.Activity).OfType<StudyActivityTime>()
                .Where(activity => activity.ForegroundSeconds > 0).ToArray();
            var activity = batch[0].IsExam ? measured.FirstOrDefault()
                : measured.Aggregate(default(StudyActivityTime), (total, sample) => total + sample);
            if (activity.ForegroundSeconds >= 15 && activity.Efficiency is { } efficiency)
            {
                var share = batch[0].IsExam ? 1d : (double)measured.Length / scored.Length;
                factor += 0.08 * share * (efficiency - factor);
            }
            var points = 100 * quality * factor;
            performance = new(points, quality, pace, volume);
        }
        var first = batch[0];
        return new(first.Subject, date, batch[^1].Time, first.IsExam,
            batch.Count, scored.Length, performance);
    }

    private static StudyRating Rating(IEnumerable<StudySession> sessions)
    {
        var ranked = sessions.Where(session => session.Performance is not null)
            .OrderByDescending(session => session.Performance!.Score).ThenByDescending(session => session.End).ToArray();
        var best = ranked.DistinctBy(session => (session.Date, session.Subject))
            .Take(StudyRating.BestSlots).ToArray();
        var recent = ranked.DistinctBy(session => session.Date).OrderByDescending(session => session.Date)
            .Take(StudyRating.RecentDays).OrderByDescending(session => session.Performance!.Score)
            .ThenByDescending(session => session.End).Take(StudyRating.RecentSlots).ToArray();
        return new(best, recent);
    }

    private static (int Current, int Longest) Streaks(IReadOnlyList<StudyDay> days, DateOnly today)
    {
        var longest = 0;
        var streak = 0;
        var latest = DateOnly.MinValue;
        foreach (var day in days)
        {
            streak = day.Date.DayNumber == latest.DayNumber + 1 ? streak + 1 : 1;
            longest = Math.Max(longest, streak);
            latest = day.Date;
        }
        return (today.DayNumber - latest.DayNumber is 0 or 1 ? streak : 0, longest);
    }
}
