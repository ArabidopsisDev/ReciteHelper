using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Utilities;
using ReciteHelper.Wpf.Services;
using ReciteHelper.Wpf.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace ReciteHelper.Wpf.Views;

public partial class QuizView : UserControl, INotifyPropertyChanged
{
    private bool _grading;
    private bool _recordsNeedSave;
    private bool _personalizationPending;
    private readonly IQuizService _quizService;
    private readonly IProjectFileService _projectFileService;
    private readonly IReviewPersonalizationService _reviewPersonalizationService;
    private readonly IRuleReviewService? _ruleReviewService;
    private readonly ObservableCollection<QuestionItem> _questions = [];
    private int _nextQuestionOrder;
    private readonly LatestBuffer<bool> _latest;
    private int _currentQuestionIndex = 0;
    private int _activeQuestionCount;
    private readonly Project _project;
    private readonly Stopwatch _answerClock = new();
    private string? _selectedChoiceId;
    private string? _selectedTrueFalseAnswer;
    private List<FillBlankAnswerItem> _fillBlankAnswers = [];
    internal Project Project => _project;
    internal Chapter? Chapter { get; }
    internal bool HasQuestions => _questions.Count > 0;
    internal bool HasCloseWork => _recordsNeedSave || _personalizationPending;
    internal bool IsBusy => _grading || !IsEnabled;
    internal StudyActivityContext ActivityContext
    {
        get
        {
            if (GradingReview.IsVisible) return new(GradingReview, true, true);
            if (IsBusy) return default;
            var current = _questions.ElementAtOrDefault(_currentQuestionIndex);
            var bucket = current is { IsArchived: false, Status: AnswerStatus.NotAnswered } ? current.Activity : null;
            return new(this, true, HasQuestions, bucket);
        }
    }
    internal event EventHandler? RecordsChanged;
    internal event EventHandler<IReadOnlySet<Question>?>? RecordsInvalidated;
    internal event EventHandler? NotesRequested;
    internal event EventHandler? HelpRequested;
    internal event EventHandler? HelpTargetChanged;

    internal QuestionItem? HelpTarget => _questions.ElementAtOrDefault(_currentQuestionIndex) is { Status: not AnswerStatus.NotAnswered } item ? item : null;
    public string ChoiceGroupName { get; } = Guid.NewGuid().ToString();

    internal QuizView(
        Project project,
        Chapter? chapter,
        List<Question> questions,
        IQuizService quizService,
        IProjectFileService projectFileService,
        IReviewPersonalizationService reviewPersonalizationService,
        IRuleReviewService? ruleReviewService = null,
        QuickReviewService? quickReviewService = null)
    {
        _reviewPersonalizationService = reviewPersonalizationService;
        _quizService = quizService;
        _projectFileService = projectFileService;
        _ruleReviewService = ruleReviewService;
        _project = project;
        Chapter = chapter;

        InitializeComponent();
        DataContext = this;
        GradingReview.Attach(quickReviewService, project);
        Loaded += (_, _) => PreloadQuickReview();

        ManageRuleQuestionsButton.Visibility = CompactManageButton.Visibility =
            project.TemplateType == ProjectTemplateType.RuleBasedReview ? Visibility.Visible : Visibility.Collapsed;
        _latest = LatestBuffer<bool>.Create<bool>(Config.Configure.PhonkOptions.WrongCount);

        foreach (var question in questions)
            AddQuestion(question);
        AddSmartArchives();
        OrderQuestions();
        AnswerCardItemsControl.ItemsSource = CompactCardItems.ItemsSource = _questions;
        SizeChanged += (_, e) => ResizeQuestionLayout(e.NewSize.Width);
        IsVisibleChanged += (_, _) => UpdateAnswerClock();
        IsEnabledChanged += (_, _) => UpdateAnswerClock();
        LocateCurrent();
        UpdateDisplay();
    }

    private void ManageRuleQuestions_Click(object sender, RoutedEventArgs e)
    {
        CompactCardPopup.IsOpen = false;
        if (_grading || _project.TemplateType != ProjectTemplateType.RuleBasedReview || _ruleReviewService is null) return;
        new CreateRuleReviewWindow(_ruleReviewService, _projectFileService, _ => Task.CompletedTask, _project)
            { Owner = Window.GetWindow(this) }.ShowDialog();
        RefreshRecords();
        RecordsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SwitchToQuestion(int questionNumber)
    {
        if (_grading) return;
        if (questionNumber < 1 || questionNumber > _questions.Count)
            return;

        int targetIndex = questionNumber - 1;

        if (targetIndex == _currentQuestionIndex)
            return;

        CaptureCurrentDraft();
        _currentQuestionIndex = targetIndex;
        UpdateDisplay();
    }

    private void SwitchButton_Click(object sender, RoutedEventArgs e)
    {
        CompactCardPopup.IsOpen = false;
        if (sender is Button button && button.CommandParameter is int questionNumber)
        {
            SwitchToQuestion(questionNumber);
        }
    }

    private void CompactCard_Click(object sender, RoutedEventArgs e) => CompactCardPopup.IsOpen = !CompactCardPopup.IsOpen;

    private void ResizeQuestionLayout(double width)
    {
        var compact = width < 645;
        AnswerCardColumn.Width = new GridLength(compact ? 0 : 176);
        AnswerCard.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactCardButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        if (!compact) CompactCardPopup.IsOpen = false;
        NavigationBorder.Padding = ActionsBorder.Padding = new Thickness(compact ? 14 : 28, compact ? 6 : 12, compact ? 14 : 28, compact ? 6 : 12);
        QuestionAnswerScroll.Padding = new Thickness(compact ? 14 : 28, compact ? 14 : 24, compact ? 14 : 28, compact ? 14 : 24);
        SetActionRow(NavigationActions, compact && width < 455);
        SetActionRow(SubmissionActions, compact && width < 355);
    }

    private static void SetActionRow(FrameworkElement actions, bool below)
    {
        Grid.SetRow(actions, below ? 1 : 0);
        Grid.SetColumn(actions, below ? 0 : 1);
        Grid.SetColumnSpan(actions, below ? 2 : 1);
    }

    private static AnswerStatus RecordedStatus(Question question) => (question.AnswerCleared ? null : question.Status) switch
    {
        true => AnswerStatus.Correct,
        false => AnswerStatus.Wrong,
        null => AnswerStatus.NotAnswered
    };

    private void AddQuestion(Question question)
    {
        var status = Chapter is null && !question.IsArchived ? AnswerStatus.NotAnswered : RecordedStatus(question);
        var assessment = status == AnswerStatus.NotAnswered ? null : question.LastAiAssessment;
        _questions.Add(new QuestionItem
        {
            Number = _questions.Count + 1,
            Question = question,
            OriginalOrder = _nextQuestionOrder++,
            IsArchived = question.IsArchived,
            UserAnswer = status == AnswerStatus.NotAnswered ? null : question.SubmittedAnswer ?? assessment?.UserAnswer,
            Status = status,
            Assessment = assessment
        });
    }

    private void AddSmartArchives()
    {
        if (Chapter is not null) return;
        var listed = _questions.Select(item => item.Question).ToHashSet();
        foreach (var question in ReviewQuestionCatalog.ArchiveHeads(_project))
            if (listed.Add(question)) AddQuestion(question);
    }

    private void OrderQuestions()
    {
        var ordered = _questions.OrderBy(item => item.IsArchived).ThenBy(item => item.OriginalOrder).ToArray();
        _activeQuestionCount = ordered.Count(item => !item.IsArchived);
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            if (!ReferenceEquals(_questions[index], item)) _questions.Move(_questions.IndexOf(item), index);
            item.Number = index + 1;
        }
    }

    internal void RefreshRecords(IReadOnlySet<Question>? cleared = null)
    {
        var current = _questions.ElementAtOrDefault(_currentQuestionIndex);
        var currentChanged = false;
        if (Chapter is { } chapter)
        {
            var questions = new HashSet<Question>(_questions.Select(item => item.Question!), ReferenceEqualityComparer.Instance);
            foreach (var question in chapter.Questions ?? [])
                if (questions.Add(question)) AddQuestion(question);
        }
        else
        {
            // A full answer in another tab can replace the facets in this plan.
            var available = new HashSet<Question>(ReviewQuestionCatalog.All(_project), ReferenceEqualityComparer.Instance);
            var archivedDescendants = ReviewQuestionCatalog.Roots(_project).Where(question => question.IsArchived)
                .SelectMany(question => question.ReviewFacets.Select(facet => facet.Question)).ToHashSet();
            foreach (var item in _questions.Where(item => !available.Contains(item.Question!) ||
                         archivedDescendants.Contains(item.Question!) || !item.Question!.IsArchived && (
                         !ReviewQuestionCatalog.HasAnswerRecord(item.Question) ||
                         item.Status == AnswerStatus.NotAnswered && item.Question.ReviewFacets.Count > 0)).ToArray())
                _questions.Remove(item);
            AddSmartArchives();
        }

        foreach (var item in _questions)
        {
            var question = item.Question!;
            var archiveChanged = question.IsArchived != item.IsArchived;
            item.IsArchived = question.IsArchived;
            currentChanged |= archiveChanged && ReferenceEquals(item, current);
            var status = RecordedStatus(question);
            var wasCleared = cleared?.Contains(question) == true;
            if (Chapter is null && !archiveChanged && !item.IsArchived && !wasCleared && status != AnswerStatus.NotAnswered)
                continue;
            var assessment = status == AnswerStatus.NotAnswered ? null : question.LastAiAssessment;
            if (wasCleared)
            {
                item.DraftText = null;
                item.DraftParts = null;
                item.AnswerElapsed = TimeSpan.Zero;
                item.Activity.Total = default;
                currentChanged |= ReferenceEquals(item, current);
            }
            var answer = status == AnswerStatus.NotAnswered ? null : question.SubmittedAnswer ?? assessment?.UserAnswer;
            if (status == item.Status && ReferenceEquals(assessment, item.Assessment) && answer == item.UserAnswer) continue;
            item.Status = status;
            item.Assessment = assessment;
            item.UserAnswer = answer;
            currentChanged |= ReferenceEquals(item, current);
        }

        OrderQuestions();
        var index = current is null ? -1 : _questions.IndexOf(current);
        _currentQuestionIndex = index < 0 ? 0 : index;
        if (index < 0 || currentChanged)
            UpdateDisplay();
        else
        {
            UpdateAnswerHistory(current!);
            UpdateNavigation();
        }
    }

    private void UpdateDisplay()
    {
        if (IsLoaded) App.StudyActivity?.Checkpoint();
        _answerClock.Reset();
        var hasQuestions = _questions.Count != 0;
        PracticeLayout.Visibility = hasQuestions ? Visibility.Visible : Visibility.Collapsed;
        PracticeEmptyText.Visibility = hasQuestions ? Visibility.Collapsed : Visibility.Visible;
        if (!hasQuestions)
        {
            HelpTargetChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var currentQuestion = _questions[_currentQuestionIndex];

        var question = currentQuestion.Question!;
        QuestionTextBlock.Text = EssayAnswerLayout.Stem(question, EssayAnswerLayout.Parts(question));
        var facetSource = ReviewQuestionCatalog.FindFacet(_project, currentQuestion.Question);
        FacetContextText.Visibility = facetSource is null ? Visibility.Collapsed : Visibility.Visible;
        if (facetSource is { } source)
        {
            FacetContextText.Text = $"cFSRS 切面复习：{source.Facet.Name}（占原题 {source.Facet.Weight:P0}）";
            FacetContextText.ToolTip = $"原题：{source.Parent.Text}\n占比依据：{source.Facet.WeightReason}";
        }
        QDisplayLabel.Content = null;

        UpdateAnswerHistory(currentQuestion);
        ConfigureAnswerInput(currentQuestion);
        SubmitButton.IsEnabled = !currentQuestion.IsArchived && currentQuestion.Status == AnswerStatus.NotAnswered;
        HelpButton.IsEnabled = currentQuestion.Status != AnswerStatus.NotAnswered;

        // Hide the results area (if it's a new question)
        if (currentQuestion.Status == AnswerStatus.NotAnswered)
        {
            ResultArea.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowResult(currentQuestion);
        }

        UpdateNavigation();
        HelpTargetChanged?.Invoke(this, EventArgs.Empty);
        if (IsLoaded) PreloadQuickReview();
    }

    private Guid TopicOf(Question question) => ReviewQuestionCatalog.FindFacet(_project, question)?.Parent.ReviewIdentity
        ?? question.ReviewIdentity;

    private void PreloadQuickReview()
    {
        var item = _questions.ElementAtOrDefault(_currentQuestionIndex);
        if (item is not { IsArchived: false, Status: AnswerStatus.NotAnswered })
            item = _questions.FirstOrDefault(question => !question.IsArchived && question.Status == AnswerStatus.NotAnswered);
        if (item?.Question is { } question)
            GradingReview.Preload(TopicOf(question));
    }

    private void UpdateNavigation()
    {
        var current = _questions[_currentQuestionIndex];
        var isArchived = current.IsArchived;
        var archivedCount = _questions.Count - _activeQuestionCount;
        QuestionPosition.Text = isArchived
            ? $"归档 {_currentQuestionIndex + 1 - _activeQuestionCount} / {archivedCount}"
            : $"第 {_currentQuestionIndex + 1} 题 / 共 {_activeQuestionCount} 题";
        ArchiveBanner.Visibility = isArchived ? Visibility.Visible : Visibility.Collapsed;
        if (isArchived)
        {
            var facets = current.Question!.ReviewFacets;
            ArchiveSummary.Text = facets.Count == 0 ? "已归档" : $"已归档，包含 {facets.Count} 个切面";
            ArchiveSummary.ToolTip = facets.Count == 0 ? null : string.Join("\n", facets.Select(facet => facet.Name));
        }
        RestoreAllButton.Visibility = archivedCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = _activeQuestionCount > 0;
        PrevButton.IsEnabled = _currentQuestionIndex > 0;
        NextButton.IsEnabled = _currentQuestionIndex < _questions.Count - 1;
        SkipButton.IsEnabled = !isArchived && _currentQuestionIndex < _activeQuestionCount - 1;
        UpdateAnswerCardStyles();
    }

    private void ConfigureAnswerInput(QuestionItem currentQuestion)
    {
        PartResults.Assessment = null;
        InlineAnswerFeedbackBorder.Visibility = Visibility.Collapsed;
        AiReferenceExpander.Visibility = Visibility.Collapsed;
        AiReferenceExpander.IsExpanded = false;
        var question = currentQuestion.Question!;
        var isEnabled = !currentQuestion.IsArchived && currentQuestion.Status == AnswerStatus.NotAnswered;
        var answer = isEnabled ? currentQuestion.DraftText : currentQuestion.UserAnswer;
        var draftParts = isEnabled ? currentQuestion.DraftParts : null;

        _selectedChoiceId = null;
        _selectedTrueFalseAnswer = null;
        _fillBlankAnswers = [];
        AnswerTextBox.Visibility = Visibility.Collapsed;
        MultipartAnswers.Visibility = Visibility.Collapsed;
        ChoiceOptionsItemsControl.Visibility = Visibility.Collapsed;
        FillBlankAnswersItemsControl.Visibility = Visibility.Collapsed;
        TrueFalseOptionsPanel.Visibility = Visibility.Collapsed;
        ChoiceOptionsItemsControl.ItemsSource = null;
        FillBlankAnswersItemsControl.ItemsSource = null;

        if (question.IsSingleChoice)
        {
            _selectedChoiceId = Question.ExtractOptionId(answer);
            AnswerPromptText.Text = "请选择答案：";
            AnswerTextBox.Text = "";
            AnswerTextBox.Visibility = Visibility.Collapsed;
            ChoiceOptionsItemsControl.Visibility = Visibility.Visible;
            ChoiceOptionsItemsControl.IsEnabled = isEnabled;
            ChoiceOptionsItemsControl.ItemsSource = question.Options;
        }
        else if (question.IsFillBlank)
        {
            var savedAnswers = draftParts ?? Question.SplitBlankAnswers(answer);
            _fillBlankAnswers = Enumerable.Range(0, question.BlankCount)
                .Select(index => new FillBlankAnswerItem
                {
                    Number = index + 1,
                    Text = index < savedAnswers.Count ? savedAnswers[index] : string.Empty
                })
                .ToList();
            AnswerPromptText.Text = question.BlankCount == 1 ? "请在横线上填写答案：" : "请按顺序填写各空：";
            FillBlankAnswersItemsControl.ItemsSource = _fillBlankAnswers;
            FillBlankAnswersItemsControl.Visibility = Visibility.Visible;
            FillBlankAnswersItemsControl.IsEnabled = isEnabled;
        }
        else if (question.IsTrueFalse)
        {
            _selectedTrueFalseAnswer = Question.NormalizeTrueFalseAnswer(answer);
            AnswerPromptText.Text = "请判断下列说法：";
            TrueOptionRadioButton.IsChecked = _selectedTrueFalseAnswer == "正确";
            FalseOptionRadioButton.IsChecked = _selectedTrueFalseAnswer == "错误";
            TrueFalseOptionsPanel.Visibility = Visibility.Visible;
            TrueFalseOptionsPanel.IsEnabled = isEnabled;
        }
        else if (EssayAnswerLayout.Parts(question).Count > 1 &&
            (isEnabled || !EssayAnswerLayout.NeedsFacetRegrouping(question)))
        {
            AnswerPromptText.Text = "请分别回答各问，可暂时留空不会的部分：";
            MultipartAnswers.SetQuestion(question, answer, isEnabled, draft: draftParts);
            MultipartAnswers.Visibility = Visibility.Visible;
        }
        else
        {
            AnswerPromptText.Text = question.IsTermDefinition ? "请输入名词解释：" : "请输入解答：";
            AnswerTextBox.Visibility = Visibility.Visible;
            AnswerTextBox.Height = question.IsTermDefinition ? 120d : 180d;
            AnswerTextBox.Text = answer ?? string.Empty;
            AnswerTextBox.IsEnabled = isEnabled;
        }

        UpdateAnswerClock();
    }

    private void UpdateAnswerClock()
    {
        if (IsVisible && IsEnabled && !_grading &&
            _questions.ElementAtOrDefault(_currentQuestionIndex) is { IsArchived: false, Status: AnswerStatus.NotAnswered })
            _answerClock.Start();
        else
            _answerClock.Stop();
    }

    private void UpdateAnswerCardStyles()
    {
        var answered = _questions.Count(item => !item.IsArchived && item.Status != AnswerStatus.NotAnswered);
        var archives = _questions.Count - _activeQuestionCount;
        StatsTextBlock.Text = $"已作答 {answered} / {_activeQuestionCount}" +
            (archives > 0 ? $"\n已归档 {archives} 道" : string.Empty);
        foreach (var question in _questions)
        {
            // Reset to basic style
            var style = question.IsArchived ? "ArchivedAnswerStyle" : question.Status switch
            {
                AnswerStatus.Correct => "CorrectAnswerStyle",
                AnswerStatus.Wrong => "WrongAnswerStyle",
                _ => "AnswerCardButtonStyle"
            };
            question.StatusStyle = (Style)FindResource(style);

            // If this is the current question, add a border style
            if (question.Number == _currentQuestionIndex + 1)
            {
                var currentStyle = new Style(typeof(Button), question.StatusStyle);
                currentStyle.Setters.Add(new Setter(Button.BorderBrushProperty, FindResource("ArcAccent")));
                currentStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(2)));
                question.StatusStyle = currentStyle;
            }
        }
    }

    private async void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_grading || _activeQuestionCount == 0) return;
        var dialog = new ClearReviewRecordsWindow(_questions.Where(item => !item.IsArchived)) { Owner = Window.GetWindow(this) };
        _answerClock.Stop();
        var confirmed = dialog.ShowDialog() is true;
        UpdateAnswerClock();
        if (!confirmed) return;

        CaptureCurrentDraft();
        var affected = dialog.QuestionsToClear.Count > 0
            ? ReviewHistory.Clear(_project, dialog.QuestionsToClear, dialog.AnswersOnly)
            : null;
        if (dialog.QuestionsToArchive.Count > 0)
            SetArchiveState(dialog.QuestionsToArchive, archived: true);
        await PersistPracticeChangesAsync(affected);
    }

    private async void RestoreCurrent_Click(object sender, RoutedEventArgs e)
        => await SetArchiveAsync([_questions[_currentQuestionIndex].Question!], archived: false);

    private async void RestoreAll_Click(object sender, RoutedEventArgs e)
        => await SetArchiveAsync(_questions.Where(item => item.IsArchived).Select(item => item.Question!), archived: false);

    private async Task SetArchiveAsync(IEnumerable<Question> questions, bool archived)
    {
        SetArchiveState(questions, archived);
        await PersistPracticeChangesAsync(null);
    }

    private void SetArchiveState(IEnumerable<Question> questions, bool archived)
    {
        foreach (var question in questions)
            question.IsArchived = archived;
        _project.NotifyPracticeArchiveChanged();
    }

    private async Task PersistPracticeChangesAsync(IReadOnlySet<Question>? cleared)
    {
        _recordsNeedSave = true;
        RefreshRecords(cleared);
        RecordsInvalidated?.Invoke(this, cleared);
        IsEnabled = false;
        try
        {
            await SaveRecordsAsync();
        }
        catch (Exception ex)
        {
            RecordsChanged?.Invoke(this, EventArgs.Empty);
            MessageBox.Show(Window.GetWindow(this), $"保存答题记录失败：{ex.Message}", "保存失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void UpdateAnswerHistory(QuestionItem item) => AnswerHistory.SetQuestion(item.Question);

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

        UserAnswerText.Text = question.Question.IsFillBlank
            ? string.Join("；", Question.SplitBlankAnswers(question.UserAnswer).Select((answer, index) => $"{index + 1}. {answer}"))
            : question.UserAnswer ?? "";
        CorrectAnswerText.Text = question.Question.GetCorrectAnswerText();
        if (question.Assessment is { } assessment && assessment.UserAnswer == question.UserAnswer)
        {
            PartResults.Assessment = EssayAnswerLayout.NeedsFacetRegrouping(question.Question) ? null : assessment;
            InlineAnswerFeedback.Assessment = assessment;
            InlineAnswerFeedbackBorder.Visibility = Visibility.Visible;
            if (MultipartAnswers.Visibility == Visibility.Visible)
            {
                MultipartAnswers.SetAssessment(assessment);
                InlineAnswerFeedbackBorder.Visibility = Visibility.Collapsed;
            }
            AnswerTextBox.Visibility = Visibility.Collapsed;
            ChoiceOptionsItemsControl.Visibility = Visibility.Collapsed;
            FillBlankAnswersItemsControl.Visibility = Visibility.Collapsed;
            TrueFalseOptionsPanel.Visibility = Visibility.Collapsed;
            var metric = assessment.AnswerNature.HasFlag(AnswerNature.Expressive) ? "判分" : "核心要点覆盖";
            AnswerPromptText.Text = $"您的答案：得分 {assessment.ScoreRatio:P0}" +
                (assessment.RawScoreRatio is { } raw && Math.Abs(raw - assessment.ScoreRatio) > 0.001 ? $"（{metric} {raw:P0}）" : string.Empty);
            AiReferenceText.Text = question.Question.GetCorrectAnswerText();
            AiReferenceExpander.Visibility = Visibility.Visible;
            ResultArea.Visibility = Visibility.Collapsed;
        }
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
        => HelpRequested?.Invoke(this, EventArgs.Empty);

    private void OpenNotesButton_Click(object sender, RoutedEventArgs e)
        => NotesRequested?.Invoke(this, EventArgs.Empty);

    private void LocateCurrent()
    {
        for (int i = 0; i < _activeQuestionCount; i++)
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
        if (_grading || _questions.Count == 0) return;
        var currentQuestion = _questions[_currentQuestionIndex];
        if (currentQuestion.IsArchived || currentQuestion.Status != AnswerStatus.NotAnswered) return;
        var answerText = GetCurrentAnswerText(currentQuestion);

        if (string.IsNullOrWhiteSpace(answerText))
        {
            MessageBox.Show("请输入答案", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        App.StudyActivity?.Checkpoint();
        var activity = currentQuestion.Activity.Total;
        _grading = true;
        PracticeLayout.IsEnabled = false;
        App.StudyActivity?.Checkpoint();
        _answerClock.Stop();
        var startedAt = DateTime.Now - currentQuestion.AnswerElapsed - _answerClock.Elapsed;
        SubmitButton.Content = "正在批改…";
        var succeeded = false;
        try
        {
            currentQuestion.UserAnswer = answerText;

            var pendingGrade = _quizService.ProcessAnswerAsync(
                _project,
                currentQuestion.Question!,
                currentQuestion.UserAnswer,
                startedAt);
            if (!pendingGrade.IsCompleted) GradingReview.Begin(TopicOf(currentQuestion.Question!));
            var answerResult = await pendingGrade;

            if (activity.ForegroundSeconds > 0) answerResult.ReviewTag.StudyActivity = activity;
            currentQuestion.Status = answerResult.IsCorrect ? AnswerStatus.Correct : AnswerStatus.Wrong;
            currentQuestion.DraftText = null;
            currentQuestion.DraftParts = null;
            currentQuestion.AnswerElapsed = TimeSpan.Zero;
            currentQuestion.Activity.Total = default;
            _answerClock.Reset();
            currentQuestion.Assessment = answerResult.AiAssessment;
            currentQuestion.Question!.Status = answerResult.IsCorrect;
            _recordsNeedSave = true;
            _personalizationPending = true;

            // Show result
            UpdateAnswerHistory(currentQuestion);
            ShowResult(currentQuestion);
            HelpButton.IsEnabled = true;
            HelpTargetChanged?.Invoke(this, EventArgs.Empty);
            AnswerTextBox.IsEnabled = false;
            ChoiceOptionsItemsControl.IsEnabled = false;
            FillBlankAnswersItemsControl.IsEnabled = false;
            TrueFalseOptionsPanel.IsEnabled = false;
            UpdateAnswerCardStyles();

            QDisplayLabel.Content = MemoryHintFormatter.Format(answerResult);

            await SaveRecordsAsync();
            succeeded = true;

            // Play phonk effect
            _latest.Add(answerResult.IsCorrect);
            if (_latest.EqualsTo(false) && Config.Configure.PhonkOptions.EnablePhonk)
                await PlayPhonkEffect();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"提交未完成：{ex.Message}", "批改提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _grading = false;
            PracticeLayout.IsEnabled = true;
            GradingReview.Finish(succeeded);
            App.StudyActivity?.Checkpoint();
            UpdateAnswerClock();
            SubmitButton.Content = "提交答案";
            SubmitButton.IsEnabled = currentQuestion.Status == AnswerStatus.NotAnswered;
            if (succeeded) PreloadQuickReview();
        }
    }

    private string GetCurrentAnswerText(QuestionItem currentQuestion)
    {
        var question = currentQuestion.Question!;
        if (question.IsSingleChoice)
            return question.GetOptionDisplayText(_selectedChoiceId);

        if (question.IsFillBlank)
        {
            return _fillBlankAnswers.Any(answer => string.IsNullOrWhiteSpace(answer.Text))
                ? string.Empty
                : Question.JoinBlankAnswers(_fillBlankAnswers.Select(answer => answer.Text));
        }

        if (question.IsTrueFalse)
            return _selectedTrueFalseAnswer ?? string.Empty;

        if (EssayAnswerLayout.Parts(question).Count > 1) return MultipartAnswers.AnswerText;

        return AnswerTextBox.Text.Trim();
    }

    private async Task PlayPhonkEffect()
    {
        var num = Random.Shared.Next(1, 10);
        var caveira = $"pack://application:,,,/ReciteHelper.Wpf;component/Images/Phonk/Caveira/caveira{num}.png";
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
        => SwitchToQuestion(_currentQuestionIndex + 2);

    private void PrevButton_Click(object sender, RoutedEventArgs e)
        => SwitchToQuestion(_currentQuestionIndex);

    private void NextButton_Click(object sender, RoutedEventArgs e)
        => SwitchToQuestion(_currentQuestionIndex + 2);

    private void CaptureCurrentDraft()
    {
        var item = _questions[_currentQuestionIndex];
        if (item.IsArchived || item.Status != AnswerStatus.NotAnswered) return;

        item.AnswerElapsed += _answerClock.Elapsed;
        _answerClock.Reset();
        UpdateAnswerClock();
        var question = item.Question!;
        if (question.IsFillBlank)
            item.DraftParts = _fillBlankAnswers.Select(answer => answer.Text).ToArray();
        else if (MultipartAnswers.Visibility == Visibility.Visible)
            item.DraftParts = MultipartAnswers.AnswerTexts;
        else if (question.IsSingleChoice)
            item.DraftText = _selectedChoiceId;
        else if (question.IsTrueFalse)
            item.DraftText = _selectedTrueFalseAnswer;
        else
            item.DraftText = AnswerTextBox.Text;
    }

    private void ChoiceOption_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radioButton)
            radioButton.IsChecked = _selectedChoiceId is not null && radioButton.Tag?.ToString() == _selectedChoiceId;
    }

    private void ChoiceOption_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radioButton)
            _selectedChoiceId = radioButton.Tag?.ToString();
    }

    private void TrueFalseOption_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radioButton)
            _selectedTrueFalseAnswer = radioButton.Tag?.ToString();
    }


    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private async Task SaveRecordsAsync()
    {
        await _projectFileService.SaveProjectAsync(_project);
        _recordsNeedSave = false;
        RecordsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal async Task<bool> SaveOnCloseAsync()
    {
        try
        {
            if (_recordsNeedSave)
                await SaveRecordsAsync();

            if (_personalizationPending)
            {
                _personalizationPending = false;
                try
                {
                    var fit = await _reviewPersonalizationService.TryPersonalizeAsync(_project);
                    if (fit is not null)
                    {
                        await _projectFileService.SaveProjectAsync(_project);
                        RecordsChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (Exception)
                {
                    // Personalization is optional once the answer records are saved.
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"记录未能保存，请重试。\n{ex.Message}", "保存失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
