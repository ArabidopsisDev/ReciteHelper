using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Wpf.Models;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Views {
    public partial class ExamSettingWindow : Window, INotifyPropertyChanged
    {
        private readonly IExamAnswerService _examAnswerService;
        private readonly IExamPaperService _examPaperService;
        private readonly IExamSettingsService _examSettingsService;
        private readonly IExamSetRepository _examSetRepository;
        private readonly IProjectCreationService _projectCreationService;
        private readonly IProjectFileService _projectFileService;
        private readonly QuickReviewService? _quickReviewService;
        private Project _project;
        private List<ChapterWeightSetting> _chapterWeights;
        private IReadOnlyList<ExamSet> _examSets = [];

        public ExamSettingWindow(
            Project project,
            IExamAnswerService examAnswerService,
            IExamPaperService examPaperService,
            IExamSettingsService examSettingsService,
            IExamSetRepository examSetRepository,
            IProjectCreationService projectCreationService,
            IProjectFileService projectFileService,
            QuickReviewService? quickReviewService = null)
        {
            _examAnswerService = examAnswerService;
            _examPaperService = examPaperService;
            _examSettingsService = examSettingsService;
            _examSetRepository = examSetRepository;
            _projectCreationService = projectCreationService;
            _projectFileService = projectFileService;
            _quickReviewService = quickReviewService;

            InitializeComponent();
            _project = project;

            InitializeSettings();
            InitializeChapterWeights();
            UpdatePreview();
            Loaded += async (_, _) => await LoadExamSetsAsync();
        }

        private async Task LoadExamSetsAsync()
        {
            try
            {
                _examSets = await _examSetRepository.LoadAllAsync(_project);
                ExamSetComboBox.ItemsSource = _examSets;
                LoadExamSetCheckBox.IsEnabled = _examSets.Count > 0;
                LoadExamSetCheckBox.ToolTip = _examSets.Count > 0
                    ? $"已找到 {_examSets.Count} 套导入试卷"
                    : "当前项目尚未导入套卷";
                if (_examSets.Count > 0)
                    ExamSetComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                LoadExamSetCheckBox.IsEnabled = false;
                MessageBox.Show($"加载套卷目录失败：{ex.Message}", "加载失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void InitializeSettings()
        {
            CourseNumberTextBox.Text = "XF114514";
            ExamTimeTextBox.Text = "60";
            ExamTimeSlider.Value = 60;
            QuestionCountTextBox.Text = "20";
            QuestionCountSlider.Value = 20;
        }

        private void InitializeChapterWeights()
        {
            _chapterWeights = new List<ChapterWeightSetting>();

            if (_project?.Chapters != null && _project.Chapters.Count > 0)
            {
                foreach (var chapter in _project.Chapters)
                {
                    _chapterWeights.Add(new ChapterWeightSetting
                    {
                        ChapterName = chapter.Name ?? $"第{chapter.Number}章",
                        QuestionCount = chapter.Questions?.Count ?? 0,
                        Weight = 0
                    });
                }

                NoChaptersPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoChaptersPanel.Visibility = Visibility.Visible;
            }

            ChapterWeightsItemsControl.ItemsSource = _chapterWeights;
            UpdateTotalWeight();
        }

        private void UpdatePreview()
        {
            if (int.TryParse(ExamTimeTextBox?.Text, out int examTime))
            {
                TotalTimePreview?.Content = $"考试时间：{examTime}分钟";
            }

            if (int.TryParse(QuestionCountTextBox?.Text, out int questionCount))
            {
                QuestionCountPreview?.Content = $"考试题量：{questionCount}题";
            }

            if (int.TryParse(QuestionCountTextBox?.Text, out int totalQuestions))
            {
                var choiceCount = (int)Math.Round(totalQuestions * 0.30d, MidpointRounding.AwayFromZero);
                var remainingCount = totalQuestions - choiceCount;
                var fillBlankCount = (int)Math.Round(remainingCount * 0.35d, MidpointRounding.AwayFromZero);
                var termCount = (int)Math.Round(remainingCount * 0.20d, MidpointRounding.AwayFromZero);
                var essayCount = remainingCount - fillBlankCount - termCount;
                var totalScore = choiceCount * 3 + fillBlankCount + termCount * 4 + essayCount * 5;
                ScorePerQuestionPreview?.Content = "分值：按题型固定";
                TotalScorePreview?.Content = $"预计满分：{totalScore}分";
            }
        }

        private void UpdateTotalWeight()
        {
            double totalWeight = _chapterWeights?.Sum(c => c.Weight) ?? 0;
            TotalWeightText.Text = $"{totalWeight:F0}%";
        }

        private void ExamTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ExamTimeTextBox.Text = ((int)ExamTimeSlider.Value).ToString();
            UpdatePreview();
        }

        private void QuestionCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            QuestionCountTextBox.Text = ((int)QuestionCountSlider.Value).ToString();
            UpdatePreview();
        }

        private void WeightTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Validate if the input is a number
                if (double.TryParse(textBox.Text, out double weight))
                {
                    // Limit the weight to between 0 and 100
                    if (weight < 0) weight = 0;
                    if (weight > 100) weight = 100;

                    textBox.Text = weight.ToString();

                    // Find the corresponding data item and update
                    var dataContext = (textBox.DataContext as ChapterWeightSetting);
                    if (dataContext != null)
                    {
                        dataContext.Weight = weight;
                        UpdateTotalWeight();
                    }
                }
                else if (!string.IsNullOrEmpty(textBox.Text))
                {
                    // If it is not a number, restore the original value
                    var dataContext = (textBox.DataContext as ChapterWeightSetting);
                    if (dataContext != null)
                    {
                        textBox.Text = dataContext.Weight.ToString();
                    }
                    else
                    {
                        textBox.Text = "0";
                    }
                }
            }
        }

        private void WeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                // Find the corresponding data item and update
                var dataContext = (slider.DataContext as ChapterWeightSetting);
                if (dataContext != null)
                {
                    dataContext.Weight = (int)slider.Value;
                    UpdateTotalWeight();

                    var parent = VisualTreeHelper.GetParent(slider);
                    while (parent != null && !(parent is Border))
                    {
                        parent = VisualTreeHelper.GetParent(parent);
                    }

                    if (parent is Border border)
                    {
                        var textBox = FindVisualChild<TextBox>(border, "WeightTextBox");
                        if (textBox != null)
                        {
                            textBox.Text = ((int)slider.Value).ToString();
                        }
                    }
                }
            }
        }

        private void ResetWeightsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要重置所有章节权重为0吗？", "重置权重",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var chapter in _chapterWeights)
                {
                    chapter.Weight = 0;
                }

                // Refresh display
                ChapterWeightsItemsControl.Items.Refresh();
                UpdateTotalWeight();
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            // Create an exam settings object
            var examSettings = ExamSettings.Create
            (
                CourseNumberTextBox.Text,
                int.Parse(ExamTimeTextBox.Text),
                int.Parse(QuestionCountTextBox.Text),
                5,
                _chapterWeights.ToDictionary(c => c.ChapterName, c => c.Weight)
            );

            try
            {
                await _examSettingsService.SaveAsync(_project, examSettings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存考试设置失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("考试设置已保存！", "保存成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void StartExamButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoadExamSetCheckBox.IsChecked is true)
            {
                StartImportedExam();
                return;
            }

            if (!ValidateInputs())
                return;

            // Create an exam settings object
            var examSettings = ExamSettings.Create
            (
                CourseNumberTextBox.Text,
                int.Parse(ExamTimeTextBox.Text),
                int.Parse(QuestionCountTextBox.Text),
                5,
                _chapterWeights.ToDictionary(c => c.ChapterName, c => c.Weight)
            );

            try
            {
                await _examSettingsService.SaveAsync(_project, examSettings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存考试设置失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var examQuestions = _examPaperService.Generate(_project, examSettings);

            if (examQuestions.Count == 0)
            {
                MessageBox.Show("无法生成考试题目，请检查章节权重设置或题目数量。", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var examWindow = new ExamWindow(
                examQuestions,
                _project.ProjectName!,
                examSettings,
                _examAnswerService,
                project: _project,
                projectCreationService: _projectCreationService,
                projectFileService: _projectFileService,
                quickReviewService: _quickReviewService);
            examWindow.Show();

            Close();
        }

        private void StartImportedExam()
        {
            if (ExamSetComboBox.SelectedItem is not ExamSet examSet)
            {
                MessageBox.Show("请选择要加载的套卷。", "未选择套卷", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var examSettings = ExamSettings.Create(
                "IMPORTED",
                examSet.SuggestedDurationMinutes,
                examSet.Questions.Count,
                10,
                null);
            var examWindow = new ExamWindow(
                examSet.Questions.Select(item => item.Question).ToList(),
                examSet.ResolvedMainTitle,
                examSettings,
                _examAnswerService,
                examSet.Questions,
                examSet.ResolvedSmallTitle,
                _project,
                _projectCreationService,
                _projectFileService,
                isImportedExamSet: true,
                importedSet: examSet,
                examSets: _examSetRepository,
                quickReviewService: _quickReviewService);
            examWindow.Show();
            Close();
        }

        private void LoadExamSetCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
                return;

            var useImportedSet = LoadExamSetCheckBox.IsChecked is true;
            DefaultPaperSettingsPanel.IsEnabled = !useImportedSet;
            ExamSetComboBox.IsEnabled = useImportedSet;
            SaveButton.IsEnabled = !useImportedSet;
            UpdateImportedSetPreview();
        }

        private void ExamSetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateImportedSetPreview();
        }

        private void UpdateImportedSetPreview()
        {
            if (LoadExamSetCheckBox.IsChecked is not true || ExamSetComboBox.SelectedItem is not ExamSet examSet)
                return;

            var totalScore = examSet.Questions.Sum(item => item.Score);
            TotalTimePreview.Content = $"考试时间：{examSet.SuggestedDurationMinutes}分钟";
            QuestionCountPreview.Content = $"考试题量：{examSet.Questions.Count}题";
            ScorePerQuestionPreview.Content = "题目与分值：采用原卷";
            TotalScorePreview.Content = $"试卷满分：{totalScore}分";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateInputs()
        {
            // Verify exam time
            if (!int.TryParse(ExamTimeTextBox.Text, out int examTime) || examTime < 10 || examTime > 180)
            {
                MessageBox.Show("考试时间必须在10-180分钟之间", "输入错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ExamTimeTextBox.Focus();
                return false;
            }

            // Verify the number of questions
            if (!int.TryParse(QuestionCountTextBox.Text, out int questionCount) || questionCount < 5 || questionCount > 100)
            {
                MessageBox.Show("考试题量必须在5-100题之间", "输入错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                QuestionCountTextBox.Focus();
                return false;
            }

            // Verify if a chapter exists
            if (_chapterWeights == null || _chapterWeights.Count == 0)
            {
                MessageBox.Show("当前项目没有章节，无法生成考试", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private T FindVisualChild<T>(DependencyObject parent, string childName = null) where T : DependencyObject
        {
            if (parent == null) return null;

            int childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

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

        private void PresetExamButton_Click(object sender, RoutedEventArgs e)
        {
            PresetMenu.PlacementTarget = PresetExamButton;
            PresetMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            PresetMenu.IsOpen = true;
        }

        private void WeakestPointExam_Click(object sender, RoutedEventArgs e)
        {
            if (_project.Chapters is null)
            {
                MessageBox.Show("组卷失败，您的项目不包含任何章节", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dict = new Dictionary<Chapter, double>();
            foreach (var chapter in _project.Chapters)
                dict.Add(chapter, ProjectView.CalculateMasteryLevel(chapter));

            var lowestChapters = dict.OrderBy(kvp => kvp.Value).
                                      ThenBy(kvp => Guid.NewGuid()).Take(5).
                                      Select(kvp => kvp.Key.Name).ToList();

            foreach (var weight in _chapterWeights)
            {
                if (lowestChapters.Contains(weight.ChapterName))
                    weight.Weight = 20;
                else
                    weight.Weight = 0;
            }

            MessageBox.Show("已完成最弱点组卷", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BalancedWeaknessExam_Click(object sender, RoutedEventArgs e)
        {
            if (_project.Chapters is null)
            {
                MessageBox.Show("组卷失败，您的项目不包含任何章节", "失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dict = new Dictionary<Chapter, double>();
            foreach (var chapter in _project.Chapters)
                dict.Add(chapter, ProjectView.CalculateMasteryLevel(chapter));

            var sum = dict.Values.Sum(v => 100 - (int)v);
            var count = 0;
            foreach (var kvp in dict)
            {
                var chapter = dict.Where(x => x.Key == kvp.Key).FirstOrDefault().Key;
                var weight = ProjectView.CalculateMasteryLevel(chapter);

                var ch = _chapterWeights.Where(x => x.ChapterName == kvp.Key.Name).FirstOrDefault();
                ch!.Weight = (100 - weight) * 100 / sum;
                count += (int)ch.Weight;
            }

            var diff = 100 - count;
            var assign = (int)Math.Ceiling((double)diff / _project.Chapters.Count);

            for (int i = 0; diff > 0; i++)
            {
                _chapterWeights[i].Weight += assign;
                diff -= assign;
            }

            MessageBox.Show("已完成弱点均衡", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
