using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ReciteHelper.Model;
using ReciteHelper.Utils;
using ReciteHelper.ViewModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ReciteHelper.View;

public partial class QuizWindow : Window, INotifyPropertyChanged
{
    private ObservableCollection<QuestionItem> _questions;
    private LatestBuffer<bool> _latest;
    private int _currentQuestionIndex = 0;
    private int _totalQuestions = 0;
    private string _chapterName = "";
    private Project _project = new();
    private DateTime _startTime = DateTime.Now;

    public QuizWindow(Project project, string chapterName)
    {
        InitializeComponent();
        DataContext = this;

        _project = project;
        _chapterName= chapterName;
        _latest = new LatestBuffer<bool>(Config.Configure.PhonkOptions.WrongCount);

        InitializeQuestions(project.Chapters!.Find(x => x.Name == chapterName)!.Questions!);
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
    }

    private void UpdateDisplay()
    {
        if (_questions == null || _questions.Count == 0) return;

        var currentQuestion = _questions[_currentQuestionIndex];

        // Update question display
        CurrentQuestionText.Text = (_currentQuestionIndex + 1).ToString();
        TotalQuestionsText.Text = _totalQuestions.ToString();
        QuestionTextBlock.Text = currentQuestion.Question!.Text;

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

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("是否确认清空答题记录？", "清空记录",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            for (int i = 0; i < _questions.Count(); i++)
            {
                _questions[i].UserAnswer = null;
                _questions[i].Status = AnswerStatus.NotAnswered;
            }
        }

        Window_Closing(sender, null!);
        Close();
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

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AnswerTextBox.Text))
        {
            MessageBox.Show("请输入答案", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentQuestion = _questions[_currentQuestionIndex];
        currentQuestion.UserAnswer = AnswerTextBox.Text.Trim();

        // Determine whether the answer is roughly similar to the given answer
        var isCorrect = JudgeAnswer.Run(currentQuestion);
        currentQuestion.Status = isCorrect ? AnswerStatus.Correct : AnswerStatus.Wrong;

        // Show result
        ShowResult(currentQuestion);
        AnswerTextBox.IsEnabled = false;
        UpdateAnswerCardStyles();

        // Record data
        var similarity = JudgeAnswer.CalculateSimilarity(currentQuestion);
        var duration = DateTime.Now - _startTime;
        var rate = currentQuestion.UserAnswer.Length / duration.TotalSeconds;
        var rStandard = Config.Configure.RStandard;
        var rRelative = (double)rate / rStandard;

        if (AnswerTextBox.Text.Length <= 10)
            rRelative = -0.3125 * rRelative + 4.125;
        rRelative = rRelative > 1.125d ? 1.125d : rRelative;

        var qValue = Supermemo.PredictQValue(rRelative, similarity);
        var efValue = Supermemo.CalculateEFValue(
            currentQuestion.Question!.EFValue, qValue);

        _questions[_currentQuestionIndex].Question!.ReviewTag.Add(
            new ReviewTag()
            {
                Rate=rRelative,
                Time=DateTime.Now,
                Similarity=similarity,
                QValue = qValue
            });
        currentQuestion.Question!.EFValue = efValue;

        // Play phonk effect
        _latest.Add(isCorrect);
        if (_latest.EqualsTo(false) && Config.Configure.PhonkOptions.EnablePhonk)
            await PlayPhonkEffect();
    }

    private async Task PlayPhonkEffect()
    {
        var num = Random.Shared.Next(1, 10);
        var caveira = $"pack://application:,,,/ReciteHelper;component/Images/Phonk/Caveira/caveira{num}.png";
        string sound = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Phonk", "Soundfx", $"phonk{num}.mp3");

        PhonkImage.Source = new BitmapImage(new Uri(caveira));
        PhonkPlayer.Source = new Uri(sound, UriKind.Absolute);

        ImageTranslate.X = 1000;
        PhonkImage.Opacity = 0;

        var sb = new Storyboard();

        var moveAnim = new DoubleAnimation
        {
            From = 1000,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new BackEase { Amplitude = 0.8, EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(moveAnim, PhonkImage);
        Storyboard.SetTargetProperty(moveAnim, new PropertyPath("RenderTransform.(TranslateTransform.X)"));

        var opacityAnim = new DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(50) };
        Storyboard.SetTarget(opacityAnim, PhonkImage);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Opacity"));

        sb.Children.Add(moveAnim);
        sb.Children.Add(opacityAnim);

        sb.Begin();
        PhonkPlayer.Play();

        await Task.Delay(5000);

        PhonkPlayer.Stop();
        PhonkImage.Source = null;
        PhonkImage.Opacity = 0;
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

            _startTime = DateTime.Now;
        }
    }

    private void AnswerTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // TODO: AI verification
        // Spending this extra money is not worthwhile and requires further consideration.
    }


    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_project is null) return;

        // Save record
        var chapter = _project.Chapters!.Find(x => x.Name == _chapterName)!;
        for (int i = 0; i < _questions.Count; i++)
        {
            chapter.Questions![i].UserAnswer = _questions[i].UserAnswer;
            chapter.Questions![i].Status = _questions[i].Status switch
            {
                AnswerStatus.NotAnswered => null,
                AnswerStatus.Correct => true,
                AnswerStatus.Wrong => false,
                _ => throw new NotImplementedException("Fuck U")
            };

        }

        var path = Path.Combine(_project.StoragePath!, _project.ProjectName!, _project.ProjectName!);
        var json = System.Text.Json.JsonSerializer.Serialize(_project,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText($"{path}.rhproj", json);
    }
}