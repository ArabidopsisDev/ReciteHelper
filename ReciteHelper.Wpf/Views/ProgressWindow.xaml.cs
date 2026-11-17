using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Enums;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Views;

public partial class ProgressWindow : Window, INotifyPropertyChanged
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(11, 11, 11));
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromRgb(233, 233, 233));
    private static readonly Brush ActiveTextBrush = Brushes.White;
    private static readonly Brush InactiveTextBrush = new SolidColorBrush(Color.FromRgb(125, 125, 125));

    private ProjectCreationStage _currentStage = ProjectCreationStage.ReadingText;
    private ExamSetImportStage _currentExamStage = ExamSetImportStage.ReadingSource;
    private bool _isClosed;
    private bool _usesSourceChapters;

    public ProgressWindow() : this(ProgressWindowMode.ProjectCreation)
    {
    }

    public ProgressWindow(ProgressWindowMode mode)
    {
        InitializeComponent();
        DataContext = this;
        Closed += (_, _) => _isClosed = true;

        if (mode == ProgressWindowMode.ExamSetImport)
        {
            ConfigureExamSetImportMode();
            ApplyExamStage(ExamSetImportStage.ReadingSource, "正在读取所选试卷文件。");
            SetProgress(0, 1, true, "文件读取");
        }
        else if (mode == ProgressWindowMode.ProjectContentImport)
        {
            ConfigureProjectContentImportMode();
            ApplyStage(ProjectCreationStage.ReadingText, "正在读取新的资料，随后将提取知识点和题目。");
            SetProgress(0, 1, true, "读取进度");
        }
        else
        {
            ApplyStage(ProjectCreationStage.ReadingText, "正在读取题库文件，随后将进入知识提取。");
            SetProgress(0, 1, true, "读取进度");
        }
    }

    public Brush Step1Brush { get => field; private set => SetField(ref field, value); } = ActiveBrush;
    public Brush Step2Brush { get => field; private set => SetField(ref field, value); } = ActiveBrush;
    public Brush Step3Brush { get => field; private set => SetField(ref field, value); } = InactiveBrush;
    public Brush Step4Brush { get => field; private set => SetField(ref field, value); } = InactiveBrush;

    public Brush Step1TextBrush { get => field; private set => SetField(ref field, value); } = ActiveTextBrush;
    public Brush Step2TextBrush { get => field; private set => SetField(ref field, value); } = ActiveTextBrush;
    public Brush Step3TextBrush { get => field; private set => SetField(ref field, value); } = InactiveTextBrush;
    public Brush Step4TextBrush { get => field; private set => SetField(ref field, value); } = InactiveTextBrush;

    public Brush Line1Brush { get => field; private set => SetField(ref field, value); } = ActiveBrush;
    public Brush Line2Brush { get => field; private set => SetField(ref field, value); } = InactiveBrush;
    public Brush Line3Brush { get => field; private set => SetField(ref field, value); } = InactiveBrush;

    public string CurrentTitle { get => field; private set => SetField(ref field, value); } = string.Empty;
    public string CurrentDescription { get => field; private set => SetField(ref field, value); } = string.Empty;
    public string DetailLabel { get => field; private set => SetField(ref field, value); } = string.Empty;
    public string ProgressText { get => field; private set => SetField(ref field, value); } = string.Empty;

    public int ProgressCurrent { get => field; private set => SetField(ref field, value); }
    public int ProgressTotal { get => field; private set => SetField(ref field, value); } = 1;
    public bool IsProgressIndeterminate { get => field; private set => SetField(ref field, value); } = true;

    public void ApplyProgress(ProjectCreationProgress progress)
    {
        if (_isClosed)
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplyProgress(progress));
            return;
        }

        if (progress.UsesSourceChapters && !_usesSourceChapters)
            ConfigureSourceChapterProjectMode();

        var stage = progress.Stage < _currentStage ? _currentStage : progress.Stage;
        ApplyStage(stage, progress.Label);

        var (current, total, label) = stage switch
        {
            ProjectCreationStage.ReadingText => (progress.RoundCurrent, progress.RoundTotal, "读取进度"),
            ProjectCreationStage.KnowledgeExtraction => (progress.ScanCurrent, progress.ScanTotal, _usesSourceChapters ? "章节提取进度" : "知识提取进度"),
            ProjectCreationStage.TextClustering => (progress.ClusterCurrent, progress.ClusterTotal, _usesSourceChapters ? "题目生成进度" : "文本聚类进度"),
            ProjectCreationStage.VectorGeneration => (progress.ClusterCurrent, progress.ClusterTotal, "向量生成进度"),
            ProjectCreationStage.Completed => (1, 1, "完成状态"),
            _ => (0, 1, "当前进度")
        };

        SetProgress(current, total, total <= 0, label);
    }

    public void ApplyProgress(ExamSetImportProgress progress)
    {
        if (_isClosed)
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ApplyProgress(progress));
            return;
        }

        var stage = progress.Stage < _currentExamStage ? _currentExamStage : progress.Stage;
        ApplyExamStage(stage, progress.Message);
        var current = progress.Completed ?? 0;
        var total = progress.Total ?? 0;
        var determinate = stage is ExamSetImportStage.ValidatingQuestions or
            ExamSetImportStage.SavingPapers or ExamSetImportStage.Completed && total > 0;
        var label = stage switch
        {
            ExamSetImportStage.ReadingSource => "文件读取",
            ExamSetImportStage.ExtractingPapers => "内容识别",
            ExamSetImportStage.ValidatingQuestions => "套卷校验",
            ExamSetImportStage.SavingPapers => "保存进度",
            ExamSetImportStage.Completed => "完成状态",
            _ => "当前进度"
        };
        SetProgress(current, total, !determinate, label);
    }

    private void ConfigureExamSetImportMode()
    {
        Title = "导入试卷";
        WindowHeadingText.Text = "正在创建套卷";
        WindowSubheadingText.Text = "正在识别套卷边界，并整理题目、答案与解析。";
        Step1Label.Text = "读取试卷";
        Step2Label.Text = "套卷抽取";
        Step3Label.Text = "题目校验";
        Step4Label.Text = "保存套卷";
        FooterText.Text = "试卷较多时可能需要几分钟，窗口会保持响应。";
        SetVisualStage(1);
    }

    private void ConfigureProjectContentImportMode()
    {
        Title = "导入内容";
        WindowHeadingText.Text = "正在导入内容";
        WindowSubheadingText.Text = "正在从资料或错题中提取题目，并归并到现有章节与知识库。";
        Step1Label.Text = "读取资料";
        Step2Label.Text = "知识提取";
        Step3Label.Text = "章节归并";
        Step4Label.Text = "知识库更新";
        FooterText.Text = "导入期间窗口会保持响应，完成后项目会自动保存。";
    }

    private void ConfigureSourceChapterProjectMode()
    {
        _usesSourceChapters = true;
        WindowSubheadingText.Text = "检测到源文件包含明确章节，将按原章节生成题目并构建知识库。";
        Step1Label.Text = "读取文本";
        Step2Label.Text = "章节提取";
        Step3Label.Text = "题目生成";
        Step4Label.Text = "向量生成";
    }

    private void ApplyExamStage(ExamSetImportStage stage, string? label)
    {
        _currentExamStage = stage;
        SetVisualStage(Math.Min((int)stage, 4));
        (CurrentTitle, CurrentDescription) = stage switch
        {
            ExamSetImportStage.ReadingSource => (
                "正在读取试卷内容",
                label ?? "正在从 PDF 或 TXT 中提取可供识别的文字。"),
            ExamSetImportStage.ExtractingPapers => (
                "正在识别套卷内容",
                label ?? "正在区分不同套卷，并抽取题目、题型、答案、解析和标题。"),
            ExamSetImportStage.ValidatingQuestions => (
                "正在校验题目结构",
                label ?? "正在修复填空空位、规范题型，并检查答案与解析是否完整。"),
            ExamSetImportStage.SavingPapers => (
                "正在保存套卷文件",
                label ?? "正在将校验后的套卷写入项目 exams 目录。"),
            ExamSetImportStage.Completed => (
                "套卷导入完成",
                label ?? "所有套卷均已保存，可以在模拟考试中加载。"),
            _ => ("正在导入试卷", label ?? "试卷正在处理中。")
        };
    }

    private void SetVisualStage(int stage)
    {
        Step1Brush = stage >= 1 ? ActiveBrush : InactiveBrush;
        Step2Brush = stage >= 2 ? ActiveBrush : InactiveBrush;
        Step3Brush = stage >= 3 ? ActiveBrush : InactiveBrush;
        Step4Brush = stage >= 4 ? ActiveBrush : InactiveBrush;
        Step1TextBrush = stage >= 1 ? ActiveTextBrush : InactiveTextBrush;
        Step2TextBrush = stage >= 2 ? ActiveTextBrush : InactiveTextBrush;
        Step3TextBrush = stage >= 3 ? ActiveTextBrush : InactiveTextBrush;
        Step4TextBrush = stage >= 4 ? ActiveTextBrush : InactiveTextBrush;
        Line1Brush = stage >= 2 ? ActiveBrush : InactiveBrush;
        Line2Brush = stage >= 3 ? ActiveBrush : InactiveBrush;
        Line3Brush = stage >= 4 ? ActiveBrush : InactiveBrush;
    }

    private void ApplyStage(ProjectCreationStage stage, string? label)
    {
        _currentStage = stage;

        var visualStage = stage < ProjectCreationStage.KnowledgeExtraction
            ? ProjectCreationStage.KnowledgeExtraction
            : stage;

        Step1Brush = GetStepBrush(visualStage, ProjectCreationStage.ReadingText);
        Step2Brush = GetStepBrush(visualStage, ProjectCreationStage.KnowledgeExtraction);
        Step3Brush = GetStepBrush(visualStage, ProjectCreationStage.TextClustering);
        Step4Brush = GetStepBrush(visualStage, ProjectCreationStage.VectorGeneration);

        Step1TextBrush = GetStepTextBrush(visualStage, ProjectCreationStage.ReadingText);
        Step2TextBrush = GetStepTextBrush(visualStage, ProjectCreationStage.KnowledgeExtraction);
        Step3TextBrush = GetStepTextBrush(visualStage, ProjectCreationStage.TextClustering);
        Step4TextBrush = GetStepTextBrush(visualStage, ProjectCreationStage.VectorGeneration);

        Line1Brush = visualStage >= ProjectCreationStage.KnowledgeExtraction ? ActiveBrush : InactiveBrush;
        Line2Brush = visualStage >= ProjectCreationStage.TextClustering ? ActiveBrush : InactiveBrush;
        Line3Brush = visualStage >= ProjectCreationStage.VectorGeneration ? ActiveBrush : InactiveBrush;

        (CurrentTitle, CurrentDescription) = GetStageText(stage, label);
    }

    private static Brush GetStepBrush(ProjectCreationStage current, ProjectCreationStage step)
    {
        return current >= step ? ActiveBrush : InactiveBrush;
    }

    private static Brush GetStepTextBrush(ProjectCreationStage current, ProjectCreationStage step)
    {
        return current >= step ? ActiveTextBrush : InactiveTextBrush;
    }

    private (string Title, string Description) GetStageText(ProjectCreationStage stage, string? label)
    {
        return stage switch
        {
            ProjectCreationStage.ReadingText => (
                "正在读取你的资料",
                label ?? "正在复制并读取题库文件，准备进入知识提取流程。"),
            ProjectCreationStage.KnowledgeExtraction => (
                _usesSourceChapters ? "正在提取源文件章节" : "你的资料正在被提取知识点",
                label ?? (_usesSourceChapters
                    ? "正在按照源文件中的章标题切分资料，保留原始章节结构。"
                    : "你的资料已经被切割完成，正在分块提取知识点和相关题目。")),
            ProjectCreationStage.TextClustering => (
                _usesSourceChapters ? "正在从章节中生成题目" : "正在整理章节结构",
                label ?? (_usesSourceChapters
                    ? "每个源文件章节会被切成小文本块并并发处理，生成后直接归入对应章节。"
                    : "初步题目已经生成，正在合并相似章节并整理题目结构。")),
            ProjectCreationStage.VectorGeneration => (
                "正在生成知识库向量",
                label ?? "正在将知识点转换为本地向量索引，后续可用于知识库检索。"),
            ProjectCreationStage.Completed => (
                "项目创建完成",
                label ?? "题目、章节和本地知识库已经处理完成，正在保存项目文件。"),
            _ => ("正在创建项目", label ?? "项目正在处理中。")
        };
    }

    private void SetProgress(int current, int total, bool isIndeterminate, string label)
    {
        DetailLabel = label;
        IsProgressIndeterminate = isIndeterminate;
        ProgressTotal = Math.Max(total, 1);
        ProgressCurrent = Math.Clamp(current, 0, ProgressTotal);
        ProgressText = isIndeterminate ? "处理中" : $"{ProgressCurrent}/{ProgressTotal}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public enum ProgressWindowMode
{
    ProjectCreation,
    ExamSetImport,
    ProjectContentImport
}
