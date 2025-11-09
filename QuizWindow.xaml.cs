using FuzzyString;
using ReciteHelper.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReciteHelper
{
    public partial class QuizWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<QuestionItem> _questions;
        private int _currentQuestionIndex = 0;
        private int _totalQuestions = 0;
        private int _correctCount = 0;
        private int _wrongCount = 0;
        private Project project = new();

        public QuizWindow(Project project)
        {
            InitializeComponent();
            DataContext = this;
            this.project = project;

            InitializeQuestions(project.QuestionBank!);
            LocateCurrent();
            UpdateDisplay();
        }


        private void SwitchToQuestion(int questionNumber)
        {
            if (questionNumber < 1 || questionNumber > _totalQuestions)
                return;

            int targetIndex = questionNumber - 1;

            if (targetIndex == _currentQuestionIndex)
                return;

            _currentQuestionIndex = targetIndex;
            UpdateDisplay();
        }

        private void SwitchButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.CommandParameter is int questionNumber)
            {
                SwitchToQuestion(questionNumber);
            }
        }

        private void InitializeQuestions(List<Question> questions)
        {
            _questions = new ObservableCollection<QuestionItem>();

            for (int i = 0; i < questions.Count; i++)
            {
                _questions.Add(new QuestionItem
                {
                    Number = i + 1,
                    Question = questions[i],
                    Status = questions[i].Status switch
                    {
                        true => AnswerStatus.Correct,
                        false => AnswerStatus.Wrong,
                        null => AnswerStatus.NotAnswered
                    },
                    UserAnswer = questions[i].UserAnswer,
                    StatusStyle = (Style)FindResource("AnswerCardButtonStyle")
                });
            }

            _totalQuestions = _questions.Count;
            AnswerCardItemsControl.ItemsSource = _questions;
            UpdateAnswerCardStyles();
            UpdateStatistics();
        }

        private void UpdateDisplay()
        {
            if (_questions == null || _questions.Count == 0) return;

            var currentQuestion = _questions[_currentQuestionIndex];

            // Update question display
            CurrentQuestionText.Text = (_currentQuestionIndex + 1).ToString();
            TotalQuestionsText.Text = _totalQuestions.ToString();
            QuestionTextBlock.Text = currentQuestion.Question.Text;

            // Clear the answer input box
            AnswerTextBox.Text = "";
            AnswerTextBox.IsEnabled = currentQuestion.Status == AnswerStatus.NotAnswered;

            // Update button state
            PrevButton.IsEnabled = _currentQuestionIndex > 0;
            NextButton.IsEnabled = _currentQuestionIndex < _totalQuestions - 1;

            // Hide the results area (if it's a new question)
            if (currentQuestion.Status == AnswerStatus.NotAnswered)
            {
                ResultArea.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowResult(currentQuestion);
            }

            UpdateAnswerCardStyles();
        }

        private void UpdateAnswerCardStyles()
        {
            foreach (var question in _questions)
            {
                // Reset to basic style
                question.StatusStyle = (Style)FindResource("AnswerCardButtonStyle");

                // Apply styles based on status
                switch (question.Status)
                {
                    case AnswerStatus.Correct:
                        question.StatusStyle = (Style)FindResource("CorrectAnswerStyle");
                        break;
                    case AnswerStatus.Wrong:
                        question.StatusStyle = (Style)FindResource("WrongAnswerStyle");
                        break;
                }

                // If this is the current question, add a border style
                if (question.Number == _currentQuestionIndex + 1)
                {
                    var currentStyle = new Style(typeof(Button), question.StatusStyle);
                    currentStyle.Setters.Add(new Setter(Button.BorderBrushProperty, new SolidColorBrush(Colors.Blue)));
                    currentStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(3)));
                    question.StatusStyle = currentStyle;
                }
            }
        }

        private void ShowResult(QuestionItem question)
        {
            ResultArea.Visibility = Visibility.Visible;

            switch (question.Status)
            {
                case AnswerStatus.Correct:
                    ResultTitleText.Text = "回答正确！";
                    ResultTitleText.Foreground = new SolidColorBrush(Color.FromRgb(21, 87, 36));
                    ResultArea.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                    ResultArea.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));
                    break;
                case AnswerStatus.Wrong:
                    ResultTitleText.Text = "回答错误！";
                    ResultTitleText.Foreground = new SolidColorBrush(Color.FromRgb(114, 28, 36));
                    ResultArea.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                    ResultArea.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));
                    break;
            }

            UserAnswerText.Text = question.UserAnswer ?? "";
            CorrectAnswerText.Text = question.Question.CorrectAnswer;
        }

        private void UpdateStatistics()
        {
            _correctCount = _questions.Count(q => q.Status == AnswerStatus.Correct);
            _wrongCount = _questions.Count(q => q.Status == AnswerStatus.Wrong);

            StatsTextBlock.Text = $"总计: {_totalQuestions}  正确: {_correctCount}  错误: {_wrongCount}";

            double progress = _totalQuestions > 0 ? (double)(_correctCount + _wrongCount) / _totalQuestions : 0;
            ProgressBar.Value = progress * 100;
        }

        private void LocateCurrent()
        {
            for (int i = 0; i < _questions.Count(); i++)
            {
                if (_questions[i].Status == AnswerStatus.NotAnswered)
                {
                    _currentQuestionIndex = i;
                    return;
                }
            }
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AnswerTextBox.Text))
            {
                MessageBox.Show("请输入答案", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var currentQuestion = _questions[_currentQuestionIndex];
            currentQuestion.UserAnswer = AnswerTextBox.Text.Trim();

            // Determine whether the answer is roughly similar to the given answer
            var tolerance = FuzzyStringComparisonTolerance.Strong;
            var comparisonOptions = new List<FuzzyStringComparisonOptions>
            {
                FuzzyStringComparisonOptions.UseOverlapCoefficient,
                FuzzyStringComparisonOptions.UseLongestCommonSubsequence,
                FuzzyStringComparisonOptions.UseLongestCommonSubstring
            };

            bool isCorrect = currentQuestion.UserAnswer.ApproximatelyEquals(
                currentQuestion.Question!.CorrectAnswer, comparisonOptions, tolerance);
            currentQuestion.Status = isCorrect ? AnswerStatus.Correct : AnswerStatus.Wrong;

            ShowResult(currentQuestion);
            AnswerTextBox.IsEnabled = false;
            UpdateStatistics();
            UpdateAnswerCardStyles();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentQuestionIndex < _totalQuestions - 1)
            {
                _currentQuestionIndex++;
                UpdateDisplay();
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentQuestionIndex > 0)
            {
                _currentQuestionIndex--;
                UpdateDisplay();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentQuestionIndex < _totalQuestions - 1)
            {
                _currentQuestionIndex++;
                UpdateDisplay();
            }
        }

        private void AnswerTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // TODO: AI verification
        }

        public class QuestionItem : INotifyPropertyChanged
        {
            public int Number { get; set; }
            public Question? Question { get; set; }
            public AnswerStatus Status { get; set; }
            public string? UserAnswer { get; set; }

            public Style? StatusStyle
            {
                get => field;
                set
                {
                    field = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public enum AnswerStatus
        {
            NotAnswered,
            Correct,
            Wrong
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (project is null) return;

            // Save record
            for (int i = 0; i < _questions.Count(); i++)
            {
                project.QuestionBank![i].UserAnswer = _questions[i].UserAnswer;
                project.QuestionBank[i].Status = _questions[i].Status switch
                {
                    AnswerStatus.NotAnswered => null,
                    AnswerStatus.Correct => true,
                    AnswerStatus.Wrong => false,
                    _ => throw new NotImplementedException("Fuck U")
                };
            }

            var path = Path.Combine(project.StoragePath!, project.ProjectName!, project.ProjectName!);
            var json = System.Text.Json.JsonSerializer.Serialize(project,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText($"{path}.rhproj", json);
        }
    }
}