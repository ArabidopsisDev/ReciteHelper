using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using ReciteHelper.Models;
using System.IO;
using System.Windows;

namespace ReciteHelper;

public partial class CreateProjectWindow : Window
{
    public string? ProjectName { get; private set; }
    public string? StoragePath { get; private set; }
    public string? QuestionBankPath { get; private set; }
    public string? FullProjectPath { get; private set; }

    public CreateProjectWindow()
    {
        InitializeComponent();

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string defaultPath = Path.Combine(documentsPath, "ReciteHelper", "Projects");
        StoragePathTextBox.Text = defaultPath;

        UpdatePreview();
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
            Filter = "PDF文件 (*.pdf)|*.pdf",
            Title = "选择题库PDF文件"
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
        bool isValid = true;

        // Verify project name
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

        // Verify storage path
        if (string.IsNullOrWhiteSpace(StoragePathTextBox.Text))
        {
            ShowValidationError(StoragePathValidation, "请选择存储路径");
            isValid = false;
        }
        else if (!Directory.Exists(StoragePathTextBox.Text))
        {
            try
            {
                // Check if the directory can be created
                Directory.CreateDirectory(StoragePathTextBox.Text);
            }
            catch
            {
                ShowValidationError(StoragePathValidation, "无法访问或创建该路径");
                isValid = false;
            }
        }
        else
        {
            HideValidationError(StoragePathValidation);
        }

        // Verify the question bank file
        if (string.IsNullOrWhiteSpace(QuestionBankTextBox.Text))
        {
            ShowValidationError(QuestionBankValidation, "请选择题库PDF文件");
            isValid = false;
        }
        else if (!File.Exists(QuestionBankTextBox.Text))
        {
            ShowValidationError(QuestionBankValidation, "题库文件不存在");
            isValid = false;
        }
        else if (Path.GetExtension(QuestionBankTextBox.Text).ToLower() != ".pdf")
        {
            ShowValidationError(QuestionBankValidation, "请选择PDF文件");
            isValid = false;
        }
        else
        {
            HideValidationError(QuestionBankValidation);
        }

        // Check if the project already exists
        if (isValid)
        {
            string projectFileName = ProjectNameTextBox.Text.Trim() + ".rhproj";
            string fullPath = Path.Combine(StoragePathTextBox.Text, ProjectNameTextBox.Text.Trim(), projectFileName);

            if (File.Exists(fullPath))
            {
                ShowValidationError(ProjectNameValidation, "该项目已存在");
                isValid = false;
            }
        }

        ConfirmButton.IsEnabled = isValid;
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
        if (!string.IsNullOrWhiteSpace(ProjectNameTextBox.Text) && !string.IsNullOrWhiteSpace(StoragePathTextBox.Text))
        {
            string projectDir = Path.Combine(StoragePathTextBox.Text, ProjectNameTextBox.Text.Trim());
            string projectFile = Path.Combine(projectDir, ProjectNameTextBox.Text.Trim() + ".rhproj");

            ProjectPathPreview.Text = $"项目文件: {projectFile}";
        }
        else
        {
            ProjectPathPreview.Text = "项目文件: 请填写完整信息";
        }

        if (!string.IsNullOrWhiteSpace(QuestionBankTextBox.Text))
        {
            string fileName = Path.GetFileName(QuestionBankTextBox.Text);
            QuestionBankPreview.Text = $"题库文件: {fileName}";
        }
        else
        {
            QuestionBankPreview.Text = "题库文件: 未选择";
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmButton.IsEnabled) return;

        try
        {
            ProjectName = ProjectNameTextBox.Text.Trim();
            StoragePath = StoragePathTextBox.Text;
            QuestionBankPath = QuestionBankTextBox.Text;

            // Create project directory
            string projectDir = Path.Combine(StoragePath, ProjectName);
            if (!Directory.Exists(projectDir))
            {
                Directory.CreateDirectory(projectDir);
            }

            // Copy the question bank files to the project directory
            string destQuestionBankPath = Path.Combine(projectDir, Path.GetFileName(QuestionBankPath));
            File.Copy(QuestionBankPath, destQuestionBankPath, true);

            // Create project files
            FullProjectPath = Path.Combine(projectDir, ProjectName + ".rhproj");
            var project = new Project
            {
                ProjectName = ProjectName,
                QuestionBankPath = destQuestionBankPath,
                QuestionBank = null,
                StoragePath = StoragePath
            };

            string json = System.Text.Json.JsonSerializer.Serialize(project,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FullProjectPath, json);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建项目失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static string ExtractAllTextFromPdf(string filePath)
    {
        var docNetInstance = DocLib.Instance;
        var pageDimensions = new PageDimensions(1080, 1920);

        string fullText = "";

        // Start reading the document
        using (var docReader = docNetInstance.GetDocReader(filePath, pageDimensions))
        {
            int pageCount = docReader.GetPageCount();

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                using var pageReader = docReader.GetPageReader(pageIndex);
                string pageText = pageReader.GetText();
                fullText += pageText + "\n";
            }
        }

        return fullText;
    }

    private void CountButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuestionBankTextBox.Text is null)
            return;

        var length = 0d;

        try
        {
            length = ExtractAllTextFromPdf(QuestionBankTextBox.Text).Length;
        }
        catch
        {
            MessageBox.Show("价格估计失败，请确保您已经选择文件", "价格估计");
            return;
        }

        var coff = 1.25d;
        var tokens = length * 1.3d * (1d + coff);
        var price = length / 1_000_000 * 2 + length * coff / 1_000_000 * 3; 


        MessageBox.Show($"""
            texts: {length:F0}
            coefficient: {coff:F2}
            tokens(pred tot.): {tokens:F0}

            预计价格: {price:F2} 元
            """, "价格预计");
    }
}