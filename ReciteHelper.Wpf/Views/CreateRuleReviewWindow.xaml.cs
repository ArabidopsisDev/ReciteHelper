using Microsoft.Win32;
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

    public CreateRuleReviewWindow(IRuleReviewService service, IProjectFileService files, Func<Project, Task> onCreated)
    {
        _service = service;
        _files = files;
        _onCreated = onCreated;
        InitializeComponent();
        CategoryList.ItemsSource = _categories;
        CategoryList.SelectedIndex = 0;
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
        var index = CategoryList.SelectedIndex;
        _categories.Remove(category);
        for (var i = 0; i < _categories.Count; i++) _categories[i].Number = i + 1;
        CategoryList.SelectedIndex = Math.Min(index, _categories.Count - 1);
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _editing = CategoryList.SelectedItem as RuleReviewCategoryItem;
        if (PromptBox is not null) PromptBox.Text = _editing?.Prompt ?? string.Empty;
        if (CategoryNameBox is not null) CategoryNameBox.Text = _editing?.Name ?? string.Empty;
        if (PromptBox is not null) PromptBox.IsEnabled = _editing is not null && !_busy;
        if (CategoryNameBox is not null) CategoryNameBox.IsEnabled = _editing is not null;
    }

    private void CategoryNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_editing is not null) _editing.Name = CategoryNameBox.Text;
    }

    private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_editing is not null) _editing.Prompt = PromptBox.Text;
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
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
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
            if (_completed.Count < _categories.Count)
            {
                var category = _categories[_completed.Count];
                if (_accepted is null)
                {
                    if (_rejectedPrompt == category.Prompt.Trim()) throw new ArgumentException("请根据预览结果修改出题要求，再次生成题目。");
                    StatusText.Text = $"正在为“{category.Name}”生成两道预览题…";
                    var preview = await _service.PreviewAsync(category.ToRequest(), _lifetime.Token);
                    if (!AcceptPreview(preview))
                    {
                        _rejectedPrompt = preview.Prompt;
                        StatusText.Text = $"请修改“{category.Name}”的出题要求，再次生成两道预览题。";
                        return;
                    }
                    _accepted = preview;
                    _rejectedPrompt = null;
                }
                StatusText.Text = $"已接受“{category.Name}”的预览题，正在完成本类全部题目…";
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
                StatusText.Text = $"已完成 {_completed.Count}/{_categories.Count} 类。请预览“{_editing?.Name}”的两道题。";
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
            GenerateButton.Content = _completed.Count == _categories.Count ? "重试保存项目" : _accepted is null ? _rejectedPrompt is null ? "生成两道预览题" : "重新生成预览" : "重试完成本类题目";
        }
    }

    private bool AcceptPreview(RuleReviewPreview preview) => BuildPreview(preview, this).ShowDialog() == true;

    internal static Window BuildPreview(RuleReviewPreview preview, Window? owner) =>
        new RuleReviewPreviewWindow(preview) { Owner = owner };

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
