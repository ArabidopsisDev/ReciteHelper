using ReciteHelper.Models;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ReciteHelper;

public partial class SelectChapterWindow : Window, INotifyPropertyChanged
{
    private Project _currentProject;
    private DispatcherTimer _clockTimer;
    private List<ChapterViewModel> _chapters;

    public SelectChapterWindow(Project project)
    {
        InitializeComponent();
        _currentProject = project;

        InitializeData();
        InitializeClock();
        UpdateDisplay();
    }

    private void InitializeData()
    {
        // 转换章节数据为ViewModel
        _chapters = new List<ChapterViewModel>();

        if (_currentProject?.Chapters != null)
        {
            foreach (var chapter in _currentProject.Chapters)
            {
                var chapterVM = new ChapterViewModel(chapter)
                {
                    MasteryLevel = CalculateMasteryLevel(chapter)
                };
                _chapters.Add(chapterVM);
            }
        }

        ChaptersItemsControl.ItemsSource = _chapters;
    }

    private double CalculateMasteryLevel(Chapter chapter)
    {
        // Calculate the mastery level of the chapter.
        if (chapter.Questions == null || chapter.Questions.Count == 0)
            return 0;

        int count = 0, sum = chapter.Questions.Count;
        foreach (var question in chapter.Questions)
            if (question.Status == true) count++;

        return (double)count / sum * 100d;
    }

    private void InitializeClock()
    {
        _clockTimer = new DispatcherTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();
    }

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        CurrentTimeText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void UpdateDisplay()
    {
        // Update project information
        ProjectNameText.Text = _currentProject?.ProjectName ?? "未命名项目";
        ProjectPathText.Text = _currentProject?.StoragePath ?? "路径不可用";

        // Update last access time
        if (_currentProject?.LastAccessed != null)
        {
            LastAccessedText.Text = $"最后访问：{_currentProject.LastAccessed:yyyy-MM-dd HH:mm}";
        }

        // Update statistics
        var chapterCount = _chapters?.Count ?? 0;
        var totalQuestions = _chapters?.Sum(c => c.QuestionCount) ?? 0;
        ChapterStatsText.Text = $"共 {chapterCount} 个章节，{totalQuestions} 道题目";

        // Show/hide empty state
        EmptyStatePanel.Visibility = chapterCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChapterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ChapterViewModel chapterVM)
        {
            NavigateToChapterQuiz(chapterVM.Chapter);
        }
    }

    private void NavigateToChapterQuiz(Chapter chapter)
    {
        if (chapter?.Questions == null || chapter.Questions.Count == 0)
        {
            MessageBox.Show("该章节暂无题目", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var quizWindow = new QuizWindow(_currentProject, chapter.Name!)
        {
            Title = $"{_currentProject.ProjectName} - {chapter.Name}",
            Owner = this
        };

        quizWindow.ShowDialog();
        RefreshMasteryLevels();
    }

    private void RefreshMasteryLevels()
    {
        foreach (var chapterVM in _chapters)
        {
            chapterVM.MasteryLevel = CalculateMasteryLevel(chapterVM.Chapter);
        }

        ChaptersItemsControl.Items.Refresh();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public Project CurrentProject
    {
        get => _currentProject;
        set
        {
            _currentProject = value;
            OnPropertyChanged(nameof(CurrentProject));
            InitializeData();
            UpdateDisplay();
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class ChapterViewModel : INotifyPropertyChanged
{
    public Chapter Chapter { get; }

    public int QuestionCount => Chapter.Questions?.Count ?? 0;

    private double _masteryLevel;
    public double MasteryLevel
    {
        get => _masteryLevel;
        set
        {
            _masteryLevel = value;
            OnPropertyChanged(nameof(MasteryLevel));
        }
    }

    public ChapterViewModel(Chapter chapter)
    {
        Chapter = chapter;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}