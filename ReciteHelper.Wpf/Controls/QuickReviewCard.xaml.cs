using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Controls;

public partial class QuickReviewCard : UserControl
{
    private readonly List<QuickReviewPrompt> _ready = [];
    private readonly HashSet<Guid> _shownTopics = [];
    private QuickReviewService? _service;
    private Project? _project;
    private QuickReviewPrompt? _current;
    private Guid? _excluded;
    private bool _preparing;
    private int _waitSequence;
    private bool _waiting;
    private bool _answered;

    public QuickReviewCard()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (_project is not null) _project.PracticeArchiveChanged += ArchiveChanged; };
        Unloaded += (_, _) => { if (_project is not null) _project.PracticeArchiveChanged -= ArchiveChanged; };
    }

    internal void Attach(QuickReviewService? service, Project? project)
    {
        _service = service;
        _project = project;
    }

    internal async void Preload(Guid? currentTopic = null)
    {
        if (_preparing || _service is null || _project is null) return;
        _ready.RemoveAll(prompt => !prompt.IsAvailable);
        if (_ready.Any(prompt => prompt.Topic.ReviewIdentity != currentTopic)) return;
        var exclude = _shownTopics.Concat(_ready.Select(prompt => prompt.Topic.ReviewIdentity)).ToHashSet();
        if (currentTopic is { } id) exclude.Add(id);

        _preparing = true;
        try
        {
            await foreach (var prompt in _service.PrepareAsync(_project, exclude))
            {
                _ready.Add(prompt);
                ShowIfReady();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Quick review could not be prepared ({ex.GetType().Name}).");
        }
        finally { _preparing = false; }
    }

    internal async void Begin(Guid? excluded = null)
    {
        var sequence = ++_waitSequence;
        await Task.Delay(650);
        if (sequence != _waitSequence) return;
        _waiting = true;
        _excluded = excluded;
        ShowIfReady();
    }

    internal void Finish(bool succeeded)
    {
        _waitSequence++;
        _waiting = false;
        if (!succeeded) Hide();
        if (_current is null) return;
        GradingStatus.Text = "批改已完成";
        WaitingProgress.Visibility = Visibility.Hidden;
        DismissButton.Content = "查看批改";
        DismissButton.Style = (Style)FindResource("PrimaryButtonStyle");
    }

    private void ShowIfReady()
    {
        if (!_waiting || _current is not null || Visibility == Visibility.Visible) return;
        var prompt = _ready.Where(item => item.IsAvailable && item.Topic.ReviewIdentity != _excluded)
            .MinBy(item => item.Priority);
        if (prompt is null) return;
        _ready.Remove(prompt);
        _shownTopics.Add(prompt.Topic.ReviewIdentity);
        _current = prompt;
        _answered = false;
        GradingStatus.Text = "正在批改";
        WaitingProgress.Visibility = Visibility.Visible;
        PromptText.Text = prompt.Text;
        Choices.ItemsSource = prompt.Choices;
        Choices.IsEnabled = FillRow.IsEnabled = true;
        Choices.Visibility = prompt.Choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FillRow.Visibility = prompt.Choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BlankAnswer.Text = string.Empty;
        Feedback.Visibility = Visibility.Collapsed;
        DismissButton.Content = "跳过";
        DismissButton.Style = (Style)FindResource("SubtleButtonStyle");
        Visibility = Visibility.Visible;
        App.StudyActivity?.Checkpoint();
    }

    private void ArchiveChanged(object? sender, EventArgs e)
    {
        _ready.RemoveAll(prompt => !prompt.IsAvailable);
        if (_current is { IsAvailable: false })
            Hide();
        ShowIfReady();
    }

    private void Choice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string answer }) Check(answer);
    }

    private void Check_Click(object sender, RoutedEventArgs e) => Check(BlankAnswer.Text);

    private void BlankAnswer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Check(BlankAnswer.Text);
    }

    private void Check(string answer)
    {
        if (_current is null || _answered || string.IsNullOrWhiteSpace(answer)) return;
        _answered = true;
        Choices.IsEnabled = FillRow.IsEnabled = false;
        var correct = _current.Accepts(answer);
        Feedback.Text = correct ? "答对了。" : $"参考答案为{_current.AcceptedAnswers[0]}。";
        Feedback.Foreground = (Brush)FindResource(correct ? "ArcSuccess" : "ArcDanger");
        Feedback.Visibility = Visibility.Visible;
        if (_waiting) DismissButton.Content = "收起小题";
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        _waiting = false;
        Hide();
    }

    private void Hide()
    {
        Visibility = Visibility.Collapsed;
        _current = null;
        App.StudyActivity?.Checkpoint();
    }
}
