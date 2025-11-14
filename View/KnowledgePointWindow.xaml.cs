using ReciteHelper.Model;
using ReciteHelper.Utils;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper.View;

public partial class KnowledgePointWindow : Window, INotifyPropertyChanged
{
    private Project _currentProject;
    private KnowledgePoint _currentKnowledgePoint;
    private string _currentMarkdownContent;
    private Renderer renderer;

    public KnowledgePointWindow(Project project)
    {
        InitializeComponent();
        _currentProject = project;

        DataContext = this;
        InitializeData();
        UpdateDisplay();
    }

    private void InitializeData()
    {
        if (_currentProject?.Chapters != null)
        {
            ChaptersItemsControl.ItemsSource = _currentProject.Chapters;

            // Check if there are any knowledge points
            var hasKnowledgePoints = _currentProject.Chapters.Any(c =>
                c.KnowledgePoints != null && c.KnowledgePoints.Count > 0);

            EmptyStatePanel.Visibility = hasKnowledgePoints ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateDisplay()
    {
        // Update project title
        Title = $"知识点学习 - {_currentProject?.ProjectName ?? "未命名项目"}";
    }

    private void ChapterToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Chapter chapter)
        {
            // Find the corresponding knowledge point ItemsControl
            var parent = VisualTreeHelper.GetParent(button);
            while (parent != null && !(parent is ItemsControl))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is ItemsControl itemsControl)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromItem(chapter);
                if (container != null)
                {
                    var knowledgePointsControl = FindVisualChild<ItemsControl>(container, "KnowledgePointsItemsControl");
                    var expandIcon = FindVisualChild<System.Windows.Shapes.Path>(button, "ExpandIcon");

                    if (knowledgePointsControl != null)
                    {
                        if (knowledgePointsControl.Visibility == Visibility.Collapsed)
                        {
                            knowledgePointsControl.Visibility = Visibility.Visible;
                            if (expandIcon != null)
                            {
                                expandIcon.RenderTransform = new RotateTransform(180, 8, 4);
                            }
                        }
                        else
                        {
                            knowledgePointsControl.Visibility = Visibility.Collapsed;
                            if (expandIcon != null)
                            {
                                var transform = expandIcon.RenderTransform as RotateTransform;
                                expandIcon.RenderTransform = new RotateTransform(0, 8, 4);
                            }
                        }
                    }
                }
            }
        }
    }

    private void KnowledgePointButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is KnowledgePoint knowledgePoint)
        {
            ShowKnowledgePointDetail(knowledgePoint);
        }
    }

    private void MasteryCheckBox_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is CheckBox checkBox && checkBox.Tag is KnowledgePoint knowledgePoint)
        {
            knowledgePoint.IsMastered = checkBox.IsChecked == true;

            // If this knowledge point is currently displayed, update the details area.
            if (_currentKnowledgePoint == knowledgePoint)
            {
                UpdateMasteryDisplay();
            }

            // Save changes
            SaveProjectChanges();
        }
    }

    private void DetailMasteryCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_currentKnowledgePoint != null)
        {
            _currentKnowledgePoint.IsMastered = MasteryCheckBox.IsChecked == true;
            UpdateMasteryDisplay();
            SaveProjectChanges();

            // Update the checkbox status in the list
            RefreshKnowledgePointList();
        }
    }

    private void ShowKnowledgePointDetail(KnowledgePoint knowledgePoint)
    {
        _currentKnowledgePoint = knowledgePoint;
        CurrentMarkdownContent = knowledgePoint.ContentMarkdown ?? "# 暂无内容\n\n该知识点还没有添加详细内容。";

        // Update UI
        KnowledgePointTitle.Text = knowledgePoint.Name ?? "未命名知识点";
        UpdateMasteryDisplay();

        // Hide empty state
        DetailEmptyStatePanel.Visibility = Visibility.Collapsed;
        MarkdownViewer.Visibility = Visibility.Visible;
    }

    private void UpdateMasteryDisplay()
    {
        if (_currentKnowledgePoint != null)
        {
            MasteryCheckBox.IsChecked = _currentKnowledgePoint.IsMastered;

            if (_currentKnowledgePoint.IsMastered)
            {
                MasteryStatusText.Text = "已掌握";
                MasteryStatusText.Foreground = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            }
            else
            {
                MasteryStatusText.Text = "未掌握";
                MasteryStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69));
            }
        }
    }

    private void RefreshKnowledgePointList()
    {
        // Force refresh list display
        ChaptersItemsControl.Items.Refresh();
    }

    private void SaveProjectChanges()
    {
        try
        {
            // Save project data to file
            string json = System.Text.Json.JsonSerializer.Serialize(_currentProject,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                });

            File.WriteAllText(Path.Combine(_currentProject.StoragePath!,$"{_currentProject.ProjectName}.rhproj"), json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存知识点状态失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private T? FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
    {
        if (parent == null) return null;

        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T result)
            {
                if (childName == null || (child is FrameworkElement fe && fe.Name == childName))
                {
                    return result;
                }
            }

            var descendant = FindVisualChild<T>(child, childName);
            if (descendant != null) return descendant;
        }

        return null;
    }

    public string CurrentMarkdownContent
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged(nameof(CurrentMarkdownContent));

            Dispatcher.BeginInvoke(new Action(() =>
            {
                renderer.ApplyMarkdownStylesWithRetry();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        renderer = new Renderer(MarkdownViewer, Dispatcher);
    }
}