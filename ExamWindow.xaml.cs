using FuzzyString;
using ReciteHelper.Models;
using ReciteHelper.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReciteHelper;

public partial class ExamWindow : Window, INotifyPropertyChanged
{
    private ObservableCollection<ExamQuestionItem> _questions;
    private int _currentQuestionIndex = 0;
    private int _totalQuestions = 0;
    private int _correctCount = 0;
    private DateTime _examStartTime;
    private DispatcherTimer _examTimer;
    private TimeSpan _examDuration = TimeSpan.FromMinutes(60);
    private TimeSpan _timeRemaining;

    public ExamWindow(List<Question> questions, string examName)
    {
        InitializeComponent();
        DataContext = this;

        GenerateExamNumber();
        InitializeQuestions(questions);
        InitializeTimer();
        ShowInstructions();

        ExamText.Text = $"2025-2026 学年第 1 学期《{examName}》课程考试（A）卷";
    }

    private void GenerateExamNumber()
    {
        Random random = new Random();
        string examNumber = $"RK{DateTime.Now:yyyyMMdd}{random.Next(1000, 9999)}";
        ExamNumberText.Text = examNumber;
    }

    private void InitializeQuestions(List<Question> questions)
    {
        _questions = new ObservableCollection<ExamQuestionItem>();

        for (int i = 0; i < questions.Count; i++)
        {
            _questions.Add(new ExamQuestionItem
            {
                Number = i + 1,
                Question = questions[i],
                UserAnswer = "",
                Status = ExamAnswerStatus.NotAnswered,
                StatusStyle = (Style)FindResource("ExamCardButtonStyle")
            });
        }

        _totalQuestions = _questions.Count;
        ExamCardItemsControl.ItemsSource = _questions;
        TotalQuestionsText.Text = _totalQuestions.ToString();
    }

    private void InitializeTimer()
    {
        _examTimer = new DispatcherTimer();
        _examTimer.Interval = TimeSpan.FromSeconds(1);
        _examTimer.Tick += ExamTimer_Tick;
        _timeRemaining = _examDuration;
        UpdateTimeDisplay();
    }

    private void ExamTimer_Tick(object sender, EventArgs e)
    {
        _timeRemaining = _timeRemaining.Subtract(TimeSpan.FromSeconds(1));
        UpdateTimeDisplay();

        if (_timeRemaining <= TimeSpan.Zero)
        {
            _examTimer.Stop();
            TimeRemainingText.Text = "00:00";
            AutoSubmitExam();
        }
    }

    private void UpdateTimeDisplay()
    {
        TimeRemainingText.Text = $"{_timeRemaining:mm\\:ss}";

        if (_timeRemaining <= TimeSpan.FromMinutes(10))
        {
            TimeRemainingText.Foreground = new SolidColorBrush(Colors.Red);
        }
    }

    private void ShowInstructions()
    {
        InstructionsPart.Visibility = Visibility.Visible;
        ExamPart.Visibility = Visibility.Collapsed;
        ResultPart.Visibility = Visibility.Collapsed;
        SubmitExamButton.Visibility = Visibility.Collapsed;
    }

    private void ShowExam()
    {
        InstructionsPart.Visibility = Visibility.Collapsed;
        ExamPart.Visibility = Visibility.Visible;
        ResultPart.Visibility = Visibility.Collapsed;
        SubmitExamButton.Visibility = Visibility.Visible;

        _examStartTime = DateTime.Now;
        _examTimer.Start();
        UpdateDisplay();
    }

    private void ShowResults()
    {
        InstructionsPart.Visibility = Visibility.Collapsed;
        ExamPart.Visibility = Visibility.Collapsed;
        ResultPart.Visibility = Visibility.Visible;
        SubmitExamButton.Visibility = Visibility.Collapsed;

        _examTimer.Stop();
        CalculateScore();
    }

    private void UpdateDisplay()
    {
        if (_questions == null || _questions.Count == 0) return;

        var currentQuestion = _questions[_currentQuestionIndex];

        CurrentQuestionText.Text = (_currentQuestionIndex + 1).ToString();
        QuestionContentText.Text = currentQuestion.Question.Text;

        AnswerInputTextBox.Text = currentQuestion.UserAnswer;
        UpdateExamCardStyles();
    }

    private void UpdateExamCardStyles()
    {
        foreach (var question in _questions)
        {
            if (question.Number == _currentQuestionIndex + 1)
            {
                question.StatusStyle = (Style)FindResource("CurrentExamQuestionStyle");
            }
            else if (!string.IsNullOrEmpty(question.UserAnswer))
            {
                question.StatusStyle = (Style)FindResource("AnsweredExamQuestionStyle");
            }
            else
            {
                question.StatusStyle = (Style)FindResource("ExamCardButtonStyle");
            }
        }
    }

    private void CalculateScore()
    {
        _correctCount = 0;

        var tolerance = FuzzyStringComparisonTolerance.Strong;
        var comparisonOptions = new List<FuzzyStringComparisonOptions>
            {
                FuzzyStringComparisonOptions.UseOverlapCoefficient,
                FuzzyStringComparisonOptions.UseLongestCommonSubsequence,
                FuzzyStringComparisonOptions.UseLongestCommonSubstring
            };
        var similarity = new CosineSimilarity();

        foreach (var question in _questions)
        {
            if (string.IsNullOrEmpty(question.UserAnswer)) continue;

            var cscore = similarity.Calculate(question.UserAnswer,
                question.Question!.CorrectAnswer!);
            bool isCorrect = question.UserAnswer.ApproximatelyEquals(
                question.Question!.CorrectAnswer, comparisonOptions, tolerance);
            isCorrect = isCorrect | (cscore > .5);

            if (isCorrect)
            {
                _correctCount++;
            }
        }

        var score = (_correctCount * 100.0) / _totalQuestions;
        var wrongCount = _totalQuestions - _correctCount;
        var timeUsed = DateTime.Now - _examStartTime;

        ScoreText.Text = score.ToString("F0");
        ScoreDetailText.Text = $"正确率：{score:F1}%";
        CorrectCountText.Text = $"答对：{_correctCount}题";
        WrongCountText.Text = $"答错：{wrongCount}题";
        TimeUsedText.Text = $"用时：{timeUsed:mm\\:ss}";

        if (score >= 90)
            EncouragementText.Text = "优秀！你的表现非常出色！";
        else if (score >= 80)
            EncouragementText.Text = "很好！继续努力！";
        else if (score >= 60)
            EncouragementText.Text = "及格了，还有提升空间！";
        else
            EncouragementText.Text = "需要多加练习，加油！";
    }

    private void StartExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (AgreeCheckBox.IsChecked == true)
        {
            ShowExam();
        }
    }

    private void AgreeCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        StartExamButton.IsEnabled = AgreeCheckBox.IsChecked == true;
    }

    private void ExamCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is int questionNumber)
        {
            SwitchToQuestion(questionNumber);
        }
    }

    private void SwitchToQuestion(int questionNumber)
    {
        if (questionNumber < 1 || questionNumber > _totalQuestions)
            return;

        SaveCurrentAnswer();

        int targetIndex = questionNumber - 1;
        _currentQuestionIndex = targetIndex;
        UpdateDisplay();
    }

    private void SaveCurrentAnswer()
    {
        var currentQuestion = _questions[_currentQuestionIndex];
        currentQuestion.UserAnswer = AnswerInputTextBox.Text.Trim();

        if (!string.IsNullOrEmpty(currentQuestion.UserAnswer))
        {
            currentQuestion.Status = ExamAnswerStatus.Answered;
        }
        else
        {
            currentQuestion.Status = ExamAnswerStatus.NotAnswered;
        }
    }

    private void SaveAnswerButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentAnswer();
        UpdateExamCardStyles();
        MessageBox.Show("答案已保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearAnswerButton_Click(object sender, RoutedEventArgs e)
    {
        AnswerInputTextBox.Text = "";
        var currentQuestion = _questions[_currentQuestionIndex];
        currentQuestion.UserAnswer = "";
        currentQuestion.Status = ExamAnswerStatus.NotAnswered;
        UpdateExamCardStyles();
    }

    private void NextQuestionButton_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentAnswer();

        if (_currentQuestionIndex < _totalQuestions - 1)
        {
            _currentQuestionIndex++;
            UpdateDisplay();
        }
        else
        {
            MessageBox.Show("已经是最后一题了", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AnswerInputTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // 实时保存答案（可选）
        // 或者只在点击保存按钮时保存
    }

    private void SubmitExamButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("确定要交卷吗？交卷后将不能修改答案。",
            "确认交卷", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            SubmitExam();
        }
    }

    private void AutoSubmitExam()
    {
        MessageBox.Show("考试时间已到，系统将自动交卷。", "时间到", MessageBoxButton.OK, MessageBoxImage.Information);
        SubmitExam();
    }

    private void SubmitExam()
    {
        // Make sure to save all answers
        SaveCurrentAnswer();
        ShowResults();
    }

    private void ReviewAnswersButton_Click(object sender, RoutedEventArgs e)
    {
        // Create a window to view the answers
        var reviewWindow = new ExamReviewWindow(/*_questions.ToList()*/);
        reviewWindow.Owner = this;
        reviewWindow.ShowDialog();
    }

    private void RetryExamButton_Click(object sender, RoutedEventArgs e)
    {
        // Restart the exam
        var result = MessageBox.Show("确定要重新开始考试吗？", "重新考试",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // Reset all answers
            foreach (var question in _questions)
            {
                question.UserAnswer = "";
                question.Status = ExamAnswerStatus.NotAnswered;
            }

            _currentQuestionIndex = 0;
            _timeRemaining = _examDuration;
            TimeRemainingText.Foreground = new SolidColorBrush(Color.FromRgb(133, 135, 150));

            ShowExam();
        }
    }

    private void CloseExamButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (ExamPart.Visibility == Visibility.Visible)
        {
            var result = MessageBox.Show("考试尚未结束，确定要退出吗？", "确认退出",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
            }
            else
            {
                _examTimer.Stop();
            }
        }
    }

    public class ExamQuestionItem : INotifyPropertyChanged
    {
        public int Number { get; set; }
        public Question Question { get; set; }
        public string UserAnswer { get; set; }
        public ExamAnswerStatus Status { get; set; }

        private Style _statusStyle;
        public Style StatusStyle
        {
            get => _statusStyle;
            set
            {
                _statusStyle = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum ExamAnswerStatus
    {
        NotAnswered,
        Answered
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void AgreeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (AgreeCheckBox.IsChecked == true) StartExamButton.IsEnabled = true;
        else StartExamButton?.IsEnabled = false;
    }
}