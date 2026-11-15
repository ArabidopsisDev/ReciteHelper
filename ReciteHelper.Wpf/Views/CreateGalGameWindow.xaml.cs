using Microsoft.Win32;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Wpf.Models;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ReciteHelper.Wpf.Views;

public partial class CreateGalGameWindow : Window, INotifyPropertyChanged
{
    private readonly IProjectFileService _projectFileService;
    private readonly IGalGameCreationService _galGameCreationService;

    public CreateGalGameWindow(
        IProjectFileService projectFileService,
        IGalGameCreationService galGameCreationService)
    {
        _projectFileService = projectFileService;
        _galGameCreationService = galGameCreationService;

        InitializeComponent();
        DataContext = this;
    }

    public string SelectedFilePath
    {
        get => field;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "复习项目文件 (*.rhproj)|*.rhproj|所有文件 (*.*)|*.*",
            Title = "选择复习项目文件",
            Multiselect = false,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            CheckFileExists = true,
            CheckPathExists = true,
        };

        if (openFileDialog.ShowDialog() == true)
        {
            SelectedFilePath = openFileDialog.FileName;

            if (!Path.GetExtension(SelectedFilePath).Equals(".rhproj", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("请选择 .rhproj 格式的文件", "文件格式错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                SelectedFilePath = null;
            }
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            MessageBox.Show("请先选择复习项目文件", "未选择文件",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_projectFileService.ProjectExists(SelectedFilePath))
        {
            MessageBox.Show("选择的文件不存在，请重新选择", "文件不存在",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!HasTextGenerationAccess())
        {
            MessageBox.Show("尚未配置可用的文本生成服务，无法创建游戏项目。请先完成模型账号授权（OAuth）、API Key 或托管服务配置。", "尚未配置模型服务",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"确定要使用 {Path.GetFileName(SelectedFilePath)} 创建 GalGame 吗？\n\n" +
            "此过程可能需要一些时间，请耐心等待...",
            "确认创建",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _galGameCreationService.CreateAsync(SelectedFilePath, Config.Configure.DeepSeekKey ?? string.Empty);
            MessageBox.Show("游戏文件创建成功，您可以在章节界面的菜单中加载了", "创建成功", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"游戏文件创建失败：{ex.Message}", "创建失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool HasTextGenerationAccess()
    {
        return ReciteHelper.Core.Configuration.ModelAccess.HasTextGeneration(Config.Configure);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
