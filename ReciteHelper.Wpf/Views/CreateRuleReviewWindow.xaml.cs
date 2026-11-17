using Microsoft.Win32;
using ReciteHelper.Application.Services;
using ReciteHelper.Core.Aggregates;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Wpf.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ReciteHelper.Infrastructure.Utilities;

namespace ReciteHelper.Wpf.Views;

public partial class CreateRuleReviewWindow : Window
{
    private readonly IRuleReviewService _service;
    private readonly IProjectFileService _files;
    private readonly Func<Project, Task> _onCreated;
    private readonly ObservableCollection<RuleReviewCategoryItem> _categories = [new() { Number = 1 }];
    private readonly List<Chapter> _completed = [];
    private readonly CancellationTokenSource _lifetime = new();
    private RuleReviewCategoryItem? _editing;
    private RuleReviewPreview? _accepted;
    private string? _rejectedPrompt;
    private bool _started;
    private bool _busy;
    private bool _saving;
    private readonly Project? _existingProject;
    private RuleReviewCategoryItem? _additionalTarget;
    private Chapter? _pendingAdditional;
    private bool _loadingEditor;

    public CreateRuleReviewWindow(IRuleReviewService service, IProjectFileService files, Func<Project, Task> onCreated, Project? existingProject = null)
    {
        _service = service;
        _files = files;
        _onCreated = onCreated;
        _existingProject = existingProject;
        InitializeComponent();
        if (existingProject is not null)
        {
            Title = PageTitleText.Text = "管理大类与出题";
            PageDescriptionText.Text = $"当前项目：{existingProject.ProjectName}。为已有大类追加题目，或建立新的大类。";
            ProjectDetailsSection.Visibility = Visibility.Collapsed;
            CategoryListHint.Text = "选择已有大类，或增加新的大类。";
            NameBox.Text = existingProject.ProjectName;
            FolderBox.Text = existingProject.StoragePath;
            NameBox.IsReadOnly = true;
            ChooseFolderButton.IsEnabled = false;
            _categories.Clear();
            foreach (var chapter in existingProject.Chapters ?? [])
            {
                var item = new RuleReviewCategoryItem { Number = _categories.Count + 1, Name = chapter.Name ?? string.Empty, ExistingChapter = chapter };
                foreach (var material in chapter.ReviewMaterials) item.Materials.Add(material);
                _categories.Add(item);
            }
            if (_categories.Count == 0) _categories.Add(new() { Number = 1 });
            WorkflowText.Text = "选择大类 → 预览题目 → 保存新增题目";
            StatusText.Text = "选择大类后填写本次新增题目的考查范围。";
        }
        CategoryList.ItemsSource = _categories;
        CategoryList.SelectedIndex = 0;
        if (_existingProject is not null) UpdateAdditionalControls();
        Closed += (_, _) => _lifetime.Cancel();
        Closing += (_, args) => { if (_saving) args.Cancel = true; };
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择项目保存目录" };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var category = new RuleReviewCategoryItem { Number = _categories.Count + 1 };
        _categories.Add(category);
        CategoryList.SelectedItem = category;
    }

    private void RemoveCategory_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not RuleReviewCategoryItem category) return;
        if (category.ExistingChapter is not null || _busy) return;
        var index = CategoryList.SelectedIndex;
        _categories.Remove(category);
        for (var i = 0; i < _categories.Count; i++) _categories[i].Number = i + 1;
        CategoryList.SelectedIndex = Math.Min(index, _categories.Count - 1);
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _loadingEditor = true;
        _editing = CategoryList.SelectedItem as RuleReviewCategoryItem;
        if (PromptBox is not null) PromptBox.Text = _editing?.Prompt ?? string.Empty;
        if (CategoryNameBox is not null) CategoryNameBox.Text = _editing?.Name ?? string.Empty;
        if (PromptBox is not null) PromptBox.IsEnabled = _editing is not null && !_busy;
        if (CategoryNameBox is not null) CategoryNameBox.IsEnabled = _editing is not null;
        if (MaterialList is not null) MaterialList.ItemsSource = _editing?.Materials;
        if (NoMaterialCheckBox is not null) RefreshMaterialControls();
        _loadingEditor = false;
        if (_existingProject is not null && PromptBox is not null)
        {
            _rejectedPrompt = null;
            UpdateAdditionalControls();
        }
    }

    private void CategoryNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingEditor && _editing is not null) _editing.Name = CategoryNameBox.Text;
    }

    private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingEditor && _editing is not null) _editing.Prompt = PromptBox.Text;
    }

    private async void ImportMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        var dialog = new OpenFileDialog { Title = "添加课程资料", Multiselect = true,
            Filter = "课程资料|*.pdf;*.txt;*.docx;*.doc;*.pptx;*.ppt|所有文件|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        var category = _editing;
        _busy = true;
        MaterialControls.IsEnabled = GenerateButton.IsEnabled = false;
        CategoryList.IsEnabled = AddCategoryButton.IsEnabled = RemoveCategoryButton.IsEnabled = false;
        try
        {
            foreach (var path in dialog.FileNames)
            {
                StatusText.Text = $"正在读取 {Path.GetFileName(path)}…";
                var content = await Task.Run(() => (string)ExtractText.FromAutomatic(path));
                if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("未读到资料文字。扫描件请先识别文字，或粘贴课程笔记。");
                category.Materials.Add(new(Path.GetFileName(path), content));
            }
            category.WithoutMaterialConfirmed = false;
            _rejectedPrompt = null;
            RefreshMaterialControls();
            StatusText.Text = "资料已添加。生成题目时会提取相关原文和课程核心要点。";
        }
        catch (Exception ex) { StatusText.Text = $"资料未添加：{ex.Message}"; }
        finally
        {
            _busy = false;
            MaterialControls.IsEnabled = GenerateButton.IsEnabled = true;
            CategoryList.IsEnabled = AddCategoryButton.IsEnabled = RemoveCategoryButton.IsEnabled = !_started;
            if (_existingProject is not null) UpdateAdditionalControls();
        }
    }

    private void AddPastedMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null || string.IsNullOrWhiteSpace(PastedMaterialBox.Text)) return;
        _editing.Materials.Add(new($"粘贴资料 {_editing.Materials.Count + 1}", PastedMaterialBox.Text.Trim()));
        PastedMaterialBox.Clear();
        _editing.WithoutMaterialConfirmed = false;
        _rejectedPrompt = null;
        RefreshMaterialControls();
    }

    private void RemoveMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is not null && sender is Button { Tag: ReviewMaterial material }) _editing.Materials.Remove(material);
        _rejectedPrompt = null;
        RefreshMaterialControls();
    }

    private void RefreshMaterialControls()
    {
        var loading = _loadingEditor;
        _loadingEditor = true;
        NoMaterialCheckBox.IsEnabled = _editing?.Materials.Count == 0;
        NoMaterialCheckBox.IsChecked = _editing?.WithoutMaterialConfirmed == true;
        _loadingEditor = loading;
    }

    private void NoMaterial_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingEditor || _editing is null) return;
        var accepted = MessageBox.Show(this, "不提供资料将会降低出题和判分的准确率。AI 会按本科课程的一般知识生成题目，可能与课程要求有出入。\n\n确定本次不提供资料吗？",
            "未提供复习资料", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        _editing.WithoutMaterialConfirmed = accepted;
        if (!accepted) NoMaterialCheckBox.IsChecked = false;
    }

    private void NoMaterial_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_loadingEditor && _editing is not null) _editing.WithoutMaterialConfirmed = false;
    }

    private static void ValidateMaterial(RuleReviewCategoryItem category)
    {
        if (category.Materials.Count == 0 && !category.WithoutMaterialConfirmed)
            throw new ArgumentException($"请为“{category.DisplayName}”添加资料，或主动选择“本次不提供资料”。");
    }

    private void ValidateSetup()
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." || name.EndsWith('.'))
            throw new ArgumentException("请填写有效的项目名称。");
        if (System.Text.RegularExpressions.Regex.IsMatch(name.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("该名称被 Windows 保留，请换一个项目名称。");
        if (!Directory.Exists(FolderBox.Text)) throw new ArgumentException("请选择项目保存目录。");
        if (Directory.Exists(Path.Combine(FolderBox.Text, name))) throw new ArgumentException("保存目录中已有同名文件夹，请修改项目名称。");
        if (_categories.Count == 0 || _categories.Any(c => string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Prompt)))
            throw new ArgumentException("请为每个大类填写名称和详细出题要求。");
        if (_categories.Select(c => c.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _categories.Count)
            throw new ArgumentException("聚类大类名称重复，请分别命名。");
        foreach (var category in _categories) ValidateMaterial(category);
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_existingProject is not null)
        {
            await GenerateAdditionalAsync();
            return;
        }
        try
        {
            if (!_started)
            {
                ValidateSetup();
                _started = true;
                ProjectFields.IsEnabled = CategoryList.IsEnabled = AddCategoryButton.IsEnabled = RemoveCategoryButton.IsEnabled = false;
                CategoryNameBox.IsReadOnly = true;
                CategoryList.SelectedIndex = 0;
            }
            _busy = true;
            GenerationProgress.Visibility = Visibility.Visible;
            WorkflowText.Text = $"正在处理第 {_completed.Count + 1}/{_categories.Count} 个大类";
            GenerateButton.IsEnabled = PromptBox.IsEnabled = false;
            MaterialControls.IsEnabled = false;
            if (_completed.Count < _categories.Count)
            {
                var category = _categories[_completed.Count];
                if (_accepted is null)
                {
                    if (_rejectedPrompt == category.Prompt.Trim()) throw new ArgumentException("请根据预览结果修改出题要求，再次生成题目。");
                    StatusText.Text = $"正在为“{category.Name}”生成预览题目与评分要点…";
                    var preview = await _service.PreviewAsync(category.ToRequest(), _lifetime.Token);
                    if (!AcceptPreview(preview))
                    {
                        _rejectedPrompt = preview.Prompt;
                        StatusText.Text = $"请修改“{category.Name}”的出题要求，再次生成预览。";
                        return;
                    }
                    _accepted = preview;
                    _rejectedPrompt = null;
                }
                StatusText.Text = $"已接受预览，正在分批并行生成“{category.Name}”的 {_accepted.QuestionCount} 道题…";
                var chapter = await _service.CompleteAsync(_accepted, _lifetime.Token);
                chapter.Number = _completed.Count + 1;
                _completed.Add(chapter);
                category.CompletedQuestionCount = chapter.Questions?.Count ?? 0;
                _accepted = null;
            }
            if (_completed.Count == _categories.Count)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                StatusText.Text = "所有大类已完成，正在保存项目…";
                _saving = true;
                var project = new Project { ProjectName = NameBox.Text.Trim(), StoragePath = FolderBox.Text,
                    TemplateType = ProjectTemplateType.RuleBasedReview, Chapters = _completed.ToList() };
                await _files.SaveProjectAsync(project);
                await _onCreated(project);
                _saving = false;
                DialogResult = true;
            }
            else
            {
                CategoryList.SelectedIndex = _completed.Count;
                ScopeTitle.Text = $"确认“{_editing?.Name}”的出题要求";
                StatusText.Text = $"已完成 {_completed.Count}/{_categories.Count} 类。请生成“{_editing?.Name}”的预览。";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"本步未完成：{ex.Message}" + (_completed.Count > 0 ? $" 当前窗口已保留 {_completed.Count} 个大类的题目。" : string.Empty);
        }
        finally
        {
            _busy = false;
            _saving = false;
            GenerationProgress.Visibility = Visibility.Collapsed;
            WorkflowText.Text = $"已完成 {_completed.Count}/{_categories.Count} 个大类";
            GenerateButton.IsEnabled = true;
            PromptBox.IsEnabled = _accepted is null && _completed.Count < _categories.Count;
            MaterialControls.IsEnabled = PromptBox.IsEnabled;
            GenerateButton.Content = _completed.Count == _categories.Count ? "重试保存项目" : _accepted is null ? _rejectedPrompt is null ? "生成预览题目" : "重新生成预览" : "重试完成本类题目";
        }
    }

    private async Task GenerateAdditionalAsync()
    {
        try
        {
            var category = _additionalTarget ?? _editing ?? throw new ArgumentException("请先选择或增加一个大类。");
            var request = category.ToRequest();
            ValidateMaterial(category);
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Prompt))
                throw new ArgumentException("请填写大类名称，以及本次新增题目的具体考查范围。");
            if (category.ExistingChapter is null && _existingProject!.Chapters?.Any(chapter => string.Equals(chapter.Name?.Trim(), request.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is true)
                throw new ArgumentException("当前项目已有同名大类，请选择该大类追加题目。");
            _busy = true;
            UpdateAdditionalControls();
            GenerationProgress.Visibility = Visibility.Visible;
            if (_pendingAdditional is null)
            {
                if (_accepted is null)
                {
                    if (_rejectedPrompt == request.Prompt.Trim()) throw new ArgumentException("请根据预览结果修改要求，再次生成题目。");
                    StatusText.Text = $"正在为“{request.Name}”生成新增题目的预览…";
                    var preview = await _service.PreviewAsync(request, _lifetime.Token);
                    if (!AcceptPreview(preview))
                    {
                        _rejectedPrompt = preview.Prompt;
                        StatusText.Text = "请修改本次出题要求，再次生成预览。";
                        return;
                    }
                    _accepted = preview;
                    _additionalTarget = category;
                    _rejectedPrompt = null;
                }
                StatusText.Text = $"预览已确认，正在分批并行生成 {_accepted.QuestionCount} 道题…";
                _pendingAdditional = await _service.CompleteAsync(_accepted, _lifetime.Token);
            }
            _lifetime.Token.ThrowIfCancellationRequested();
            _saving = true;
            StatusText.Text = "新增题目已生成，正在保存到当前项目…";
            var added = _pendingAdditional.Questions?.Count ?? 0;
            category.ExistingChapter = await new RuleReviewProjectEditor(_files)
                .AppendAsync(_existingProject!, category.ExistingChapter, _pendingAdditional);
            category.Prompt = string.Empty;
            category.RefreshSavedState();
            _accepted = null;
            _pendingAdditional = null;
            _additionalTarget = null;
            PromptBox.Text = string.Empty;
            StatusText.Text = $"已为“{category.Name}”保存 {added} 道新题，共 {category.ExistingChapter.Questions?.Count ?? 0} 道。可以继续出题或增加大类。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusText.Text = $"本次出题未完成：{ex.Message}" + (_pendingAdditional is not null ? " 已保留生成结果，可直接重试保存。" : string.Empty);
        }
        finally
        {
            _busy = _saving = false;
            GenerationProgress.Visibility = Visibility.Collapsed;
            UpdateAdditionalControls();
        }
    }

    private void UpdateAdditionalControls()
    {
        var pending = _accepted is not null || _pendingAdditional is not null;
        var editable = !_busy && !pending;
        CategoryList.IsEnabled = AddCategoryButton.IsEnabled = editable;
        RemoveCategoryButton.IsEnabled = editable && _editing is { ExistingChapter: null };
        CategoryNameBox.IsReadOnly = _editing?.ExistingChapter is not null;
        CategoryNameBox.IsEnabled = PromptBox.IsEnabled = editable && _editing is not null;
        MaterialControls.IsEnabled = editable && _editing is not null;
        GenerateButton.IsEnabled = !_busy && _editing is not null;
        GenerateButton.Content = _pendingAdditional is not null ? "重试保存新增题目" : _accepted is not null ? "重试完成本类题目" : "生成本类预览";
        DiscardBatchButton.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        DiscardBatchButton.IsEnabled = !_busy;
        ScopeTitle.Text = _editing?.ExistingChapter is not null ? $"为“{_editing.Name}”追加题目" : "填写新大类的出题要求";
        RequirementsHint.Text = "填写本次新增的考查对象、内容和详细程度。AI 会避开本类已有题目，并安排新增题目的数量。";
        PreviousScopeExpander.Visibility = string.IsNullOrWhiteSpace(_editing?.PreviousRequirements) ? Visibility.Collapsed : Visibility.Visible;
        PreviousRequirementsText.Text = _editing?.PreviousRequirements ?? string.Empty;
    }

    private void DiscardBatch_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _accepted = null;
        _pendingAdditional = null;
        _additionalTarget = null;
        _rejectedPrompt = null;
        StatusText.Text = "本次尚未保存的出题结果已取消，可重新填写要求。";
        UpdateAdditionalControls();
    }

    private bool AcceptPreview(RuleReviewPreview preview) => BuildPreview(preview, this).ShowDialog() == true;

    internal static Window BuildPreview(RuleReviewPreview preview, Window? owner) =>
        new RuleReviewPreviewWindow(preview) { Owner = owner };

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
