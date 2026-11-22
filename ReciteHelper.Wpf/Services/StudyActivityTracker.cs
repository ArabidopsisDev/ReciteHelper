using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Wpf.Views;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace ReciteHelper.Wpf.Services;

internal sealed class StudyActivityBucket
{
    public StudyActivityTime Total { get; set; }
}

internal readonly record struct StudyActivityContext(FrameworkElement? Scope, bool CountForeground,
    bool IsStudy, StudyActivityBucket? Bucket = null);

internal sealed class StudyActivityTracker : IDisposable
{
    private const int TypingGraceSeconds = 12;
    private const int ReadingGraceSeconds = 25;
    private readonly WpfApplication _app;
    private readonly InputManager _input;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly string _path;
    private readonly Dictionary<DateOnly, StudyActivityTime> _pending = [];
    private StudyActivityContext _context;
    private double _checkpoint;
    private double _activeUntil;
    private double _lastSave;
    private DateTimeOffset _wallCheckpoint = DateTimeOffset.Now;

    public StudyActivityTracker(WpfApplication app, string? path = null)
    {
        _app = app;
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "study_activity.json");
        _input = InputManager.Current;
        _input.PostProcessInput += OnInput;
        _app.Activated += OnActivated;
        _app.Deactivated += OnDeactivated;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
        Checkpoint();
    }

    public void Checkpoint() => Advance(ResolveContext());

    public IReadOnlyDictionary<DateOnly, StudyActivityTime> Snapshot()
    {
        Checkpoint();
        Dictionary<DateOnly, StudyActivityTime> days;
        try { days = ReadFile(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Trace.WriteLine(ex);
            days = [];
        }
        MergeInto(days);
        return days;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Checkpoint();
        if (_checkpoint - _lastSave >= 30) Flush();
    }

    private void OnActivated(object? sender, EventArgs e) => Checkpoint();

    private void OnDeactivated(object? sender, EventArgs e)
    {
        Advance(default);
        Flush();
    }

    private void Advance(StudyActivityContext next)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var wallTime = DateTimeOffset.Now;
        var seconds = now - _checkpoint;
        if (_context.CountForeground)
        {
            var sample = StudyActivityTime.FromInterval(seconds, _activeUntil - _checkpoint);
            if (sample.ForegroundSeconds > 0)
            {
                AddToDays(sample, _wallCheckpoint, wallTime);
                if (_context.Bucket is { } bucket) bucket.Total += sample;
            }
        }

        if (_context != next || seconds > 5) _activeUntil = now;
        _context = next;
        _checkpoint = now;
        _wallCheckpoint = wallTime;
    }

    private void AddToDays(StudyActivityTime sample, DateTimeOffset start, DateTimeOffset end)
    {
        var startDate = DateOnly.FromDateTime(start.DateTime);
        var endDate = DateOnly.FromDateTime(end.DateTime);
        if (startDate == endDate)
        {
            Add(startDate, sample);
            return;
        }

        var beforeMidnight = Math.Clamp(sample.ForegroundSeconds - end.TimeOfDay.TotalSeconds, 0, sample.ForegroundSeconds);
        var activeBefore = Math.Min(beforeMidnight, sample.ActiveSeconds);
        Add(startDate, new(beforeMidnight, activeBefore));
        Add(endDate, new(sample.ForegroundSeconds - beforeMidnight, sample.ActiveSeconds - activeBefore));
    }

    private void Add(DateOnly date, StudyActivityTime sample) => _pending[date] = _pending.GetValueOrDefault(date) + sample;

    private StudyActivityContext ResolveContext()
    {
        var foreground = _app.Windows.OfType<Window>().FirstOrDefault(window =>
            window.IsActive && window.IsVisible && window.WindowState != WindowState.Minimized);
        return foreground switch
        {
            MainWindow main => main.ActivityContext,
            ExamWindow exam => exam.ActivityContext,
            { Content: QuizView quiz } => quiz.ActivityContext,
            { Content: FrameworkElement element } when IsStudyTool(element) => new(element, true, true),
            { } window => new(window, true, false),
            _ => default
        };
    }

    private void OnInput(object sender, ProcessInputEventArgs e)
    {
        var input = e.StagingItem.Input;
        var routed = input.RoutedEvent;
        if (routed != TextCompositionManager.TextInputEvent && routed != Keyboard.KeyDownEvent &&
            routed != Mouse.MouseWheelEvent && routed != Mouse.MouseUpEvent) return;

        Checkpoint();
        if (!_context.CountForeground) return;
        var ancestors = Ancestors((input.OriginalSource ?? input.Source ?? input.Device?.Target) as DependencyObject).ToArray();
        var inStudy = ancestors.Any(IsStudyTool) || _context.IsStudy && ancestors.Contains(_context.Scope);
        if (!inStudy) return;
        var inEditor = ancestors.Any(element => element is TextBoxBase or PasswordBox);
        var grace = input switch
        {
            TextCompositionEventArgs { Text.Length: > 0 } when inEditor => TypingGraceSeconds,
            KeyEventArgs { IsRepeat: false } key when inEditor && IsEditingKey(key.Key) => TypingGraceSeconds,
            KeyEventArgs { IsRepeat: false, Key: Key.PageUp or Key.PageDown or Key.Up or Key.Down } when !inEditor => ReadingGraceSeconds,
            MouseWheelEventArgs { Delta: not 0 } => ReadingGraceSeconds,
            MouseButtonEventArgs { ChangedButton: MouseButton.Left } when !inEditor => ReadingGraceSeconds,
            _ => 0
        };
        if (grace > 0) _activeUntil = Math.Max(_activeUntil, _checkpoint + grace);
    }

    private static bool IsEditingKey(Key key) => key is Key.Back or Key.Delete or Key.ImeProcessed ||
        Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && key is Key.V or Key.X or Key.Z or Key.Y;

    internal static bool IsStudyTool(DependencyObject element)
        => element is ProjectNotesView or QuestionHelpView or ReviewMistakesView or KnowledgePointView;

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject? element)
    {
        while (element is not null)
        {
            yield return element;
            element = element switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(element),
                FrameworkContentElement content => content.Parent ?? ContentOperations.GetParent(content),
                _ => LogicalTreeHelper.GetParent(element)
            };
        }
    }

    private Dictionary<DateOnly, StudyActivityTime> ReadFile()
    {
        if (!File.Exists(_path)) return [];
        using var stream = File.Open(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return JsonSerializer.Deserialize<Dictionary<DateOnly, StudyActivityTime>>(stream) ?? [];
    }

    private void MergeInto(Dictionary<DateOnly, StudyActivityTime> days)
    {
        foreach (var (date, sample) in _pending)
            days[date] = days.GetValueOrDefault(date) + sample;
    }

    private void Flush()
    {
        _lastSave = _checkpoint;
        if (_pending.Count == 0) return;
        var temporary = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using var gate = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose);
            var days = ReadFile();
            MergeInto(days);
            try
            {
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(days));
                File.Move(temporary, _path, overwrite: true);
            }
            finally { File.Delete(temporary); }
            _pending.Clear();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Trace.WriteLine(ex);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _input.PostProcessInput -= OnInput;
        _app.Activated -= OnActivated;
        _app.Deactivated -= OnDeactivated;
        Advance(default);
        Flush();
    }
}
