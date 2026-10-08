using Microsoft.Win32;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Wpf.Models;
using System.IO;
using System.Windows;

namespace ReciteHelper.Wpf.Views;

public partial class CreateProjectWindow : Window
{
    private readonly Action<string, string> _updateRecentProject;
    private readonly IProjectCreationService _projectCreationService;
    private readonly IQuestionBankTextService _questionBankTextService;

    public CreateProjectWindow(
        Action<string, string> updateRecentProject,
        IProjectCreationService projectCreationService,
        IQuestionBankTextService questionBankTextService)
    {
        _updateRecentProject = updateRecentProject;
        _projectCreationService = projectCreationService;
        _questionBankTextService = questionBankTextService;

        InitializeComponent();
        UpdatePreview();

        StoragePathTextBox.Text = @"D:\";
    }

    private void BrowseStoragePathButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();

        if (dialog.ShowDialog() == true)
        {
            StoragePathTextBox.Text = dialog.FolderName;
            ValidateInputs();
            UpdatePreview();
        }
    }

    private void BrowseQuestionBankButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "学习资料 (*.pdf;*.meg)|*.pdf;*.meg|PDF文件 (*.pdf)|*.pdf|合并文件 (*.meg)|*.meg",
            Title = "添加学习资料",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            QuestionBankTextBox.Text = openFileDialog.FileName;
            ValidateInputs();
            UpdatePreview();
        }
    }

    private void ProjectNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ValidateInputs();
        UpdatePreview();
    }

    private void ValidateInputs()
    {
        var isValid = true;

        if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text))
        {
            ShowValidationError(ProjectNameValidation, "项目名称不能为空");
            isValid = false;
        }
        else if (ProjectNameTextBox.Text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowValidationError(ProjectNameValidation, "项目名称包含无效字符");
            isValid = false;
        }
        else
        {
            HideValidationError(ProjectNameValidation);
        }

        if (string.IsNullOrWhiteSpace(StoragePathTextBox.Text))
        {
            ShowValidationError(StoragePathValidation, "请选择存储路径");
            isValid = false;
        }
        else if (!Directory.Exists(StoragePathTextBox.Text))
        {
            ShowValidationError(StoragePathValidation, "无法访问该路径");
            isValid = false;
        }
        else
        {
            HideValidationError(StoragePathValidation);
        }

        if (string.IsNullOrWhiteSpace(QuestionBankTextBox.Text))
        {
            ShowValidationError(QuestionBankValidation, "请选择题库文件");
            isValid = false;
        }
        else if (!ValidateQuestionBanks())
        {
            isValid = false;
        }
        else
        {
            HideValidationError(QuestionBankValidation);
        }

        if (isValid)
        {
            var projectName = ProjectNameTextBox.Text.Trim();
            var projectPath = Path.Combine(StoragePathTextBox.Text, projectName, $"{projectName}.rhproj");

            if (File.Exists(projectPath))
            {
                ShowValidationError(ProjectNameValidation, "该项目已存在");
                isValid = false;
            }
        }

        ConfirmButton.IsEnabled = isValid;
    }

    private bool ValidateQuestionBanks()
    {
        foreach (var file in GetQuestionBankPaths())
        {
            if (!File.Exists(file))
            {
                ShowValidationError(QuestionBankValidation, "题库文件不存在");
                return false;
            }

            var extension = Path.GetExtension(file);
            if (!extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".meg", StringComparison.OrdinalIgnoreCase))
            {
                ShowValidationError(QuestionBankValidation, "请选择 PDF 或 MEG 学习资料文件");
                return false;
            }
        }

        return true;
    }

    private void ShowValidationError(System.Windows.Controls.TextBlock validationTextBlock, string message)
    {
        validationTextBlock.Text = message;
        validationTextBlock.Visibility = Visibility.Visible;
    }

    private void HideValidationError(System.Windows.Controls.TextBlock validationTextBlock)
    {
        validationTextBlock.Visibility = Visibility.Collapsed;
    }

    private void UpdatePreview()
    {
        if (!string.IsNullOrWhiteSpace(ProjectNameTextBox.Text) &&
            !string.IsNullOrWhiteSpace(StoragePathTextBox.Text))
        {
            var projectDir = Path.Combine(StoragePathTextBox.Text, ProjectNameTextBox.Text.Trim());
            var projectFile = Path.Combine(projectDir, ProjectNameTextBox.Text.Trim() + ".rhproj");

            ProjectPathPreview.Text = $"项目文件: {projectFile}";
        }
        else
        {
            ProjectPathPreview.Text = "项目文件: 请填写完整信息";
        }

        var questionBankPaths = GetQuestionBankPaths();
        QuestionBankPreview.Text = questionBankPaths.Count > 0
            ? $"学习资料: {Path.GetFileName(questionBankPaths[0])}"
            : "学习资料: 未选择";
    }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmButton.IsEnabled)
            return;

        var questionBankPaths = GetQuestionBankPaths();
        if (!HasTextGenerationAccess())
        {
            MessageBox.Show("创建项目需要可用的文本生成服务。请通过模型账号授权（OAuth）、API Key 或激活码完成模型服务配置。", "尚未配置模型服务",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var progressWindow = new ProgressWindow
        {
            Owner = this
        };
        var progress = new Progress<ProjectCreationProgress>(value => UpdateProgress(progressWindow, value));
        var succeeded = false;

        try
        {
            ConfirmButton.IsEnabled = false;
            progressWindow.Show();

            var request = new CreateProjectRequest(
                ProjectNameTextBox.Text.Trim(),
                StoragePathTextBox.Text,
                questionBankPaths,
                Config.Configure?.DeepSeekKey ?? string.Empty,
                Config.Configure?.Strategy ?? ReciteHelper.Core.Enums.MissingStrategy.Ignore);

            var result = await _projectCreationService.CreateAsync(request, progress);

            MessageBox.Show("项目创建成功");
            _updateRecentProject(result.ProjectPath, result.Project.ProjectName!);

            succeeded = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建项目失败：{ex.Message}", "创建失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (progressWindow.IsVisible)
                progressWindow.Close();

            if (!succeeded)
                ConfirmButton.IsEnabled = true;
        }
    }

    private static bool HasTextGenerationAccess()
    {
        return ReciteHelper.Core.Configuration.ModelAccess.HasTextGeneration(Config.Configure);
    }

    private static void UpdateProgress(ProgressWindow progressWindow, ProjectCreationProgress progress)
    {
        progressWindow.ApplyProgress(progress);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void CountButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(QuestionBankTextBox.Text))
            return;

        double length;

        try
        {
            length = (await _questionBankTextService.ExtractAsync(GetQuestionBankPaths())).Length;
        }
        catch
        {
            MessageBox.Show("估计失败，请确保您已经选择文件", "价格估计");
            return;
        }

        var coefficient = 1.25d;
        var tokens = length * 1.3d * (1d + coefficient);
        var price = length / 1_000_000 * 2.5 + length * coefficient / 1_000_000 * 3 * 2.10d;

        var addition = string.Empty;

        if (DateTime.Now.Hour is > 9 and < 12 or > 14 and < 18)
        {
            addition = $"""
            
            -------------- 
            如果您选择等到非峰时时段再创建项目，预计可节省：{price:F2} 元。
            --------------
            峰时时段：9:00～12:00 和 14:00～18:00
            """;
            price *= 2;
        }

        var msg = $"""
                   texts: {length:F0}
                   coefficient: {coefficient:F2}
                   tokens(pred tot.): {tokens:F0}

                   预计价格: {price:F2} 元
                   {addition}
                   """;

        MessageBox.Show(msg, "价格预计");
    }

    private List<string> GetQuestionBankPaths()
    {
        return QuestionBankTextBox.Text
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
