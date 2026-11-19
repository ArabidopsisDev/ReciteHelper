using Microsoft.Win32;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Infrastructure.Services;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Views;
using System.Windows;
using System.Windows.Controls;

namespace ReciteHelper.Wpf;

public partial class MainWindow : Window
{
    private readonly IProjectFileService _projectFileService;
    private readonly IProjectCreationService _projectCreationService;
    private readonly IQuestionBankTextService _questionBankTextService;
    private readonly IStartupCompatibilityService _startupCompatibilityService;
    private readonly IQuizService _quizService;
    private readonly IRecentProjectService _recentProjectService;
    private readonly IReviewGenerator _reviewGenerator;
    private readonly IReviewPersonalizationService _reviewPersonalizationService;
    private readonly IExamAnswerService _examAnswerService;
    private readonly IExamPaperService _examPaperService;
    private readonly IExamSettingsService _examSettingsService;
    private readonly IExamSetImportService _examSetImportService;
    private readonly IExamSetRepository _examSetRepository;
    private readonly IFileMergeService _fileMergeService;
    private readonly IQuestionHelpService _questionHelpService;
    private readonly IConfigService _configService;
    private readonly IPiModelService _piModelService;
    private readonly HostedModelService _hostedModelService;
    private readonly IRuleReviewService _ruleReviewService;
    private List<RecentProject> recentProjects = new();

    public MainWindow(
        IProjectFileService projectFileService,
        IProjectCreationService projectCreationService,
        IQuestionBankTextService questionBankTextService,
        IStartupCompatibilityService startupCompatibilityService,
        IQuizService quizService,
        IRecentProjectService recentProjectService,
        IReviewGenerator reviewGenerator,
        IExamAnswerService examAnswerService,
        IExamPaperService examPaperService,
        IExamSettingsService examSettingsService,
        IExamSetImportService examSetImportService,
        IExamSetRepository examSetRepository,
        IFileMergeService fileMergeService,
        IQuestionHelpService questionHelpService,
        IReviewPersonalizationService reviewPersonalizationService,
        IConfigService configService,
        IPiModelService piModelService,
        HostedModelService hostedModelService,
        IRuleReviewService ruleReviewService)
    {
        _reviewPersonalizationService = reviewPersonalizationService;
        _projectFileService = projectFileService;
        _projectCreationService = projectCreationService;
        _questionBankTextService = questionBankTextService;
        _startupCompatibilityService = startupCompatibilityService;
        _quizService = quizService;
        _recentProjectService = recentProjectService;
        _reviewGenerator = reviewGenerator;
        _examAnswerService = examAnswerService;
        _examPaperService = examPaperService;
        _examSettingsService = examSettingsService;
        _examSetImportService = examSetImportService;
        _examSetRepository = examSetRepository;
        _fileMergeService = fileMergeService;
        _questionHelpService = questionHelpService;
        _configService = configService;
        _piModelService = piModelService;
        _hostedModelService = hostedModelService;
        _ruleReviewService = ruleReviewService;

        _startupCompatibilityService.Initialize();

        InitializeComponent();
        LoadSlogan();
        if (Random.Shared.Next(100) == 0) ShowAiHomeEasterEgg();
        Loaded += async (_, _) => await LoadRecentProjectsAsync();
    }

    private void LoadSlogan()
    {
        SloganLabel.Content = "Arabidopsis\n学习记录保存在项目中。";
    }

    private async Task LoadRecentProjectsAsync()
    {
        try
        {
            recentProjects = (await _recentProjectService.LoadAsync()).ToList();
            PopulateRecentProjectsUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载最近项目失败: {ex.Message}");
            recentProjects = new();
        }
    }

    private void PopulateRecentProjectsUI()
    {
        RecentProjectsPanel.Children.Clear();
        recentProjects.Sort((x, y) => y.LastAccessed.CompareTo(x.LastAccessed));

        if (recentProjects.Count == 0)
        {
            RecentProjectsPanel.Children.Add(new TextBlock
            {
                Text = "暂无最近项目。创建或打开项目后，它会显示在这里。",
                Style = (Style)FindResource("SubtitleTextStyle"),
                Margin = new Thickness(12, 12, 24, 0)
            });
        }

        foreach (var project in recentProjects)
            AddRecentProjectToUI(project.ProjectName, project.ProjectPath);
    }

    private void AddRecentProjectToUI(string? projectName, string? projectPath)
    {
        var button = new Button
        {
            Style = (Style)FindResource("RecentItemStyle"),
            Tag = projectPath,
            ToolTip = projectPath
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var image = new System.Windows.Shapes.Path
        {
            Data = (System.Windows.Media.Geometry)FindResource("ArcFolderIcon"),
            Style = (Style)FindResource("ArcLineIcon"),
            Margin = new Thickness(0, 0, 14, 0)
        };
        Grid.SetColumn(image, 0);

        var stackPanel = new StackPanel();
        Grid.SetColumn(stackPanel, 1);

        var nameTextBlock = new TextBlock
        {
            Text = projectName,
            FontWeight = FontWeights.Medium,
            Foreground = (System.Windows.Media.Brush)FindResource("ArcForeground"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var pathTextBlock = new TextBlock
        {
            Text = projectPath,
            Style = (Style)FindResource("CaptionTextStyle"),
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("ArcTextSecondary"),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        stackPanel.Children.Add(nameTextBlock);
        stackPanel.Children.Add(pathTextBlock);

        grid.Children.Add(image);
        grid.Children.Add(stackPanel);
        var chevron = new System.Windows.Shapes.Path
        {
            Data = (System.Windows.Media.Geometry)FindResource("ArcChevronIcon"),
            Style = (Style)FindResource("ArcLineIcon"),
            Width = 7, Height = 12, Margin = new Thickness(12, 0, 4, 0)
        };
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);

        button.Content = grid;
        button.Click += RecentProject_Click;

        RecentProjectsPanel.Children.Add(button);
    }

    public async Task AddRecentProjectAsync(string projectPath, string? projectName = null)
    {
        try
        {
            recentProjects = (await _recentProjectService.AddOrUpdateAsync(projectPath, projectName)).ToList();
            PopulateRecentProjectsUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存最近项目失败: {ex.Message}");
        }
    }

    private async void RecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string projectPath })

            await OpenProjectAsync(projectPath);


    }

    private void CreateNewProject_Click(object sender, RoutedEventArgs e)
    {
        var select = new ProjectTypeSelectionWindow();
        var dialogResult = select.ShowDialog();
        var type = new ProjectType();
        bool? result = false;

        if (dialogResult == true)
            type = select.SelectedProjectType;
        else
            return;

        if (type.TemplateType == ProjectTemplateType.RuleBasedReview)
        {
            result = new CreateRuleReviewWindow(_ruleReviewService, _projectFileService,
                project => AddRecentProjectAsync(System.IO.Path.Combine(project.StoragePath!, project.ProjectName!, $"{project.ProjectName}.rhproj"), project.ProjectName)) { Owner = this }.ShowDialog();
        }
        else if (type.TemplateType == ProjectTemplateType.ClassicalReview)
        {
            result = new CreateProjectWindow(CatchProjectAsync, _projectCreationService, _questionBankTextService)
            {
                Owner = this
            }.ShowDialog();
        }
        else if (type.TemplateType == ProjectTemplateType.PDFMerge)
        {
            new FileMergeWindow(_fileMergeService).Show();
            result = true;
        }
        else
        {
            MessageBox.Show("该项目类型暂不可用", "无法创建", MessageBoxButton.OK, MessageBoxImage.Information);
            result = true;
        }

        if (result == false)
            MessageBox.Show("已放弃创建项目", "放弃创建", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "ReciteHelper项目文件 (*.rhproj)|*.rhproj",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            var projectPath = openFileDialog.FileName;
            await OpenProjectAsync(projectPath);
            await AddRecentProjectAsync(projectPath);
        }
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 ReciteHelper 项目包",
            Filter = "ReciteHelper项目包 (*.rhp)|*.rhp",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        var folderDialog = new OpenFolderDialog
        {
            Title = "选择项目导入后的存放目录"
        };

        if (folderDialog.ShowDialog() != true)
            return;

        try
        {
            var importedProject = await _projectFileService.ImportProjectArchiveAsync(
                dialog.FileName,
                folderDialog.FolderName);
            await AddRecentProjectAsync(importedProject.ProjectPath, importedProject.ProjectName);

            MessageBox.Show(
                $"项目导入成功：\n{importedProject.ProjectPath}",
                "导入成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"项目包无法导入。\n详细信息：{ex.Message}",
                "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResourceCenter_Click(object sender, RoutedEventArgs e)
    {
        var window = new ResourceCenterWindow(
            Config.Configure.ResourceCenterServerUrl,
            _projectFileService,
            AddRecentProjectAsync)
        {
            Owner = this
        };
        window.Show();
    }

    private async void CatchProjectAsync(string path, string name)
    {
        await AddRecentProjectAsync(path, name);
    }

    private async Task OpenProjectAsync(string projectPath)
    {
        projectPath = System.IO.Path.GetFullPath(projectPath);
        if (_projectTabs.TryGetValue(projectPath, out var openTab))
        {
            SelectTab(openTab);
            return;
        }

        if (!_projectFileService.ProjectExists(projectPath))
        {
            MessageBox.Show($"项目文件不存在: {projectPath}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (!_openingProjects.Add(projectPath)) return;

        try
        {
            var project = await _projectFileService.OpenProjectAsync(projectPath);
            if (project is null)
                return;

            var view = new ProjectView(
                project,
                _projectFileService,
                _examAnswerService,
                _examPaperService,
                _examSettingsService,
                _examSetImportService,
                _examSetRepository,
                _projectCreationService,
                _ruleReviewService);
            view.HomeRequested += (_, _) => SelectTab(HomeTab);
            view.KnowledgeRequested += (_, _) => OpenKnowledgeTab(project);
            view.ChapterPracticeRequested += (_, chapter) => OpenPracticeTab(view, chapter);
            view.SmartPracticeRequested += (_, _) => OpenPracticeTab(view, null);
            view.KnowledgeSourcesChanged += (_, _) =>
            {
                if (_knowledgeTabs.TryGetValue(project, out var knowledgeTab) && knowledgeTab.Content is KnowledgePointView knowledge)
                    knowledge.RefreshSources();
            };
            var tab = CreateTab(project.ProjectName ?? System.IO.Path.GetFileNameWithoutExtension(projectPath), view, projectPath);
            _projectTabs.Add(projectPath, tab);
            SelectTab(tab);

            PopulateRecentProjectsUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开项目失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _openingProjects.Remove(projectPath); }
    }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_savingWorkspace || WorkspaceTabs.Items.OfType<TabItem>().Any(IsBusyTab))
        {
            e.Cancel = true;
            return;
        }
        var practiceViews = PracticeTabs().Select(tab => (QuizView)tab.Content).ToArray();
        var notebooks = _projectNotes.Values.ToArray();
        if (practiceViews.Any(view => view.HasCloseWork) || notebooks.Any(notebook => notebook.HasCloseWork))
        {
            e.Cancel = true;
            if (await SaveWorkspaceAsync(practiceViews, notebooks))
                _ = Dispatcher.BeginInvoke(new Action(Close));
            return;
        }
        foreach (var view in practiceViews) view.Release();
        recentProjects = (await _recentProjectService.RemoveMissingAsync()).ToList();
    }
}
