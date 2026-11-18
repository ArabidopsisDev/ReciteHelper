using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Services;
using ReciteHelper.Core.Scheduling;
using ReciteHelper.Wpf.Models;
using ReciteHelper.Wpf.Utilities;
using ReciteHelper.Wpf.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private readonly IQuestionHelpService _questionHelpService;
    private readonly IReviewPersonalizationService _reviewPersonalizationService;
    private readonly IRuleReviewService? _ruleReviewService;
    private readonly ObservableCollection<QuestionItem> _questions = [];
    private readonly LatestBuffer<bool> _latest;
    private int _currentQuestionIndex = 0;
    private int _totalQuestions = 0;
    private readonly Project _project;
    private DateTime _startTime = DateTime.Now;
    private string? _selectedChoiceId;
    private string? _selectedTrueFalseAnswer;
    private List<FillBlankAnswerItem> _fillBlankAnswers = [];
    private IReadOnlyList<KnowledgeBaseMatch> _helpMatches = [];
    private CancellationTokenSource? _helpCancellation;
    private static readonly Brush HighlightBackground = new SolidColorBrush(Color.FromRgb(205, 250, 224));

    private sealed record HighlightedKnowledgeMatch(
        string Title,
        IReadOnlyList<HighlightedTextSegment> ContentSegments);

    private sealed record HighlightedTextSegment(string Text, bool IsHighlighted);

    private readonly record struct NormalizedChar(char Value, int OriginalIndex);

    private readonly record struct HighlightSpan(int Start, int End)
    {
        public int Length => End - Start;
    }

    internal Project Project => _project;
    internal Chapter? Chapter { get; }
    internal bool HasCloseWork => _recordsNeedSave || _personalizationPending;
    internal event EventHandler? CloseRequested;
    internal event EventHandler? RecordsChanged;
    public string ChoiceGroupName { get; } = Guid.NewGuid().ToString();

    internal QuizView(
        Project project,
        Chapter? chapter,
        List<Question> questions,
        IQuizService quizService,
        IProjectFileService projectFileService,
        IQuestionHelpService questionHelpService,
        IReviewPersonalizationService reviewPersonalizationService,
        IRuleReviewService? ruleReviewService = null)
    {
        _reviewPersonalizationService = reviewPersonalizationService;
        _quizService = quizService;
        _projectFileService = projectFileService;
        _questionHelpService = questionHelpService;
        _ruleReviewService = ruleReviewService;
        _project = project;
        Chapter = chapter;

        InitializeComponent();
        DataContext = this;

        ManageRuleQuestionsButton.Visibility = project.TemplateType == ProjectTemplateType.RuleBasedReview ? Visibility.Visible : Visibility.Collapsed;
        _latest = LatestBuffer<bool>.Create<bool>(Config.Configure.PhonkOptions.WrongCount);

        foreach (var question in questions)
            AddQuestion(question);
        _totalQuestions = _questions.Count;
        AnswerCardItemsControl.ItemsSource = _questions;
        LocateCurrent();
        UpdateDisplay();
    }

    private void ManageRuleQuestions_Click(object sender, RoutedEventArgs e)
    {
        if (_grading || _project.TemplateType != ProjectTemplateType.RuleBasedReview || _ruleReviewService is null) return;
        new CreateRuleReviewWindow(_ruleReviewService, _projectFileService, _ => Task.CompletedTask, _project)
            { Owner = Window.GetWindow(this) }.ShowDialog();
        RefreshRecords();
        RecordsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SwitchToQuestion(int questionNumber)
    {
        if (_grading) return;
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

    private static AnswerStatus RecordedStatus(Question question) => question.Status switch
    {
        true => AnswerStatus.Correct,
        false => AnswerStatus.Wrong,
        null => AnswerStatus.NotAnswered
    };

    private void AddQuestion(Question question)
    {
        var status = Chapter is null ? AnswerStatus.NotAnswered : RecordedStatus(question);
        var assessment = status == AnswerStatus.NotAnswered ? null : question.LastAiAssessment;
        _questions.Add(new QuestionItem
        {
            Number = _questions.Count + 1,
            Question = question,
            UserAnswer = assessment?.UserAnswer,
            Status = status,
            Assessment = assessment
        });
    }

    internal void RefreshRecords()
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
            foreach (var item in _questions.Where(item =>
                         !available.Contains(item.Question!) ||
                         !ReviewQuestionCatalog.HasAnswerRecord(item.Question!) ||
                         item.Status == AnswerStatus.NotAnswered && item.Question!.ReviewFacets.Count > 0).ToArray())
                _questions.Remove(item);
        }

        foreach (var item in _questions)
        {
            var status = RecordedStatus(item.Question!);
            if (Chapter is null && status != AnswerStatus.NotAnswered) continue;
            var assessment = status == AnswerStatus.NotAnswered ? null : item.Question!.LastAiAssessment;
            if (status == item.Status && ReferenceEquals(assessment, item.Assessment)) continue;
            item.Status = status;
            item.Assessment = assessment;
            item.UserAnswer = assessment?.UserAnswer;
            currentChanged |= ReferenceEquals(item, current);
        }

        _totalQuestions = _questions.Count;
        for (int i = 0; i < _questions.Count; i++)
            _questions[i].Number = i + 1;
        var index = current is null ? -1 : _questions.IndexOf(current);
        _currentQuestionIndex = index < 0 ? 0 : index;
        if (index < 0 || currentChanged)
            UpdateDisplay();
        else
            UpdateNavigation();
    }

    private void UpdateDisplay()
    {
        ResetHelpPanel();
        var hasQuestions = _questions.Count != 0;
        PracticeLayout.Visibility = hasQuestions ? Visibility.Visible : Visibility.Collapsed;
        PracticeEmptyText.Visibility = hasQuestions ? Visibility.Collapsed : Visibility.Visible;
        if (!hasQuestions) return;

        var currentQuestion = _questions[_currentQuestionIndex];

        QuestionTextBlock.Text = currentQuestion.Question!.Text;
        var facetSource = ReviewQuestionCatalog.FindFacet(_project, currentQuestion.Question);
        FacetContextText.Visibility = facetSource is null ? Visibility.Collapsed : Visibility.Visible;
        if (facetSource is { } source)
        {
            FacetContextText.Text = $"cFSRS 切面复习：{source.Facet.Name}（占原题 {source.Facet.Weight:P0}）";
            FacetContextText.ToolTip = $"原题：{source.Parent.Text}\n占比依据：{source.Facet.WeightReason}";
        }
        QDisplayLabel.Content = null;

        ConfigureAnswerInput(currentQuestion);
        SubmitButton.IsEnabled = currentQuestion.Status == AnswerStatus.NotAnswered;
        AskDeepSeekButton.IsEnabled = currentQuestion.Status != AnswerStatus.NotAnswered;

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
    }

    private void UpdateNavigation()
    {
        CurrentQuestionText.Text = (_currentQuestionIndex + 1).ToString();
        TotalQuestionsText.Text = _totalQuestions.ToString();
        PrevButton.IsEnabled = _currentQuestionIndex > 0;
        NextButton.IsEnabled = SkipButton.IsEnabled = _currentQuestionIndex < _totalQuestions - 1;
        UpdateAnswerCardStyles();
    }

    private void ConfigureAnswerInput(QuestionItem currentQuestion)
    {
        PartResults.Assessment = null;
        InlineAnswerFeedbackBorder.Visibility = Visibility.Collapsed;
        AiReferenceExpander.Visibility = Visibility.Collapsed;
        AiReferenceExpander.IsExpanded = false;
        var question = currentQuestion.Question!;
        var isEnabled = currentQuestion.Status == AnswerStatus.NotAnswered;

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
            _selectedChoiceId = Question.ExtractOptionId(currentQuestion.UserAnswer);
            AnswerPromptText.Text = "请选择答案：";
            AnswerTextBox.Text = "";
            AnswerTextBox.Visibility = Visibility.Collapsed;
            ChoiceOptionsItemsControl.Visibility = Visibility.Visible;
            ChoiceOptionsItemsControl.IsEnabled = isEnabled;
            ChoiceOptionsItemsControl.ItemsSource = question.Options;
        }
        else if (question.IsFillBlank)
        {
            var savedAnswers = Question.SplitBlankAnswers(currentQuestion.UserAnswer);
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
            _selectedTrueFalseAnswer = Question.NormalizeTrueFalseAnswer(currentQuestion.UserAnswer);
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
            MultipartAnswers.SetQuestion(question, currentQuestion.UserAnswer, isEnabled);
            MultipartAnswers.Visibility = Visibility.Visible;
            MultipartAnswers.AnswerChanged -= MultipartAnswers_AnswerChanged;
            MultipartAnswers.AnswerChanged += MultipartAnswers_AnswerChanged;
        }
        else
        {
            AnswerPromptText.Text = question.IsTermDefinition ? "请输入名词解释：" : "请输入解答：";
            AnswerTextBox.Visibility = Visibility.Visible;
            AnswerTextBox.Height = question.IsTermDefinition ? 120d : 180d;
            AnswerTextBox.Text = currentQuestion.UserAnswer ?? string.Empty;
            AnswerTextBox.IsEnabled = isEnabled;
        }

        if (isEnabled)
            _startTime = DateTime.Now;
    }

    private void UpdateAnswerCardStyles()
    {
        StatsTextBlock.Text = $"已作答 {_questions.Count(item => item.Status != AnswerStatus.NotAnswered)} / {_totalQuestions}";
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
                currentStyle.Setters.Add(new Setter(Button.BorderBrushProperty, FindResource("ArcAccent")));
                currentStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(2)));
                question.StatusStyle = currentStyle;
            }
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("是否清空本标签页所有题目的答题与复习记录？清空后，掌握度将重新计算。", "清空记录",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        ClearRecords();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ClearRecords()
    {
        ReciteHelper.Application.Services.ReviewHistory.Clear(_project, _questions.Select(item => item.Question!));
        _recordsNeedSave = true;
        foreach (var item in _questions)
        {
            item.UserAnswer = null;
            item.Status = AnswerStatus.NotAnswered;
            item.Assessment = null;
        }
        _currentQuestionIndex = 0;
        UpdateDisplay();
    }

    private void ShowResult(QuestionItem question)
    {
        ResultArea.Visibility = Visibility.Visible;
        QuestionHelpButton.Visibility = Visibility.Collapsed;

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
                QuestionHelpButton.Visibility = HasUsableKnowledgeBase
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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
            if (question.Question.SourceExcerpts.Count > 0)
                AiReferenceText.Text += "\n\n资料原文：\n" + string.Join("\n\n", question.Question.SourceExcerpts);
            AiReferenceExpander.Visibility = Visibility.Visible;
            ResultArea.Visibility = Visibility.Collapsed;
        }
    }

    private bool HasUsableKnowledgeBase => _project.KnowledgeBase is { Entries.Count: > 0 };

    private async void QuestionHelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasUsableKnowledgeBase)
            return;

        var questionIndex = _currentQuestionIndex;
        var currentQuestion = _questions[questionIndex];

        _helpCancellation?.Cancel();
        _helpCancellation?.Dispose();
        _helpCancellation = new CancellationTokenSource();

        HelpSidebarColumn.Width = new GridLength(288);
        HelpSidebar.Visibility = Visibility.Visible;
        KnowledgeMatchesItemsControl.ItemsSource = null;
        KnowledgeLoadingText.Text = "正在查询知识库...";
        KnowledgeLoadingText.Visibility = Visibility.Visible;
        AskAiBanner.Visibility = Visibility.Collapsed;
        AiAnswerPanel.Visibility = Visibility.Collapsed;
        _helpMatches = [];

        try
        {
            var matches = await _questionHelpService.FindMatchesAsync(
                _project,
                currentQuestion.Question!,
                _helpCancellation.Token);

            if (questionIndex != _currentQuestionIndex)
                return;

            _helpMatches = matches;
            KnowledgeMatchesItemsControl.ItemsSource = BuildHighlightedMatches(matches, currentQuestion);
            KnowledgeLoadingText.Text = matches.Count == 0
                ? "知识库中没有找到可用的相关内容。"
                : string.Empty;
            KnowledgeLoadingText.Visibility = matches.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            AskAiBanner.Visibility = matches.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            KnowledgeLoadingText.Text = $"知识库查询失败：{ex.Message}";
            KnowledgeLoadingText.Visibility = Visibility.Visible;
        }
    }

    private async void AskAiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_helpMatches.Count == 0)
            return;

        var questionIndex = _currentQuestionIndex;
        var currentQuestion = _questions[questionIndex];
        AskAiBanner.Visibility = Visibility.Collapsed;
        AiAnswerPanel.Visibility = Visibility.Visible;
        AiAnswerText.Text = "正在生成题目解析...";
        AskAiButton.IsEnabled = false;

        try
        {
            var explanation = await _questionHelpService.ExplainAsync(
                currentQuestion.Question!,
                currentQuestion.UserAnswer ?? string.Empty,
                _helpMatches,
                _helpCancellation?.Token ?? CancellationToken.None);

            if (questionIndex == _currentQuestionIndex)
                AiAnswerText.Text = string.IsNullOrWhiteSpace(explanation)
                    ? "暂未生成有效解析。"
                    : explanation.Trim();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AiAnswerText.Text = $"生成解析失败：{ex.Message}";
        }
        finally
        {
            AskAiButton.IsEnabled = true;
        }
    }

    private async void AskDeepSeekButton_Click(object sender, RoutedEventArgs e)
    {
        var questionIndex = _currentQuestionIndex;
        var currentQuestion = _questions[questionIndex];
        if (currentQuestion.Status == AnswerStatus.NotAnswered)
        {
            MessageBox.Show("请先提交答案，再查看题目解析。", "尚未提交答案",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _helpCancellation?.Cancel();
        _helpCancellation?.Dispose();
        _helpCancellation = new CancellationTokenSource();
        var cancellationToken = _helpCancellation.Token;

        HelpSidebarColumn.Width = new GridLength(288);
        HelpSidebar.Visibility = Visibility.Visible;
        KnowledgeMatchesItemsControl.ItemsSource = null;
        KnowledgeLoadingText.Text = "正在查询本地知识库...";
        KnowledgeLoadingText.Visibility = Visibility.Visible;
        AskAiBanner.Visibility = Visibility.Collapsed;
        AiAnswerPanel.Visibility = Visibility.Visible;
        AiAnswerText.Text = "正在生成题目解析...";
        AskDeepSeekButton.IsEnabled = false;

        try
        {
            var matches = await _questionHelpService.FindMatchesAsync(
                _project,
                currentQuestion.Question!,
                cancellationToken);
            if (questionIndex != _currentQuestionIndex)
                return;

            _helpMatches = matches;
            KnowledgeMatchesItemsControl.ItemsSource = BuildHighlightedMatches(matches, currentQuestion);
            KnowledgeLoadingText.Text = matches.Count == 0
                ? "未匹配到本地知识点，将根据题目和答案直接生成解析。"
                : string.Empty;
            KnowledgeLoadingText.Visibility = matches.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            var explanation = await _questionHelpService.ExplainAsync(
                currentQuestion.Question!,
                currentQuestion.UserAnswer ?? string.Empty,
                matches,
                cancellationToken);
            if (questionIndex == _currentQuestionIndex)
                AiAnswerText.Text = string.IsNullOrWhiteSpace(explanation)
                    ? "暂未生成有效解析。"
                    : explanation.Trim();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AiAnswerText.Text = $"生成解析失败：{ex.Message}";
        }
        finally
        {
            if (questionIndex == _currentQuestionIndex)
                AskDeepSeekButton.IsEnabled = currentQuestion.Status != AnswerStatus.NotAnswered;
        }
    }

    private void CloseHelpSidebar_Click(object sender, RoutedEventArgs e)
    {
        HelpSidebar.Visibility = Visibility.Collapsed;
        HelpSidebarColumn.Width = new GridLength(0);
    }

    private void KnowledgeContentText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock textBlock)
            return;

        textBlock.Inlines.Clear();

        if (textBlock.Tag is not IReadOnlyList<HighlightedTextSegment> segments)
            return;

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);
            if (segment.IsHighlighted)
            {
                run.Background = HighlightBackground;
                run.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
                run.FontWeight = FontWeights.Medium;
            }

            textBlock.Inlines.Add(run);
        }
    }

    private static IReadOnlyList<HighlightedKnowledgeMatch> BuildHighlightedMatches(
        IReadOnlyList<KnowledgeBaseMatch> matches,
        QuestionItem currentQuestion)
    {
        return matches
            .Select(match => new HighlightedKnowledgeMatch(
                match.Title,
                CreateHighlightSegments(match.Content, currentQuestion)))
            .ToList();
    }

    private static IReadOnlyList<HighlightedTextSegment> CreateHighlightSegments(
        string content,
        QuestionItem currentQuestion)
    {
        if (string.IsNullOrEmpty(content))
            return [new HighlightedTextSegment(string.Empty, false)];

        var sourceText = BuildHighlightSourceText(currentQuestion);
        var spans = FindHighlyOverlappingSpans(content, sourceText);
        if (spans.Count == 0)
            return [new HighlightedTextSegment(content, false)];

        var segments = new List<HighlightedTextSegment>();
        var cursor = 0;
        foreach (var span in spans)
        {
            if (span.Start > cursor)
                segments.Add(new HighlightedTextSegment(content[cursor..span.Start], false));

            segments.Add(new HighlightedTextSegment(content[span.Start..span.End], true));
            cursor = span.End;
        }

        if (cursor < content.Length)
            segments.Add(new HighlightedTextSegment(content[cursor..], false));

        return segments;
    }

    private static string BuildHighlightSourceText(QuestionItem currentQuestion)
    {
        var question = currentQuestion.Question!;
        var builder = new StringBuilder();
        builder.AppendLine(question.Text);
        builder.AppendLine(currentQuestion.UserAnswer);
        builder.AppendLine(question.GetCorrectAnswerText());

        foreach (var option in question.Options)
            builder.AppendLine(option.DisplayText);

        return builder.ToString();
    }

    private static IReadOnlyList<HighlightSpan> FindHighlyOverlappingSpans(
        string content,
        string sourceText)
    {
        const int minMatchLength = 5;

        var contentChars = NormalizeForComparison(content);
        var sourceChars = NormalizeForComparison(sourceText);
        if (contentChars.Count < minMatchLength || sourceChars.Count < minMatchLength)
            return [];

        var candidates = new List<HighlightSpan>();
        var previous = new int[sourceChars.Count];
        var current = new int[sourceChars.Count];

        for (var i = 0; i < contentChars.Count; i++)
        {
            Array.Clear(current);

            for (var j = 0; j < sourceChars.Count; j++)
            {
                if (contentChars[i].Value != sourceChars[j].Value)
                    continue;

                current[j] = i == 0 || j == 0 ? 1 : previous[j - 1] + 1;
                if (current[j] < minMatchLength)
                    continue;

                var isMatchEnding =
                    i == contentChars.Count - 1 ||
                    j == sourceChars.Count - 1 ||
                    contentChars[i + 1].Value != sourceChars[j + 1].Value;

                if (!isMatchEnding)
                    continue;

                var startIndex = i - current[j] + 1;
                var endIndex = i;
                candidates.Add(new HighlightSpan(
                    contentChars[startIndex].OriginalIndex,
                    contentChars[endIndex].OriginalIndex + 1));
            }

            (previous, current) = (current, previous);
        }

        return SelectNonOverlappingSpans(candidates);
    }

    private static List<NormalizedChar> NormalizeForComparison(string text)
    {
        var result = new List<NormalizedChar>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var value = text[i];
            if (!char.IsLetterOrDigit(value))
                continue;

            result.Add(new NormalizedChar(char.ToLowerInvariant(value), i));
        }

        return result;
    }

    private static IReadOnlyList<HighlightSpan> SelectNonOverlappingSpans(List<HighlightSpan> candidates)
    {
        if (candidates.Count == 0)
            return [];

        var selected = new List<HighlightSpan>();
        foreach (var candidate in candidates
                     .OrderByDescending(span => span.Length)
                     .ThenBy(span => span.Start))
        {
            if (selected.Any(span => candidate.Start < span.End && span.Start < candidate.End))
                continue;

            selected.Add(candidate);
        }

        selected.Sort((left, right) => left.Start.CompareTo(right.Start));

        var merged = new List<HighlightSpan>();
        foreach (var span in selected)
        {
            if (merged.Count == 0 || span.Start > merged[^1].End)
            {
                merged.Add(span);
                continue;
            }

            var last = merged[^1];
            merged[^1] = new HighlightSpan(last.Start, Math.Max(last.End, span.End));
        }

        return merged;
    }

    private void ResetHelpPanel()
    {
        _helpCancellation?.Cancel();
        _helpCancellation?.Dispose();
        _helpCancellation = null;
        _helpMatches = [];
        HelpSidebar.Visibility = Visibility.Collapsed;
        HelpSidebarColumn.Width = new GridLength(0);
        KnowledgeMatchesItemsControl.ItemsSource = null;
        AskAiBanner.Visibility = Visibility.Collapsed;
        AiAnswerPanel.Visibility = Visibility.Collapsed;
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
        if (_grading || _questions.Count == 0) return;
        var currentQuestion = _questions[_currentQuestionIndex];
        var answerText = GetCurrentAnswerText(currentQuestion);

        if (string.IsNullOrWhiteSpace(answerText))
        {
            MessageBox.Show("请输入答案", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _grading = true;
        IsEnabled = false;
        SubmitButton.Content = "正在批改…";
        try
        {
            currentQuestion.UserAnswer = answerText;

            var answerResult = await _quizService.ProcessAnswerAsync(
                _project,
                currentQuestion.Question!,
                currentQuestion.UserAnswer,
                _startTime);

            currentQuestion.Status = answerResult.IsCorrect ? AnswerStatus.Correct : AnswerStatus.Wrong;
            currentQuestion.Assessment = answerResult.AiAssessment;
            currentQuestion.Question!.Status = answerResult.IsCorrect;
            _recordsNeedSave = true;
            _personalizationPending = true;

            // Show result
            ShowResult(currentQuestion);
            AskDeepSeekButton.IsEnabled = true;
            AnswerTextBox.IsEnabled = false;
            ChoiceOptionsItemsControl.IsEnabled = false;
            FillBlankAnswersItemsControl.IsEnabled = false;
            TrueFalseOptionsPanel.IsEnabled = false;
            UpdateAnswerCardStyles();

            QDisplayLabel.Content = MemoryHintFormatter.Format(answerResult);

            await _projectFileService.SaveProjectAsync(_project);
            _recordsNeedSave = false;
            RecordsChanged?.Invoke(this, EventArgs.Empty);

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
            IsEnabled = true;
            SubmitButton.Content = "提交答案";
            SubmitButton.IsEnabled = currentQuestion.Status == AnswerStatus.NotAnswered;
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
    {
        if (_currentQuestionIndex < _totalQuestions - 1)
        {
            _currentQuestionIndex++;
            UpdateDisplay();
        }
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (_grading) return;
        if (_currentQuestionIndex > 0)
        {
            _currentQuestionIndex--;
            UpdateDisplay();
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_grading) return;
        if (_currentQuestionIndex < _totalQuestions - 1)
        {
            _currentQuestionIndex++;
            UpdateDisplay();
        }
    }

    private void MultipartAnswers_AnswerChanged(object? sender, EventArgs e)
    {
        if (_questions.Count > 0 && _questions[_currentQuestionIndex].Status == AnswerStatus.NotAnswered)
            _questions[_currentQuestionIndex].UserAnswer = MultipartAnswers.AnswerText;
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

    internal void Release() => ResetHelpPanel();

    internal async Task<bool> SaveOnCloseAsync()
    {
        try
        {
            if (_recordsNeedSave)
            {
                await _projectFileService.SaveProjectAsync(_project);
                _recordsNeedSave = false;
                RecordsChanged?.Invoke(this, EventArgs.Empty);
            }

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
