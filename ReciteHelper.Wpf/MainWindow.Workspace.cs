using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Views;

namespace ReciteHelper.Wpf;

public partial class MainWindow
{
    private readonly Dictionary<string, TabItem> _projectTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Project, TabItem> _knowledgeTabs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Project, ProjectNotesWindow> _projectNotes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _openingProjects = new(StringComparer.OrdinalIgnoreCase);
    private TabItem? _settingsTab;
    private bool _savingWorkspace;

    private TabItem CreateTab(string title, FrameworkElement content, string? tooltip = null)
    {
        var tab = new TabItem
        {
            Header = title,
            HeaderTemplate = (DataTemplate)FindResource("WorkspaceTabHeader"),
            ToolTip = tooltip ?? title,
            Content = content
        };
        WorkspaceTabs.Items.Add(tab);
        return tab;
    }

    private void SelectTab(TabItem tab)
    {
        WorkspaceTabs.SelectedItem = tab;
        Dispatcher.BeginInvoke(() =>
        {
            if (WorkspaceTabs.SelectedItem == tab) tab.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void ModelSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsTab is null)
        {
            var view = new ModelSettingsView(_hostedModelService, _configService, _piModelService);
            view.SettingsSaved += async (_, _) => Config.Use(await _configService.LoadAsync());
            var tab = CreateTab("模型设置", view);
            view.CloseRequested += (_, _) => CloseTab(tab);
            _settingsTab = tab;
        }
        SelectTab(_settingsTab);
    }

    private void OpenKnowledgeTab(Project project)
    {
        if (!_knowledgeTabs.TryGetValue(project, out var tab))
        {
            var view = new KnowledgePointView(project, _projectFileService);
            tab = CreateTab($"{project.ProjectName ?? "项目"} / 知识点", view);
            _knowledgeTabs.Add(project, tab);
        }
        SelectTab(tab);
    }

    private IEnumerable<TabItem> PracticeTabs()
        => WorkspaceTabs.Items.OfType<TabItem>().Where(tab => tab.Content is QuizView);

    private void OpenPracticeTab(ProjectView projectView, Chapter? chapter)
    {
        var project = projectView.Project;
        var tab = PracticeTabs().FirstOrDefault(tab => tab.Content is QuizView view &&
            ReferenceEquals(view.Project, project) && ReferenceEquals(view.Chapter, chapter));
        if (chapter is null && tab?.Content is QuizView { HasQuestions: false, HasCloseWork: false } empty)
        {
            empty.Release();
            WorkspaceTabs.Items.Remove(tab);
            tab = null;
        }
        if (tab is null)
        {
            var questions = chapter is null ? _reviewGenerator.GenerateReview(project, 30) : chapter.Questions!;
            if (questions.Count == 0)
            {
                MessageBox.Show(this, "当前没有可安排的复习题。可以先做章节练习，或稍后再试。",
                    "复习安排", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var title = chapter is null ? "智能复习" : chapter.Name ?? "章节练习";
            var view = new QuizView(project, chapter, questions, _quizService, _projectFileService,
                _questionHelpService, _reviewPersonalizationService, _ruleReviewService);
            tab = CreateTab(title, view, $"{project.ProjectName ?? "项目"} / {title}");
            view.RecordsChanged += (_, _) => projectView.RefreshChapters();
            view.RecordsCleared += (_, affected) =>
            {
                foreach (var practice in PracticeTabs().Select(candidate => (QuizView)candidate.Content))
                    if (!ReferenceEquals(practice, view) && ReferenceEquals(practice.Project, project))
                        practice.RefreshRecords(affected);
            };
            view.NotesRequested += (_, _) => ToggleProjectNotes(project);
        }
        SelectTab(tab);
    }

    private void ToggleProjectNotes(Project project)
    {
        if (!_projectNotes.TryGetValue(project, out var notebook))
        {
            notebook = new ProjectNotesWindow(this, project, _projectFileService);
            _projectNotes.Add(project, notebook);
            notebook.Closed += (_, _) => _projectNotes.Remove(project);
        }

        if (notebook.IsVisible)
            notebook.Close();
        else
            notebook.ShowBesideOwner();
    }

    private async Task<bool> SaveWorkspaceAsync(IEnumerable<QuizView> views, IReadOnlyList<ProjectNotesWindow>? notebooks = null)
    {
        notebooks ??= [];
        _savingWorkspace = true;
        WorkspaceTabs.IsEnabled = false;
        foreach (var notebook in notebooks) notebook.IsEnabled = false;
        try
        {
            foreach (var view in views)
                if (view.HasCloseWork && !await view.SaveOnCloseAsync()) return false;
            foreach (var notebook in notebooks)
            {
                if (!notebook.HasCloseWork || await notebook.SavePendingAsync()) continue;
                if (!notebook.IsVisible) notebook.ShowBesideOwner(moveOwner: false);
                notebook.Activate();
                return false;
            }
            return true;
        }
        finally
        {
            foreach (var notebook in notebooks) notebook.IsEnabled = true;
            WorkspaceTabs.IsEnabled = true;
            _savingWorkspace = false;
        }
    }

    private static bool IsBusyTab(TabItem tab)
        => tab.Content is FrameworkElement { IsEnabled: false };

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != WorkspaceTabs) return;
        if (e.RemovedItems.Count == 1 && e.RemovedItems[0] is TabItem previous &&
            WorkspaceTabs.Items.Contains(previous) && IsBusyTab(previous))
        {
            WorkspaceTabs.SelectedItem = previous;
            return;
        }
        if (e.AddedItems.Count == 1 && e.AddedItems[0] is TabItem { Content: QuizView practice } selected &&
            !IsBusyTab(selected))
            practice.RefreshRecords();

        var project = (WorkspaceTabs.SelectedItem as TabItem)?.Content switch
        {
            QuizView quiz => quiz.Project,
            ProjectView view => view.Project,
            KnowledgePointView knowledge => knowledge.Project,
            _ => null
        };
        foreach (var notebook in _projectNotes.Values)
            if (notebook.IsVisible && !ReferenceEquals(notebook.Project, project)) notebook.Hide();
    }

    private void Workspace_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.W || Keyboard.Modifiers != ModifierKeys.Control ||
            WorkspaceTabs.SelectedItem is not TabItem tab || tab == HomeTab) return;
        CloseTab(tab);
        e.Handled = true;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { TemplatedParent: TabItem tab })
            CloseTab(tab);
        e.Handled = true;
    }

    private async void CloseTab(TabItem tab)
    {
        if (tab == HomeTab || _savingWorkspace || IsBusyTab(tab)) return;

        if (tab.Content is QuizView practice)
        {
            if (practice.HasCloseWork && (WorkspaceTabs.Items.OfType<TabItem>().Any(IsBusyTab) ||
                !await SaveWorkspaceAsync([practice]))) return;
            if (WorkspaceTabs.SelectedItem == tab)
            {
                var projectTab = _projectTabs.Values.First(candidate =>
                    candidate.Content is ProjectView view && ReferenceEquals(view.Project, practice.Project));
                SelectTab(projectTab);
            }
            practice.Release();
        }

        else if (tab.Content is ProjectView projectView)
        {
            _knowledgeTabs.TryGetValue(projectView.Project, out var knowledgeTab);
            if (knowledgeTab is not null && IsBusyTab(knowledgeTab)) return;
            var practiceTabs = PracticeTabs().Where(candidate => candidate.Content is QuizView view &&
                ReferenceEquals(view.Project, projectView.Project)).ToArray();
            if (practiceTabs.Any(IsBusyTab)) return;
            var practiceViews = practiceTabs.Select(candidate => (QuizView)candidate.Content).ToArray();
            _projectNotes.TryGetValue(projectView.Project, out var notebook);
            if ((practiceViews.Any(view => view.HasCloseWork) || notebook?.HasCloseWork == true) &&
                (WorkspaceTabs.Items.OfType<TabItem>().Any(IsBusyTab) ||
                 !await SaveWorkspaceAsync(practiceViews, notebook is null ? [] : [notebook]))) return;

            notebook?.Close();
            foreach (var view in practiceViews) view.Release();
            foreach (var practiceTab in practiceTabs) WorkspaceTabs.Items.Remove(practiceTab);
            if (knowledgeTab is not null)
            {
                _knowledgeTabs.Remove(projectView.Project);
                WorkspaceTabs.Items.Remove(knowledgeTab);
            }
            var path = _projectTabs.First(pair => pair.Value == tab).Key;
            _projectTabs.Remove(path);
        }
        else if (tab.Content is KnowledgePointView knowledgeView)
        {
            _knowledgeTabs.Remove(knowledgeView.Project);
            if (WorkspaceTabs.SelectedItem == tab)
            {
                var projectTab = _projectTabs.Values.First(candidate =>
                    candidate.Content is ProjectView view && ReferenceEquals(view.Project, knowledgeView.Project));
                SelectTab(projectTab);
            }
        }
        else if (tab == _settingsTab)
        {
            _settingsTab = null;
        }

        WorkspaceTabs.Items.Remove(tab);
    }
}
