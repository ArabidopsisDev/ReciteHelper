using Microsoft.Win32;
using ReciteHelper.Model;
using ReciteHelper.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ReciteHelper.View;

/// <summary>
/// Interaction logic for FileMergeWindow.xaml
/// </summary>
public partial class FileMergeWindow : Window, INotifyPropertyChanged
{
    private ObservableCollection<FileItem> _fileItems;
    private long _totalFileSize;

    public FileMergeWindow()
    {
        InitializeComponent();
        _fileItems = new ObservableCollection<FileItem>();
        FilesItemsControl.ItemsSource = _fileItems;

        UpdateDisplay();
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "支持的文件格式|*.docx;*.pptx;*.pdf;*.txt;*.meg|Word文档|*.docx|PowerPoint演示文稿|*.pptx|PDF文档|*.pdf|文本文件|*.txt|合并文件|*.meg|所有文件|*.*",
            Multiselect = true,
            Title = "选择要合并的文件"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            foreach (var filePath in openFileDialog.FileNames)
            {
                if (IsSupportedFileType(filePath) && !FileExistsInList(filePath))
                {
                    AddFileItem(filePath);
                }
            }

            UpdateDisplay();
        }
    }

    private bool IsSupportedFileType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLower();
        List<string> support = [".docx", ".pptx", ".pdf", ".txt", ".meg"];

        return support.Contains(extension);
    }

    private bool FileExistsInList(string filePath)
    {
        return _fileItems.Any(item => item.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
    }

    private void AddFileItem(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);

            var fileItem = new FileItem
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FileExtension = Path.GetExtension(filePath).ToUpper().TrimStart('.'),
                FileSize = FormatFileSize(fileInfo.Length),
                LastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                FileSizeBytes = fileInfo.Length
            };

            _fileItems.Add(fileItem);
            _totalFileSize += fileInfo.Length;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法添加文件 {Path.GetFileName(filePath)}: {ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private void RemoveFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string filePath)
        {
            var fileItem = _fileItems.FirstOrDefault(item => item.FilePath == filePath);
            if (fileItem != null)
            {
                _totalFileSize -= fileItem.FileSizeBytes;
                _fileItems.Remove(fileItem);
                UpdateDisplay();
            }
        }
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fileItems.Count == 0) return;

        var result = MessageBox.Show($"确定要清空所有文件吗？共{_fileItems.Count}个文件。",
            "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _fileItems.Clear();
            _totalFileSize = 0;
            UpdateDisplay();
        }
    }

    private void MergeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_fileItems.Count == 0)
        {
            MessageBox.Show("请先添加要合并的文件", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var content = new StringBuilder();
        _fileItems.ToList().ForEach(x => content.AppendLine(ExtractText.FromAutomatic(x.FilePath)));

        var saveFileDialog = new SaveFileDialog
        {
            Filter = "合并 (*.meg)|*.meg",
            FileName = $"Merge_{DateTime.Now:yyyyMMdd_HHmmss}.meg",
            DefaultExt = ".meg"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            File.WriteAllText(saveFileDialog.FileName, content.ToString());
            MessageBox.Show("文件合并成功！", "合并成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Trigger the merge event
        OnMergeRequested();
    }

    private void UpdateDisplay()
    {
        // Update the file serial number
        for (int i = 0; i < _fileItems.Count; i++)
        {
            _fileItems[i].Index = i + 1;
        }

        // Update statistical information
        FilesCountText.Text = $"已添加 {_fileItems.Count} 个文件";
        TotalSizeText.Text = $"总大小：{FormatFileSize(_totalFileSize)}";

        // Update the status of the button
        ClearAllButton.IsEnabled = _fileItems.Count > 0;
        MergeButton.IsEnabled = _fileItems.Count >= 1;

        EmptyStatePanel.Visibility = _fileItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public List<string> SelectedFiles => _fileItems.Select(item => item.FilePath).ToList();

    public event EventHandler MergeRequested;
    protected virtual void OnMergeRequested()
    {
        MergeRequested?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
