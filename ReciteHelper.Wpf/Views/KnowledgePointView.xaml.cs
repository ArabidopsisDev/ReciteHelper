using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Utilities;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Views;

public partial class KnowledgePointView : UserControl, INotifyPropertyChanged
{
    private readonly IProjectFileService _projectFileService;
    private readonly Project _currentProject;
    private KnowledgePoint? _currentKnowledgePoint;
    private readonly Renderer _renderer;
    private List<KnowledgeSection> _sections = [];

    internal Project Project => _currentProject;

    public KnowledgePointView(Project project, IProjectFileService projectFileService)
    {
        _projectFileService = projectFileService;

        InitializeComponent();
        _currentProject = project;
        _renderer = new Renderer(MarkdownViewer, Dispatcher);

        DataContext = this;
        RefreshSources();
    }

    internal void RefreshSources()
    {
        _sections = (_currentProject.Chapters ?? []).Select(chapter => new KnowledgeSection(chapter)).ToList();
        ChaptersItemsControl.ItemsSource = _sections;
        EmptyStatePanel.Visibility = _sections.Any(section => section.KnowledgePoints.Count > 0) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ChapterToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is KnowledgeSection section)
        {
            // Find the corresponding knowledge point ItemsControl
            var parent = VisualTreeHelper.GetParent(button);
            while (parent != null && !(parent is ItemsControl))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is ItemsControl itemsControl)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromItem(section);
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
            SaveMastery(knowledgePoint, checkBox.IsChecked is true);
        }
    }

    private void DetailMasteryCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (_currentKnowledgePoint is { } point)
            SaveMastery(point, MasteryCheckBox.IsChecked is true);
    }

    private void ShowKnowledgePointDetail(KnowledgePoint knowledgePoint)
    {
        _currentKnowledgePoint = knowledgePoint;
        CurrentMarkdownContent = knowledgePoint.ContentMarkdown ?? "### 暂无内容\n\n该知识点没有重要需记忆内容。";

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

    private async void SaveMastery(KnowledgePoint original, bool mastered)
    {
        var section = _sections.First(group => group.KnowledgePoints.Any(point => ReferenceEquals(point, original)));
        var index = section.Chapter.KnowledgePoints!.FindIndex(point => ReferenceEquals(point, original));
        IsEnabled = false;
        void UpdatePoint(KnowledgePoint point)
        {
            if (ReferenceEquals(_currentKnowledgePoint, section.KnowledgePoints[index]))
                _currentKnowledgePoint = point;
            section.Chapter.KnowledgePoints[index] = point;
            section.KnowledgePoints[index] = point;
            UpdateMasteryDisplay();
        }

        UpdatePoint(original.ModifyMasteredStatus(mastered));
        try
        {
            await _projectFileService.SaveProjectAsync(_currentProject);
        }
        catch (Exception ex)
        {
            UpdatePoint(original);
            MessageBox.Show($"保存知识点状态失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private sealed class KnowledgeSection(Chapter chapter)
    {
        public Chapter Chapter { get; } = chapter;
        public string? Name => Chapter.Name;
        public int Number => Chapter.Number;
        public ObservableCollection<KnowledgePoint> KnowledgePoints { get; } = new(chapter.KnowledgePoints ?? []);
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
                _renderer.ApplyMarkdownStylesWithRetry();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

}
