using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Wpf.Utilities;

namespace ReciteHelper.Wpf.Views;

public partial class ProjectNotesWindow : Window
{
    private readonly IProjectFileService _files;
    private readonly Window _host;
    private readonly DispatcherTimer _saveTimer;
    private readonly double _notebookWidth;
    private int _revision;
    private int _savedRevision;
    private Task? _saveTask;
    private bool _closing;

    internal Project Project { get; }
    internal bool HasCloseWork => _revision != _savedRevision;

    internal ProjectNotesWindow(Window host, Project project, IProjectFileService files)
    {
        InitializeComponent();
        _host = host;
        Owner = host;
        Project = project;
        _files = files;
        _notebookWidth = Width;
        Title = $"{project.ProjectName} / 项目笔记";

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += async (_, _) => await SavePendingAsync();
        MergeQuestionNotes();
        NotesTextBox.Text = project.ReviewNote ?? string.Empty;
        NotesTextBox.TextChanged += NotesTextChanged;

        host.LocationChanged += HostBoundsChanged;
        host.SizeChanged += HostBoundsChanged;
        host.StateChanged += HostBoundsChanged;
        host.DpiChanged += HostBoundsChanged;
    }

    private void MergeQuestionNotes()
    {
        var questions = ReviewQuestionCatalog.All(Project).Where(q => !string.IsNullOrWhiteSpace(q.ReviewNote)).ToList();
        if (questions.Count == 0) return;

        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(Project.ReviewNote))
            sections.Add(Project.ReviewNote);
        foreach (var question in questions)
        {
            sections.Add($"{question.Text}\n{question.ReviewNote}");
            question.ReviewNote = null;
        }
        Project.ReviewNote = string.Join("\n\n", sections);
        QueueSave();
    }

    private void NotesTextChanged(object sender, TextChangedEventArgs e)
    {
        Project.ReviewNote = NotesTextBox.Text.Length == 0 ? null : NotesTextBox.Text;
        QueueSave();
    }

    private void QueueSave()
    {
        _revision++;
        SaveStatus.Text = "等待保存";
        SaveStatus.ToolTip = null;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    internal async Task<bool> SavePendingAsync()
    {
        _saveTimer.Stop();
        try
        {
            while (HasCloseWork)
            {
                SaveStatus.Text = "正在保存";
                var save = _saveTask ??= SaveRevisionAsync(_revision);
                try
                {
                    await save;
                }
                finally
                {
                    if (ReferenceEquals(_saveTask, save))
                        _saveTask = null;
                }
            }
            SaveStatus.Text = "已保存";
            SaveStatus.ToolTip = null;
            return true;
        }
        catch (Exception ex)
        {
            SaveStatus.Text = "保存失败。继续编辑或关闭窗口时会重试。";
            SaveStatus.ToolTip = ex.Message;
            return false;
        }
    }

    private async Task SaveRevisionAsync(int revision)
    {
        await _files.SaveProjectAsync(Project);
        _savedRevision = revision;
    }

    internal void ShowBesideOwner(bool moveOwner = true)
    {
        if (moveOwner) MakeSpaceBesideOwner();
        new WindowInteropHelper(this).EnsureHandle();
        PlaceBesideOwner();
        Show();
        PlaceBesideOwner();
        Activate();
        NotesTextBox.Focus();
    }

    private void MakeSpaceBesideOwner()
    {
        if (_host.WindowState != WindowState.Normal || !NativeWindowBounds.TryGet(_host, out var bounds, out var work)) return;

        var width = Math.Ceiling(_notebookWidth * VisualTreeHelper.GetDpi(_host).DpiScaleX);
        var rightmostLeft = work.Right - width - bounds.Width;
        if (rightmostLeft >= work.Left && bounds.Left > rightmostLeft)
            NativeWindowBounds.Place(_host, new Rect(rightmostLeft, bounds.Top, bounds.Width, bounds.Height), resize: false);
    }

    private void HostBoundsChanged(object? sender, EventArgs e)
    {
        if (IsVisible) PlaceBesideOwner();
    }

    private void PlaceBesideOwner()
    {
        if (_host.WindowState == WindowState.Minimized || !NativeWindowBounds.TryGet(_host, out var bounds, out var work)) return;

        var width = Math.Min(Math.Ceiling(_notebookWidth * VisualTreeHelper.GetDpi(_host).DpiScaleX), work.Width);
        var height = Math.Min(bounds.Height, work.Height);
        var x = Math.Clamp(bounds.Right, work.Left, work.Right - width);
        var y = Math.Clamp(bounds.Top, work.Top, work.Bottom - height);
        NativeWindowBounds.Place(this, new Rect(x, y, width, height), resize: true);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || !HasCloseWork) return;

        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        _ = FinishClosingAsync();
    }

    private async Task FinishClosingAsync()
    {
        if (await SavePendingAsync())
        {
            _ = Dispatcher.BeginInvoke(new Action(Close));
            return;
        }
        _closing = false;
        IsEnabled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _saveTimer.Stop();
        _host.LocationChanged -= HostBoundsChanged;
        _host.SizeChanged -= HostBoundsChanged;
        _host.StateChanged -= HostBoundsChanged;
        _host.DpiChanged -= HostBoundsChanged;
        base.OnClosed(e);
    }
}
