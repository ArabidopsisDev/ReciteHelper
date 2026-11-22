using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Wpf.Controls;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Views;

namespace ReciteHelper.Wpf;

public partial class MainWindow
{
    private readonly Dictionary<string, TabItem> _projectTabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Project, TabItem> _knowledgeTabs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Project, ProjectNotesView> _projectNotes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Project, QuestionHelpView> _projectHelp = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Project, ReviewMistakesView> _projectMistakes = new(ReferenceEqualityComparer.Instance);
    private WorkspacePaneManager? _panes;
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
                _reviewPersonalizationService, _ruleReviewService);
            tab = CreateTab(title, view, $"{project.ProjectName ?? "项目"} / {title}");
            view.RecordsChanged += (_, _) => projectView.RefreshChapters();
            view.RecordsCleared += (_, affected) =>
            {
                foreach (var practice in PracticeTabs().Select(candidate => (QuizView)candidate.Content))
                    if (!ReferenceEquals(practice, view) && ReferenceEquals(practice.Project, project))
                        practice.RefreshRecords(affected);
            };
            view.NotesRequested += (_, _) => OpenTool(project, WorkspacePaneManager.Notes);
            view.HelpRequested += (_, _) => OpenTool(project, WorkspacePaneManager.Sources);
            view.HelpTargetChanged += (_, _) =>
            {
                if (ActiveContent == view) UpdateHelpTarget(project, view);
            };
        }
        SelectTab(tab);
    }

    private WorkspacePaneManager Panes()
    {
        if (_panes is not null) return _panes;
        WorkspaceTabs.ApplyTemplate();
        _panes = (WorkspacePaneManager)WorkspaceTabs.Template.FindName("ToolPanes", WorkspaceTabs);
        _panes.Initialize(this);
        _panes.PaneClosed += (_, key) =>
        {
            if (key == WorkspacePaneManager.Sources && ActiveProject() is { } project && _projectHelp.TryGetValue(project, out var help))
                help.SetQuestion(null, null);
        };
        return _panes;
    }

    private void OpenTool(Project project, string key)
    {
        var panes = Panes();
        EnsureTool(project, key);
        panes.ShowPane(key);
        UpdateHelpTarget(project, ActiveContent as QuizView);
    }

    private void EnsureTool(Project project, string key)
    {
        var panes = Panes();
        switch (key)
        {
            case WorkspacePaneManager.Notes when !_projectNotes.ContainsKey(project):
                var notes = new ProjectNotesView(project, _projectFileService);
                _projectNotes.Add(project, notes);
                panes.Register(project, key, "项目笔记", notes);
                break;
            case WorkspacePaneManager.Sources when !_projectHelp.ContainsKey(project):
                var help = new QuestionHelpView(project, _questionHelpService);
                _projectHelp.Add(project, help);
                panes.Register(project, key, "资料与解析", help);
                break;
            case WorkspacePaneManager.Mistakes when !_projectMistakes.ContainsKey(project):
                var mistakes = new ReviewMistakesView(project, _projectFileService, _examSetRepository);
                _projectMistakes.Add(project, mistakes);
                panes.Register(project, key, "错误列表", mistakes);
                break;
        }
    }

    private void UpdateHelpTarget(Project project, QuizView? practice)
    {
        if (_panes?.IsOpen(WorkspacePaneManager.Sources) != true || !_projectHelp.TryGetValue(project, out var help)) return;
        var target = practice?.HelpTarget;
        help.SetQuestion(target?.Question, target?.UserAnswer);
    }

    private void WorkspaceMenu_Click(object sender, RoutedEventArgs e)
    {
        var panes = Panes();
        var menu = new ContextMenu { PlacementTarget = (Button)sender };
        var project = ActiveProject();
        foreach (var (key, title) in new[] { (WorkspacePaneManager.Notes, "项目笔记"),
            (WorkspacePaneManager.Sources, "资料与解析"), (WorkspacePaneManager.Mistakes, "错误列表") })
        {
            var item = new MenuItem { Header = title, IsEnabled = project is not null,
                IsChecked = project is not null && panes.IsOpen(key) };
            item.Click += (_, _) =>
            {
                if (project is null) return;
                EnsureTool(project, key);
                panes.TogglePane(key);
                UpdateHelpTarget(project, ActiveContent as QuizView);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var reset = new MenuItem { Header = "还原面板位置" };
        reset.Click += (_, _) => panes.ResetLayout();
        menu.Items.Add(reset);
        menu.IsOpen = true;
    }

    private object? ActiveContent => (WorkspaceTabs.SelectedItem as TabItem)?.Content;

    private Project? ActiveProject() => ActiveContent switch
    {
        QuizView quiz => quiz.Project,
        ProjectView view => view.Project,
        KnowledgePointView knowledge => knowledge.Project,
        _ => null
    };

    private async Task<bool> SaveWorkspaceAsync(IEnumerable<QuizView> views, IReadOnlyList<ProjectNotesView>? notebooks = null)
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
                MessageBox.Show(this, "项目笔记尚未保存，请重试。", "保存项目笔记", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (!IsLoaded) return;
        var project = ActiveProject();
        var panes = Panes();
        panes.SetProject(project);
        foreach (var (other, help) in _projectHelp)
            if (!ReferenceEquals(other, project)) help.SetQuestion(null, null);
        if (project is null) return;
        foreach (var key in new[] { WorkspacePaneManager.Notes, WorkspacePaneManager.Sources, WorkspacePaneManager.Mistakes })
            if (panes.IsOpen(key)) EnsureTool(project, key);
        UpdateHelpTarget(project, ActiveContent as QuizView);
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

            notebook?.Release();
            _projectNotes.Remove(projectView.Project);
            if (_projectHelp.Remove(projectView.Project, out var help)) help.SetQuestion(null, null);
            _projectMistakes.Remove(projectView.Project);
            _panes?.Remove(projectView.Project);
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
        else if (tab == _studyDashboardTab)
        {
            _studyDashboardTab = null;
        }

        WorkspaceTabs.Items.Remove(tab);
    }
}
