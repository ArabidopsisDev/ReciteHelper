using ReciteHelper.Model;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper.View
{
    public partial class ExamSettingWindow : Window, INotifyPropertyChanged
    {
        private Project _project;
        private List<ChapterWeightSetting> _chapterWeights;

        public ExamSettingWindow(Project project)
        {
            InitializeComponent();
            _project = project;

            InitializeSettings();
            InitializeChapterWeights();
            UpdatePreview();
        }

        private void InitializeSettings()
        {
            CourseNumberTextBox.Text = "XF114514";
            ExamTimeTextBox.Text = "60";
            ExamTimeSlider.Value = 60;
            QuestionCountTextBox.Text = "20";
            QuestionCountSlider.Value = 20;
            ScorePerQuestionTextBox.Text = "5";
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
            return;

            if (int.TryParse(ExamTimeTextBox?.Text, out int examTime))
            {
                TotalTimePreview?.Content = $"考试时间：{examTime}分钟";
            }

            if (int.TryParse(QuestionCountTextBox?.Text, out int questionCount))
            {
                QuestionCountPreview?.Content = $"考试题量：{questionCount}题";
            }

            if (int.TryParse(ScorePerQuestionTextBox?.Text, out int scorePerQuestion))
            {
                ScorePerQuestionPreview?.Content = $"每题分值：{scorePerQuestion}分";

                if (int.TryParse(QuestionCountTextBox?.Text, out int totalQuestions))
                {
                    int totalScore = scorePerQuestion * totalQuestions;
                    TotalScorePreview?.Content = $"试卷总分：{totalScore}分";
                }
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

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            // Create an exam settings object
            var examSettings = new ExamSettings
            {
                CourseNumber = CourseNumberTextBox.Text,
                ExamTimeMinutes = int.Parse(ExamTimeTextBox.Text),
                QuestionCount = int.Parse(QuestionCountTextBox.Text),
                ScorePerQuestion = int.Parse(ScorePerQuestionTextBox.Text),
                ChapterWeights = _chapterWeights.ToDictionary(c => c.ChapterName, c => c.Weight)
            };

            // Here you can save settings to a file or database
            SaveExamSettings(examSettings);

            MessageBox.Show("考试设置已保存！", "保存成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void StartExamButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs())
                return;

            // Create an exam settings object
            var examSettings = new ExamSettings
            {
                CourseNumber = CourseNumberTextBox.Text,
                ExamTimeMinutes = int.Parse(ExamTimeTextBox.Text),
                QuestionCount = int.Parse(QuestionCountTextBox.Text),
                ScorePerQuestion = int.Parse(ScorePerQuestionTextBox.Text),
                ChapterWeights = _chapterWeights.ToDictionary(c => c.ChapterName, c => c.Weight)
            };

            SaveExamSettings(examSettings);

            var examQuestions = GenerateExamQuestions(examSettings);

            if (examQuestions.Count == 0)
            {
                MessageBox.Show("无法生成考试题目，请检查章节权重设置或题目数量。", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var examWindow = new ExamWindow(examQuestions, _project.ProjectName!);
            examWindow.Show();

            Close();
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

            // Verify the score for each question
            if (!int.TryParse(ScorePerQuestionTextBox.Text, out int scorePerQuestion) || scorePerQuestion <= 0)
            {
                MessageBox.Show("每题分数必须为正整数", "输入错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ScorePerQuestionTextBox.Focus();
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

        private void SaveExamSettings(ExamSettings settings)
        {
            try
            {
                // You can save the settings to a file
                string json = System.Text.Json.JsonSerializer.Serialize(settings,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                string settingsPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(_project.StoragePath),
                    "exam_settings.json");

                System.IO.File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存考试设置失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<Question> GenerateExamQuestions(ExamSettings settings)
        {
            var allQuestions = new List<Question>();

            foreach (var chapter in _project.Chapters)
            {
                if (chapter.Questions != null)
                {
                    foreach (var question in chapter.Questions)
                    {
                        allQuestions.Add(new Question
                        {
                            Text = question.Text,
                            CorrectAnswer = question.CorrectAnswer,
                        });
                    }
                }
            }

            if (allQuestions.Count < settings.QuestionCount)
            {
                return new List<Question>();
            }

            // If all weights are 0, randomly select a question
            if (_chapterWeights.All(c => c.Weight == 0))
            {
                return GetRandomQuestions(allQuestions, settings.QuestionCount);
            }

            // Otherwise, questions will be selected based on weight
            return GetWeightedQuestions(settings);
        }

        private List<Question> GetRandomQuestions(List<Question> allQuestions, int count)
        {
            var random = new Random();
            return allQuestions.OrderBy(x => random.Next()).Take(count).ToList();
        }

        private List<Question> GetWeightedQuestions(ExamSettings settings)
        {
            var selectedQuestions = new List<Question>();
            var random = new Random();

            // Calculate the total weight
            double totalWeight = _chapterWeights.Sum(c => c.Weight);

            // Allocate the number of questions by weight
            foreach (var chapter in _project.Chapters)
            {
                var weightSetting = _chapterWeights.FirstOrDefault(c => c.ChapterName == chapter.Name);
                if (weightSetting == null || weightSetting.Weight == 0 || chapter.Questions == null)
                    continue;

                // Calculate the number of questions to be drawn for this chapter
                double proportion = weightSetting.Weight / totalWeight;
                int chapterQuestionCount = (int)Math.Round(settings.QuestionCount * proportion);

                chapterQuestionCount = Math.Max(1, Math.Min(chapterQuestionCount, chapter.Questions.Count));
                var chapterQuestions = chapter.Questions.OrderBy(x => random.Next()).Take(chapterQuestionCount);

                foreach (var question in chapterQuestions)
                {
                    selectedQuestions.Add(new Question
                    {
                        Text = question.Text,
                        CorrectAnswer = question.CorrectAnswer,
                    });
                }
            }

            // Supplement them from other chapters
            if (selectedQuestions.Count < settings.QuestionCount)
            {
                var remainingCount = settings.QuestionCount - selectedQuestions.Count;
                var allRemainingQuestions = new List<Question>();

                foreach (var chapter in _project.Chapters)
                {
                    if (chapter.Questions != null)
                    {
                        // Find the questions that were not selected
                        var selectedContents = selectedQuestions.Select(q => q.Text).ToList();
                        var remaining = chapter.Questions.Where(q => !selectedContents.Contains(q.Text)).ToList();
                        allRemainingQuestions.AddRange(remaining);
                    }
                }

                var additionalQuestions = allRemainingQuestions.OrderBy(x => random.Next()).Take(remainingCount);
                foreach (var question in additionalQuestions)
                {
                    selectedQuestions.Add(new Question
                    {
                        Text = question.Text,
                        CorrectAnswer = question.CorrectAnswer,
                    });
                }
            }

            // Randomly shuffle the order of the questions
            return selectedQuestions.OrderBy(x => random.Next()).ToList();
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

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}