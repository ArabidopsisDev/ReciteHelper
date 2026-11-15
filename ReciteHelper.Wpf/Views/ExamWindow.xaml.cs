using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.ValueObjects;
using ReciteHelper.Wpf.Controls.ExamPaper;
using ReciteHelper.Wpf.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReciteHelper.Wpf.Views;

public partial class ExamWindow : Window
{
    private const double FirstPageContentBudget = 640d;
    private const double RegularPageContentBudget = 860d;
    private const double PaperContentWidth = 582d;
    private const double PaginationSafetyMargin = 4d;

    private readonly IExamAnswerService _examAnswerService;
    private readonly Project? _project;
    private readonly IProjectCreationService? _projectCreationService;
    private readonly IProjectFileService? _projectFileService;
    private readonly ExamSettings _settings;
    private readonly string _examName;
    private readonly string? _paperTitle;
    private readonly ObservableCollection<ExamQuestionItem> _questions = [];
    private readonly List<ExamPaperPage> _pages = [];
    private readonly DispatcherTimer _examTimer;
    private int _currentSpreadIndex;
    private DateTime _examStartTime;
    private TimeSpan _timeRemaining;
    private bool _isExamActive;
    private bool _isSubmitted;
    private bool _grading;
    private DateTime _submittedAt;
    private readonly bool _isImportedExamSet;

    public ExamWindow(
        List<Question> questions,
        string examName,
        ExamSettings settings,
        IExamAnswerService examAnswerService,
        IReadOnlyList<ExamSetQuestion>? examSetQuestions = null,
        string? paperTitle = null,
        Project? project = null,
        IProjectCreationService? projectCreationService = null,
        IProjectFileService? projectFileService = null,
        bool isImportedExamSet = false)
    {
        _examAnswerService = examAnswerService;
        _project = project;
        if (project?.TemplateType == ProjectTemplateType.RuleBasedReview)
            foreach (var question in questions) question.RequiresAiGrading = true;
        _projectCreationService = projectCreationService;
        _projectFileService = projectFileService;
        _settings = settings;
        _examName = examName;
        _paperTitle = paperTitle;
        _isImportedExamSet = isImportedExamSet;
        _timeRemaining = TimeSpan.FromMinutes(settings.ExamTimeMinutes);
        _examTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _examTimer.Tick += ExamTimer_Tick;

        InitializeComponent();
        InitializeExamIdentity();
        InitializeQuestions(questions, examSetQuestions);
        BuildPaperPages();
        ShowInstructions();
        UpdateTimeDisplay();
        RenderCurrentSpread();
    }

    private void InitializeExamIdentity()
    {
        StudentNameText.Text = "考生0429";
        ExamNumberText.Text = $"RH{DateTime.Now:yyyyMMdd}{Random.Shared.Next(1000, 9999)}";
        ToolbarTitleText.Text = $"{_paperTitle ?? _examName} · A卷";
    }

    private void InitializeQuestions(
        IReadOnlyList<Question> questions,
        IReadOnlyList<ExamSetQuestion>? examSetQuestions)
    {
        var importedMetadata = new Dictionary<Question, ExamSetQuestion>(ReferenceEqualityComparer.Instance);
        if (examSetQuestions is not null)
        {
            foreach (var item in examSetQuestions)
                importedMetadata[item.Question] = item;
        }
        var orderedQuestions = questions
            .OrderBy(question => GetQuestionTypeOrder(question.Type))
            .ToList();

        for (var index = 0; index < orderedQuestions.Count; index++)
        {
            var question = orderedQuestions[index];
            var importedQuestion = importedMetadata.TryGetValue(question, out var metadata)
                ? metadata
                : null;
            _questions.Add(new ExamQuestionItem
            {
                Number = index + 1,
                Question = question,
                Score = importedQuestion?.Score ?? question.DefaultExamScore,
                Explanation = importedQuestion?.Explanation ?? string.Empty,
                UserAnswer = string.Empty,
                Status = ExamAnswerStatus.NotAnswered
            });
            _questions[^1].PropertyChanged += QuestionItem_PropertyChanged;
        }
    }

    private void QuestionItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExamQuestionItem.UserAnswer))
            UpdateAnsweredCount();
    }

    private void BuildPaperPages()
    {
        _pages.Clear();
        var academicYear = GetAcademicYearText(DateTime.Today);
        var builder = CreatePageBuilder(true, FirstPageContentBudget, academicYear);
        var sectionNumber = 0;

        AddQuestionSectionIfAny(
            ref builder,
            ref sectionNumber,
            _questions.Where(question => question.IsSingleChoice).ToList(),
            "选择题",
            CreateChoiceSectionDescription,
            ExamPaperElementKind.ChoiceQuestion,
            EstimateChoiceQuestionHeight,
            academicYear);

        AddQuestionSectionIfAny(
            ref builder,
            ref sectionNumber,
            _questions.Where(question => question.IsFillBlank).ToList(),
            "填空题",
            CreateFillBlankSectionDescription,
            ExamPaperElementKind.FillBlankQuestion,
            EstimateFillBlankQuestionHeight,
            academicYear);

        AddQuestionSectionIfAny(
            ref builder,
            ref sectionNumber,
            _questions.Where(question => question.IsTrueFalse).ToList(),
            "判断题",
            CreateTrueFalseSectionDescription,
            ExamPaperElementKind.TrueFalseQuestion,
            EstimateTrueFalseQuestionHeight,
            academicYear);

        AddQuestionSectionIfAny(
            ref builder,
            ref sectionNumber,
            _questions.Where(question => question.IsTermDefinition).ToList(),
            "名词解释",
            CreateTermDefinitionSectionDescription,
            ExamPaperElementKind.TermDefinitionQuestion,
            EstimateEssayQuestionHeight,
            academicYear);

        AddQuestionSectionIfAny(
            ref builder,
            ref sectionNumber,
            _questions.Where(question => question.Question?.IsEssay is true).ToList(),
            "解答题",
            CreateEssaySectionDescription,
            ExamPaperElementKind.EssayQuestion,
            EstimateEssayQuestionHeight,
            academicYear);

        if (_pages.Count == 0 || _pages[^1] != builder.Page)
            _pages.Add(builder.Page);

        if (_pages.Count % 2 != 0)
            _pages.Add(CreatePageBuilder(false, RegularPageContentBudget, academicYear).Page);

        foreach (var page in _pages)
            page.TotalPages = _pages.Count;
    }

    private void AddQuestionSectionIfAny(
        ref PageBuilder builder,
        ref int sectionNumber,
        IReadOnlyList<ExamQuestionItem> questions,
        string sectionName,
        Func<IReadOnlyList<ExamQuestionItem>, string> descriptionFactory,
        ExamPaperElementKind questionKind,
        Func<ExamQuestionItem, double> heightEstimator,
        string academicYear)
    {
        if (questions.Count == 0)
            return;

        sectionNumber++;
        AddQuestionSection(
            ref builder,
            questions,
            $"{GetChineseSectionNumber(sectionNumber)}、{sectionName}",
            descriptionFactory,
            questionKind,
            heightEstimator,
            academicYear);
    }

    private void AddQuestionSection(
        ref PageBuilder builder,
        IReadOnlyList<ExamQuestionItem> questions,
        string sectionTitle,
        Func<IReadOnlyList<ExamQuestionItem>, string> descriptionFactory,
        ExamPaperElementKind questionKind,
        Func<ExamQuestionItem, double> heightEstimator,
        string academicYear)
    {
        if (questions.Count == 0)
            return;

        var description = descriptionFactory(questions);
        var sectionHeaderHeight = MeasureSectionHeaderHeight(sectionTitle, description);
        var firstQuestionHeight = heightEstimator(questions[0]);
        EnsureSpace(
            ref builder,
            sectionHeaderHeight + firstQuestionHeight,
            academicYear);
        builder.Page.Elements.Add(new ExamPaperElement
        {
            Kind = ExamPaperElementKind.SectionHeader,
            Title = sectionTitle,
            Description = description
        });
        builder.RemainingHeight -= sectionHeaderHeight;

        foreach (var question in questions)
        {
            var estimatedHeight = heightEstimator(question);
            if (estimatedHeight > builder.RemainingHeight && builder.Page.Elements.Count > 0)
            {
                CommitPage(builder.Page);
                builder = CreatePageBuilder(false, RegularPageContentBudget, academicYear);
            }

            builder.Page.Elements.Add(new ExamPaperElement
            {
                Kind = questionKind,
                Question = question
            });
            builder.RemainingHeight -= estimatedHeight;
        }
    }

    private void EnsureSpace(
        ref PageBuilder builder,
        double requiredHeight,
        string academicYear)
    {
        if (requiredHeight <= builder.RemainingHeight)
            return;

        CommitPage(builder.Page);
        builder = CreatePageBuilder(false, RegularPageContentBudget, academicYear);
    }

    private void CommitPage(ExamPaperPage page)
    {
        if (_pages.Count == 0 || _pages[^1] != page)
            _pages.Add(page);
    }

    private PageBuilder CreatePageBuilder(
        bool showHeader,
        double contentBudget,
        string academicYear)
    {
        return new PageBuilder(
            new ExamPaperPage
            {
                PageNumber = _pages.Count + 1,
                ShowPaperHeader = showHeader,
                IsExamActive = _isExamActive,
                SubjectName = _examName,
                AcademicYearText = academicYear,
                ExamTitle = _paperTitle
            },
            contentBudget);
    }

    private static string CreateChoiceSectionDescription(IReadOnlyList<ExamQuestionItem> questions)
    {
        var score = questions.Sum(question => question.Score);
        var scoreText = questions.Select(question => question.Score).Distinct().Count() == 1
            ? $"，每小题{questions[0].Score}分"
            : string.Empty;
        return $"本大题共{questions.Count}小题{scoreText}，满分{score}分。在每小题给出的选项中，只有一项是符合题目要求的。";
    }

    private static string CreateEssaySectionDescription(IReadOnlyList<ExamQuestionItem> questions)
    {
        var score = questions.Sum(question => question.Score);
        var scoreText = questions.Select(question => question.Score).Distinct().Count() == 1
            ? $"，每小题{questions[0].Score}分"
            : string.Empty;
        return $"本大题共{questions.Count}小题{scoreText}，满分{score}分。解答应写出必要的文字说明、作答过程或推理步骤。";
    }

    private static string CreateFillBlankSectionDescription(IReadOnlyList<ExamQuestionItem> questions)
    {
        var blanks = questions.Sum(question => Math.Max(1, question.Question?.BlankCount ?? 1));
        var score = questions.Sum(question => question.Score);
        var scoreText = score == blanks
            ? $"合计{blanks}空，每空1分"
            : $"合计{blanks}空，按原卷分值计分";
        return $"本大题共{questions.Count}小题，{scoreText}，满分{score}分。请将答案填写在相应横线上。";
    }

    private static string CreateTrueFalseSectionDescription(IReadOnlyList<ExamQuestionItem> questions)
    {
        return $"本大题共{questions.Count}小题，每小题1分，满分{questions.Sum(question => question.Score)}分。请判断各题说法是否正确。";
    }

    private static string CreateTermDefinitionSectionDescription(IReadOnlyList<ExamQuestionItem> questions)
    {
        return $"本大题共{questions.Count}小题，每小题4分，满分{questions.Sum(question => question.Score)}分。请准确解释下列名词。";
    }

    private static double EstimateChoiceQuestionHeight(ExamQuestionItem item)
    {
        var control = new ChoiceQuestionControl();
        control.SetQuestion(item, false);
        control.Measure(new Size(PaperContentWidth, double.PositiveInfinity));

        return Math.Ceiling(control.DesiredSize.Height) + PaginationSafetyMargin;
    }

    private static double EstimateFillBlankQuestionHeight(ExamQuestionItem item)
    {
        var control = new FillBlankQuestionControl();
        control.SetQuestion(item, false);
        return MeasureQuestionControl(control);
    }

    private static double EstimateTrueFalseQuestionHeight(ExamQuestionItem item)
    {
        var control = new TrueFalseQuestionControl();
        control.SetQuestion(item, false);
        return MeasureQuestionControl(control);
    }

    private static double MeasureSectionHeaderHeight(string title, string description)
    {
        var control = new ExamSectionHeaderControl();
        control.SetContent(title, description);
        control.Measure(new Size(PaperContentWidth, double.PositiveInfinity));

        return Math.Ceiling(control.DesiredSize.Height) + PaginationSafetyMargin;
    }

    private static double EstimateEssayQuestionHeight(ExamQuestionItem item)
    {
        var control = new EssayQuestionControl();
        control.SetQuestion(item, false);
        return MeasureQuestionControl(control);
    }

    private static double MeasureQuestionControl(FrameworkElement control)
    {
        control.Measure(new Size(PaperContentWidth, double.PositiveInfinity));
        return Math.Ceiling(control.DesiredSize.Height) + PaginationSafetyMargin;
    }

    private static int GetQuestionTypeOrder(QuestionType type)
    {
        return type switch
        {
            QuestionType.SingleChoice => 0,
            QuestionType.FillBlank => 1,
            QuestionType.TrueFalse => 2,
            QuestionType.TermDefinition => 3,
            _ => 4
        };
    }

    private static string GetChineseSectionNumber(int number)
    {
        return number switch
        {
            1 => "一",
            2 => "二",
            3 => "三",
            4 => "四",
            5 => "五",
            _ => number.ToString()
        };
    }

    private static string GetAcademicYearText(DateTime date)
    {
        return date.Month >= 9
            ? $"{date.Year}-{date.Year + 1}学年上学期"
            : $"{date.Year - 1}-{date.Year}学年下学期";
    }

    private void ShowInstructions()
    {
        _isExamActive = false;
        _isSubmitted = false;
        ReadyPanel.Visibility = Visibility.Visible;
        RunningPanel.Visibility = Visibility.Collapsed;
        ResultOverlay.Visibility = Visibility.Collapsed;
        PaperAccessOverlay.Visibility = Visibility.Visible;
        PaperScrollViewer.Opacity = 0.12d;
        PaperScrollViewer.IsHitTestVisible = false;
        SetPagesActive(false);
    }

    private void StartExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (AgreeCheckBox.IsChecked is not true)
            return;

        StartExam();
    }

    private void StartExam()
    {
        _isExamActive = true;
        _isSubmitted = false;
        _examStartTime = DateTime.Now;
        _timeRemaining = TimeSpan.FromMinutes(_settings.ExamTimeMinutes);
        ReadyPanel.Visibility = Visibility.Collapsed;
        RunningPanel.Visibility = Visibility.Visible;
        ResultOverlay.Visibility = Visibility.Collapsed;
        PaperAccessOverlay.Visibility = Visibility.Collapsed;
        PaperScrollViewer.Opacity = 1d;
        PaperScrollViewer.IsHitTestVisible = true;
        SetPagesActive(true);
        UpdateAnsweredCount();
        UpdateTimeDisplay();
        _examTimer.Start();
    }

    private void SetPagesActive(bool isActive)
    {
        foreach (var page in _pages)
            page.IsExamActive = isActive;

        RenderCurrentSpread();
    }

    private void AgreeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        StartExamButton.IsEnabled = AgreeCheckBox.IsChecked is true;
    }

    private async void ExamTimer_Tick(object? sender, EventArgs e)
    {
        _timeRemaining -= TimeSpan.FromSeconds(1);
        UpdateTimeDisplay();
        UpdateAnsweredCount();

        if (_timeRemaining > TimeSpan.Zero)
            return;

        _examTimer.Stop();
        _timeRemaining = TimeSpan.Zero;
        UpdateTimeDisplay();
        MessageBox.Show("考试时间已到，系统将自动交卷。", "时间到", MessageBoxButton.OK, MessageBoxImage.Information);
        await SubmitExamAsync();
    }

    private void UpdateTimeDisplay()
    {
        TimeRemainingText.Text = _timeRemaining.TotalHours >= 1
            ? _timeRemaining.ToString(@"hh\:mm\:ss")
            : _timeRemaining.ToString(@"mm\:ss");
        TimeRemainingText.Foreground = _timeRemaining <= TimeSpan.FromMinutes(10)
            ? new SolidColorBrush(Color.FromRgb(166, 31, 43))
            : new SolidColorBrush(Color.FromRgb(45, 45, 45));
    }

    private void UpdateAnsweredCount()
    {
        AnsweredCountText.Text = $"{_questions.Count(question => question.IsAnswered)}/{_questions.Count}";
    }

    private void PreviousSpreadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSpreadIndex <= 0)
            return;

        _currentSpreadIndex--;
        RenderCurrentSpread();
    }

    private void NextSpreadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSpreadIndex >= GetSpreadCount() - 1)
            return;

        _currentSpreadIndex++;
        RenderCurrentSpread();
    }

    private void RenderCurrentSpread()
    {
        if (_pages.Count == 0)
            return;

        var leftPageIndex = _currentSpreadIndex * 2;
        var rightPageIndex = Math.Min(leftPageIndex + 1, _pages.Count - 1);
        LeftPageControl.SetPage(_pages[leftPageIndex]);
        RightPageControl.SetPage(_pages[rightPageIndex]);
        SpreadIndicatorText.Text = $"第 {leftPageIndex + 1}-{rightPageIndex + 1} 页";
        PreviousSpreadButton.IsEnabled = _currentSpreadIndex > 0;
        NextSpreadButton.IsEnabled = _currentSpreadIndex < GetSpreadCount() - 1;
        UpdateAnsweredCount();
    }

    private int GetSpreadCount()
    {
        return Math.Max(1, (int)Math.Ceiling(_pages.Count / 2d));
    }

    private async void SubmitExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_grading || _isSubmitted) return;
        var unansweredCount = _questions.Count(question => !question.IsAnswered);
        var prompt = unansweredCount > 0
            ? $"还有 {unansweredCount} 道题未作答，确定交卷吗？"
            : "确定要交卷吗？交卷后将不能修改答案。";
        var result = MessageBox.Show(prompt, "确认交卷", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
            await SubmitExamAsync();
    }

    private async Task SubmitExamAsync()
    {
        if (_grading || _isSubmitted) return;
        _grading = true;
        _submittedAt = DateTime.Now;
        IsEnabled = false;
        _examTimer.Stop();
        _isExamActive = false;
        SetPagesActive(false);
        try
        {
            foreach (var item in _questions)
            {
                if (item.Question is null) continue;
                if (item.AiAssessment is null || item.AiAssessment.UserAnswer != item.UserAnswer)
                    item.AiAssessment = await _examAnswerService.GradeAsync(item.Question, item.UserAnswer);
                item.EarnedScore = item.AiAssessment is { } assessment
                    ? Math.Round(item.Score * assessment.ScoreRatio, 2)
                    : _examAnswerService.IsCorrect(item.Question, item.UserAnswer) ? item.Score : 0;
            }
            _isSubmitted = true;
            RunningPanel.Visibility = Visibility.Collapsed;
            CalculateScore();
            ResultOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"批改未完成：{ex.Message} 已保留本次作答，请再次交卷重试。", "AI 批改提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            _isExamActive = true;
            SetPagesActive(true);
            if (_timeRemaining > TimeSpan.Zero) _examTimer.Start();
        }
        finally { _grading = false; IsEnabled = true; }
    }

    private void CalculateScore()
    {
        var correctQuestions = _questions
            .Where(question => question.Question is not null &&
                (question.AiAssessment?.IsCorrect ?? _examAnswerService.IsCorrect(question.Question, question.UserAnswer)))
            .ToList();
        var earnedScore = _questions.Sum(question => question.EarnedScore);
        var totalScore = _questions.Sum(question => question.Score);
        var correctCount = correctQuestions.Count;
        var wrongCount = _questions.Count - correctCount;
        var accuracy = _questions.Count == 0 ? 0d : correctCount * 100d / _questions.Count;
        var timeUsed = _submittedAt - _examStartTime;

        ScoreText.Text = earnedScore.ToString();
        ScoreDetailText.Text = $"满分 {totalScore} 分 · 正确率 {accuracy:F1}%";
        CorrectCountText.Text = $"答对 {correctCount} 题";
        WrongCountText.Text = $"答错 {wrongCount} 题";
        TimeUsedText.Text = $"用时 {timeUsed:mm\\:ss}";
        EncouragementText.Text = accuracy switch
        {
            >= 90d => "成绩优秀，作答准确而稳定。",
            >= 80d => "整体掌握良好，继续巩固错题。",
            >= 60d => "已经达到基本要求，薄弱部分仍需复习。",
            _ => "建议从错题对应的知识点重新开始复习。"
        };
    }

    private void ReviewAnswersButton_Click(object sender, RoutedEventArgs e)
    {
        var reviewWindow = new ExamReviewWindow(
            _questions.ToList(),
            _examAnswerService,
            _isImportedExamSet ? _project : null,
            _isImportedExamSet ? _projectCreationService : null,
            _isImportedExamSet ? _projectFileService : null)
        {
            Owner = this
        };
        reviewWindow.ShowDialog();
    }

    private void RetryExamButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("确定要清空答案并重新开始考试吗？", "重新考试", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        foreach (var question in _questions)
        {
            question.UserAnswer = string.Empty;
            question.Status = ExamAnswerStatus.NotAnswered;
            question.AiAssessment = null;
            question.EarnedScore = 0;
        }

        _currentSpreadIndex = 0;
        StartExam();
    }

    private void CloseExamButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_grading) { e.Cancel = true; return; }
        if (_isExamActive && !_isSubmitted)
        {
            var result = MessageBox.Show("考试尚未结束，确定要退出吗？", "确认退出", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }
        }

        _examTimer.Stop();
        base.OnClosing(e);
    }

    private sealed class PageBuilder(ExamPaperPage page, double remainingHeight)
    {
        public ExamPaperPage Page { get; } = page;
        public double RemainingHeight { get; set; } = remainingHeight;
    }
}
