using ReciteHelper.Application.Services;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Infrastructure.Services;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Views;

public partial class StudyDashboardView : UserControl
{
    private static readonly Brush[] HeatScale =
    [
        Solid(0xF4, 0xF7, 0xF5), Solid(0xE0, 0xEE, 0xE5), Solid(0xC7, 0xE2, 0xD0),
        Solid(0xA3, 0xCA, 0xB2), Solid(0x7E, 0xAC, 0x94)
    ];
    private readonly LocalStudyAccount _account;
    private CancellationTokenSource? _refresh;
    private StudyProfile? _profile;
    private StudySummary? _summary;
    private Dictionary<DateOnly, StudyDay> _days = [];
    private IReadOnlyDictionary<DateOnly, StudyActivityTime> _activityDays = new Dictionary<DateOnly, StudyActivityTime>();
    private readonly Dictionary<DateOnly, Button> _dateButtons = [];
    private readonly Dictionary<DateOnly, (Button Mark, Border Bar)> _trendMarks = [];
    private DateOnly _month;
    private DateOnly _date;

    internal StudyDashboardView(LocalStudyAccount account)
    {
        _account = account;
        InitializeComponent();
        foreach (var day in "一二三四五六日")
            CalendarLabels.Children.Add(new TextBlock
            {
                Text = day.ToString(), HorizontalAlignment = HorizontalAlignment.Center,
                Style = (Style)FindResource("StudyCaption")
            });
        foreach (var brush in HeatScale)
            HeatLegend.Children.Add(new Border
            {
                Width = 11, Height = 11, Margin = new Thickness(0, 0, 3, 0), CornerRadius = new CornerRadius(2),
                Background = brush
            });
        HeatLegend.Children.Add(new TextBlock { Text = "多", Style = (Style)FindResource("StudyCaption") });
        Loaded += async (_, _) => await RefreshAsync();
        Unloaded += (_, _) => _refresh?.Cancel();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        _refresh?.Cancel();
        var request = new CancellationTokenSource();
        _refresh = request;
        SetStatus("正在读取学习记录…");
        try
        {
            var account = await _account.LoadAsync();
            request.Token.ThrowIfCancellationRequested();
            SetProfile(account.Profile);
            var (history, summary) = await Task.Run(async () =>
            {
                var history = await StudyHistoryReader.ReadAsync(account.Projects, request.Token);
                request.Token.ThrowIfCancellationRequested();
                return (history, StudyAnalytics.Summarize(history.Attempts, DateTimeOffset.Now, TimeZoneInfo.Local));
            }, request.Token);
            request.Token.ThrowIfCancellationRequested();
            ShowSummary(summary);
            SetStatus(history.UnavailableProjects > 0 ? $"暂时无法读取 {history.UnavailableProjects} 个项目，刷新后会重新读取。" : null);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            if (!request.IsCancellationRequested) SetStatus($"学习记录读取失败。{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_refresh, request)) _refresh = null;
            request.Dispose();
        }
    }

    private void SetStatus(string? text)
    {
        StatusText.Text = text;
        StatusText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetProfile(StudyProfile profile)
    {
        _profile = profile;
        ProfileName.Text = profile.Name;
        ProfileAvatar.Fill = StudyProfileWindow.AvatarBrush(profile.Avatar);
        ProfileButton.IsEnabled = true;
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profile is null) return;
        var editor = new StudyProfileWindow(_account, _profile) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() is true) SetProfile(editor.Profile);
    }

    private void ShowSummary(StudySummary summary)
    {
        _summary = summary;
        _days = summary.Days.ToDictionary(day => day.Date);
        _activityDays = App.StudyActivity?.Snapshot() ?? new Dictionary<DateOnly, StudyActivityTime>();
        var totals = summary.Totals;
        var activity = _activityDays.Values.Aggregate(default(StudyActivityTime), (total, day) => total + day);
        DrawRating(summary.Rating);
        OverviewMetrics.ItemsSource = new[]
        {
            Metric("CalendarIcon", "学习天数", $"{summary.Days.Count:N0} 天",
                $"当前连续 {summary.CurrentStreak} 天\n最长连续 {summary.LongestStreak} 天"),
            EfficiencyMetric(activity),
            Metric("TargetIcon", "正确率", Percent(totals.Accuracy), AccuracyDetails(totals)),
            Metric("ListIcon", "作答次数", totals.Attempts.ToString("N0"),
                $"练习 {totals.PracticeAttempts:N0} 次\n考试 {totals.Exams:N0} 场"),
            Metric("BookIcon", "学习科目", summary.Subjects.Count.ToString("N0"), $"已练 {totals.Topics:N0} 道不同原题"),
            Metric("ClockIcon", "累计计时", ElapsedValue(totals, compact: true), DurationDetails(totals))
        };
        HistoryRange.Text = summary.Days.Count > 0
            ? $"{summary.Days[0].Date:yyyy/MM/dd} — {summary.Days[^1].Date:yyyy/MM/dd}"
            : "暂无学习记录";
        AllSubjects.ItemsSource = summary.Subjects.Select(ToSubjectRow).ToArray();
        EmptySubjects.Visibility = summary.Subjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_date == default)
        {
            _date = _days.Keys.Concat(_activityDays.Keys).DefaultIfEmpty(Today()).Max();
            _month = FirstOfMonth(_date);
        }
        DrawMonth();
        SelectDate(_date);
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e) => SetMonth(_month.AddMonths(-1));
    private void NextMonth_Click(object sender, RoutedEventArgs e) => SetMonth(_month.AddMonths(1));

    private void SetMonth(DateOnly month)
    {
        _month = month;
        var mostRecent = _days.Keys.Concat(_activityDays.Keys).Where(day => FirstOfMonth(day) == month)
            .DefaultIfEmpty(month).Max();
        DrawMonth();
        SelectDate(mostRecent);
    }

    private void SelectDate(DateOnly date)
    {
        HighlightDate(_date, false);
        _date = date;
        HighlightDate(date, true);
        var day = _days.GetValueOrDefault(date);
        var activity = _activityDays.GetValueOrDefault(date);
        SelectedDay.Text = date.ToString("M 月 d 日  dddd", CultureInfo.GetCultureInfo("zh-CN"));
        DayMetrics.ItemsSource = day is { Totals: var totals } ? new[]
        {
            Metric("ListIcon", "作答", $"{totals.Attempts:N0} 次"),
            Metric("TargetIcon", "正确率", Percent(totals.Accuracy), AccuracyDetails(totals)),
            Metric("ClockIcon", "计时", ElapsedValue(totals), DurationDetails(totals)),
            EfficiencyMetric(activity)
        } : [Metric("ListIcon", "作答", "0 次"), EfficiencyMetric(activity)];
        DaySubjects.ItemsSource = day?.Subjects.Select(ToSubjectRow).ToArray();
        var sessions = _summary?.Sessions.Where(session => session.Date == date).Select(ToSessionRow).ToArray() ?? [];
        DaySessions.ItemsSource = sessions;
        EmptyDay.Visibility = day is null ? Visibility.Visible : Visibility.Collapsed;
        EmptySessions.Visibility = sessions.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HighlightDate(DateOnly date, bool selected)
    {
        if (_dateButtons.TryGetValue(date, out var cell))
            cell.BorderBrush = selected ? BrushResource("StudyGreen") : Brushes.Transparent;
        if (_trendMarks.TryGetValue(date, out var trend))
        {
            trend.Mark.Background = selected ? BrushResource("StudyMist") : Brushes.Transparent;
            trend.Bar.Background = BrushResource(selected ? "StudyGreen" : "StudyBar");
        }
    }

    private void DrawMonth()
    {
        var today = Today();
        MonthLabel.Text = $"{_month:yyyy 年 M 月}";
        var earliest = _days.Keys.Concat(_activityDays.Keys).DefaultIfEmpty(today).Min();
        PreviousMonth.IsEnabled = _month > FirstOfMonth(earliest);
        NextMonth.IsEnabled = _month < FirstOfMonth(today);
        CalendarDays.Children.Clear();
        _dateButtons.Clear();
        TrendBars.Children.Clear();
        TrendDates.Children.Clear();
        _trendMarks.Clear();
        var start = ((int)_month.DayOfWeek + 6) % 7;
        for (var index = 0; index < start; index++) CalendarDays.Children.Add(new Border());
        var days = DateTime.DaysInMonth(_month.Year, _month.Month);
        var counts = Enumerable.Range(0, days)
            .Select(index => _days.GetValueOrDefault(_month.AddDays(index))?.Totals.Attempts ?? 0).ToArray();
        var maximum = AxisMaximum(counts.Max());
        TrendHigh.Text = maximum.ToString("N0");
        TrendMid.Text = (maximum / 2).ToString("N0");
        TrendBars.Columns = TrendDates.Columns = days;
        for (var value = 1; value <= days; value++)
        {
            var date = _month.AddDays(value - 1);
            var count = counts[value - 1];
            var level = count switch { 0 => 0, < 5 => 1, < 15 => 2, < 30 => 3, _ => 4 };
            var caption = $"{date:yyyy 年 M 月 d 日}，作答 {count} 次";
            var cell = new Button
            {
                Content = value, MinHeight = 24, Padding = new Thickness(0), Margin = new Thickness(2),
                Background = HeatScale[level], BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1), Style = (Style)FindResource("CalendarMark"),
                IsEnabled = date <= today, ToolTip = caption
            };
            AutomationProperties.SetName(cell, caption);
            cell.Click += (_, _) => SelectDate(date);
            _dateButtons.Add(date, cell);
            CalendarDays.Children.Add(cell);

            AddTrendMark(date, count, maximum, caption, date <= today);
            TrendDates.Children.Add(new TextBlock
            {
                Text = value is 1 or 5 or 10 or 15 or 20 or 25 || value == days ? value.ToString() : string.Empty,
                Style = (Style)FindResource("StudyCaption"), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center
            });
        }
    }

    private void AddTrendMark(DateOnly date, int count, int maximum, string caption, bool enabled)
    {
        var plotColumn = new Grid { Margin = new Thickness(2, 0, 2, 1) };
        plotColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(maximum - count, GridUnitType.Star) });
        plotColumn.RowDefinitions.Add(new RowDefinition { Height = new GridLength(count, GridUnitType.Star) });
        var bar = new Border { Background = BrushResource("StudyBar"), CornerRadius = new CornerRadius(2, 2, 0, 0) };
        Grid.SetRow(bar, 1);
        plotColumn.Children.Add(bar);
        var mark = new Button
        {
            Style = (Style)FindResource("TrendMark"), Content = plotColumn, IsEnabled = enabled, ToolTip = caption
        };
        AutomationProperties.SetName(mark, caption);
        mark.Click += (_, _) => SelectDate(date);
        _trendMarks.Add(date, (mark, bar));
        TrendBars.Children.Add(mark);
    }

    private void DrawRating(StudyRating rating)
    {
        BestRing.Data = RingArc(0, rating.BestContribution);
        RecentRing.Data = RingArc(rating.BestContribution, rating.RecentContribution);
        RatingValue.Text = rating.HasScore ? $"{rating.Score:F2}" : "暂无";
        RatingScale.Visibility = rating.HasScore ? Visibility.Visible : Visibility.Collapsed;
        var hint = rating.HasScore
            ? $"学习评分 {rating.Score:F2} / 100\n深绿为历史表现，金色为近期表现。"
            : $"暂无学习评分。一场学习至少要有 {StudyAnalytics.MinimumTopics} 道不同原题获得评分。";
        if (rating.HasScore && rating.HasOpenSlots) hint += "\n场次尚未填满，当前分数按已记录的场次计算。";
        ProfileMedallion.ToolTip = ProfileRating.ToolTip = hint;
        AutomationProperties.SetHelpText(ProfileButton, hint);
    }

    private static Geometry RingArc(double start, double value)
    {
        if (value <= 0) return Geometry.Empty;
        const double radius = 23.25;
        static Point OnRing(double position)
        {
            var angle = position * Math.Tau / 100;
            return new(27 + radius * Math.Sin(angle), 27 - radius * Math.Cos(angle));
        }
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(OnRing(start), false, false);
            path.ArcTo(OnRing(start + value), new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static int AxisMaximum(int maximum)
    {
        var step = maximum switch { <= 10 => 2, <= 50 => 10, <= 250 => 50, _ => 100 };
        return Math.Max(step, ((maximum + step - 1) / step) * step);
    }

    private MetricItem Metric(string icon, string label, string value, string? details = null)
        => new((Geometry)FindResource(icon), label, value, details);

    private MetricItem EfficiencyMetric(StudyActivityTime activity)
    {
        var details = activity.Efficiency is null ? "暂无前台使用记录。"
            : $"有效学习 {Duration(activity.ActiveSeconds)}\n前台使用 {Duration(activity.ForegroundSeconds)}\n" +
              "根据输入、点击和滚动估算。长时间无操作会降低效率。";
        return Metric("EfficiencyIcon", "学习效率", Percent(activity.Efficiency), details);
    }

    private Brush BrushResource(string key) => (Brush)FindResource(key);

    private static SubjectRow ToSubjectRow(SubjectStudy subject)
    {
        var totals = subject.Totals;
        return new(subject.Name, $"{totals.Attempts:N0} 次", Percent(totals.Accuracy), Percent(totals.AverageQuality),
            (totals.Accuracy ?? 0) * 100, $"{subject.Name}\n{AccuracyDetails(totals)}");
    }

    private SessionRow ToSessionRow(StudySession session) => new(
        (Geometry)FindResource(session.IsExam ? "ExamIcon" : "PracticeIcon"), session.End.ToLocalTime().ToString("HH:mm"),
        session.Subject, session.IsExam ? "考试" : "练习", $"{session.Attempts:N0} 次",
        $"{session.Subject}\n{session.End.ToLocalTime():HH:mm}　作答 {session.Attempts:N0} 次");

    private static string AccuracyDetails(StudyTotals totals) =>
        $"答对 {totals.Correct:N0} 次 / 已判定 {totals.Graded:N0} 次\n平均得分 {Percent(totals.AverageQuality)}";

    private static string ElapsedValue(StudyTotals totals, bool compact = false) =>
        totals.TimedPracticeAttempts + totals.TimedExams == 0 ? "—" : Duration(totals.PracticeSeconds + totals.ExamSeconds, compact);

    private static string DurationDetails(StudyTotals totals)
    {
        var parts = new List<string>();
        if (totals.TimedPracticeAttempts > 0)
            parts.Add($"练习计时 {Duration(totals.PracticeSeconds)}（{totals.TimedPracticeAttempts} 次）");
        if (totals.TimedExams > 0)
            parts.Add($"考试计时 {Duration(totals.ExamSeconds)}（{totals.TimedExams} 场）");
        return string.Join("\n", parts);
    }

    private static string Percent(double? ratio) => ratio is { } value ? $"{value * 100:F0}%" : "—";
    private static string Duration(double seconds, bool compact = false)
    {
        var elapsed = TimeSpan.FromSeconds(seconds);
        if (compact)
        {
            if (elapsed.TotalHours >= 1) return $"{(int)elapsed.TotalHours}时{elapsed.Minutes}分";
            return elapsed.TotalMinutes >= 1 ? $"{elapsed.Minutes} 分" : $"{elapsed.Seconds} 秒";
        }
        if (elapsed.TotalHours >= 1) return $"{(int)elapsed.TotalHours} 小时 {elapsed.Minutes} 分";
        return elapsed.TotalMinutes >= 1 ? $"{elapsed.Minutes} 分 {elapsed.Seconds} 秒" : $"{elapsed.Seconds} 秒";
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);
    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static Brush Solid(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private sealed record MetricItem(Geometry Icon, string Label, string Value, string? Details);
    private sealed record SubjectRow(string Name, string Attempts, string Accuracy, string Quality, double AccuracyValue, string Details);
    private sealed record SessionRow(Geometry Icon, string Time, string Subject, string Mode, string Attempts, string Details);
}
