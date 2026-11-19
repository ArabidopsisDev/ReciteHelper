using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Wpf.Controls;

namespace ReciteHelper.Wpf.Views;

public partial class ProjectNotesView : UserControl, IWorkspacePaneLayout
{
    private readonly IProjectFileService _files;
    private readonly DispatcherTimer _saveTimer;
    private int _revision;
    private int _savedRevision;
    private Task? _saveTask;
    private bool _horizontal;

    internal Project Project { get; }
    internal bool HasCloseWork => _revision != _savedRevision;

    internal ProjectNotesView(Project project, IProjectFileService files)
    {
        InitializeComponent();
        Project = project;
        _files = files;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += async (_, _) => await SavePendingAsync();
        MergeQuestionNotes();
        NotesTextBox.Text = project.ReviewNote ?? string.Empty;
        NotesTextBox.TextChanged += NotesTextChanged;

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

    internal void Release() => _saveTimer.Stop();

    public void SetHorizontalLayout(bool horizontal)
    {
        if (_horizontal == horizontal) return;
        _horizontal = horizontal;
        Grid.SetColumn(NotesTextBox, horizontal ? 1 : 0);
        Grid.SetColumnSpan(NotesTextBox, horizontal ? 1 : 2);
        Grid.SetRow(SaveStatus, horizontal ? 0 : 1);
        Grid.SetColumnSpan(SaveStatus, horizontal ? 1 : 2);
        SaveStatus.Margin = horizontal ? new Thickness(0, 2, 13, 0) : new Thickness(0, 10, 0, 0);
        SaveStatus.MaxWidth = horizontal ? 94 : double.PositiveInfinity;
    }
}
