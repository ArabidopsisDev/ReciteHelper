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
        HostedModelService hostedModelService)
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

        _startupCompatibilityService.Initialize();

        InitializeComponent();
        LoadSlogan();
        Loaded += async (_, _) => await LoadRecentProjectsAsync();
    }

    private void LoadSlogan()
    {
        List<string> slogan = ["你一定会坚持到底的", "常回家看看", "我有卡SPFA症",
            "向上的路没有同伴", "咕咕，咕咕，咕咕咕！", "坚持融入日常、抓在经常",
            "我真的是一个很坏的雪莉吗", "你好多宝宝，你开幼儿园算了", "对的对的对的，哦不对！",
            "Vive la France", "\\o/ \\o/ \\o/ \\o/ \\o/", "二楼一定要盖在一楼上"];
        SloganLabel.Content = slogan[Random.Shared.Next(0, slogan.Count)];

        if (DateTime.Now.Hour > 14 && Random.Shared.Next(1, 10) > 8)
            SloganLabel.Content = "哇塞，睡得跟猪头一样";
        if (DateTime.Now.Hour > 14 && Random.Shared.Next(1, 10) > 8)
            SloganLabel.Content = "每天睡得屁股都挪不动了吧";
        if (DateTime.Now.Hour > 10 && Random.Shared.Next(1, 10) > 8)
            SloganLabel.Content = "又不学习，你去spa";
        if (DateTime.Now.Hour > 10 && Random.Shared.Next(1, 10) > 8)
            SloganLabel.Content = "别躺在床上刷手机啃苹果了";
        if (Random.Shared.Next(1, 10) > 8)
            SloganLabel.Content = "我读到生词怎么办，跳过";
        if (Random.Shared.Next(1, 10) > 8)
            SloganLabel.Content = "每天都在屋子里面滑狗";
    }

    private async void ModelSettings_Click(object sender, RoutedEventArgs e)
    {
        var window = new ActivationWindow(_hostedModelService, _configService, _piModelService) { Owner = this };
        if (window.ShowDialog() == true) Config.Use(await _configService.LoadAsync());
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

        foreach (var project in recentProjects)
            AddRecentProjectToUI(project.ProjectName, project.ProjectPath);
    }

    private void AddRecentProjectToUI(string? projectName, string? projectPath)
    {
        var button = new Button
        {
            Style = (Style)FindResource("RecentItemStyle"),
            Tag = projectPath
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var image = new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/ReciteHelper.Wpf;component/Images/project.png")),
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetColumn(image, 0);

        var stackPanel = new StackPanel();
        Grid.SetColumn(stackPanel, 1);

        var nameTextBlock = new TextBlock
        {
            Text = projectName,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.Black,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var pathTextBlock = new TextBlock
        {
            Text = projectPath,
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        stackPanel.Children.Add(nameTextBlock);
        stackPanel.Children.Add(pathTextBlock);

        grid.Children.Add(image);
        grid.Children.Add(stackPanel);

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

        if (type.TemplateType == ProjectTemplateType.ClassicalReview)
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
        if (!_projectFileService.ProjectExists(projectPath))
        {
            MessageBox.Show($"项目文件不存在: {projectPath}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            var project = await _projectFileService.OpenProjectAsync(projectPath);
            if (project is null)
                return;

            var quizWindow = new SelectChapterWindow(
                project,
                _projectFileService,
                _quizService,
                _reviewGenerator,
                _examAnswerService,
                _examPaperService,
                _examSettingsService,
                _examSetImportService,
                _examSetRepository,
                _projectCreationService,
                _questionHelpService,
                _reviewPersonalizationService);
            quizWindow.Show();

            PopulateRecentProjectsUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开项目失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        recentProjects = (await _recentProjectService.RemoveMissingAsync()).ToList();
    }
}
