using CommunityToolkit.Mvvm.ComponentModel;
using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using System.Collections.ObjectModel;

namespace ReciteHelper.Wpf.Models;

public partial class RuleReviewCategoryItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _completedQuestionCount;

    public ObservableCollection<ReviewMaterial> Materials { get; } = [];
    public bool WithoutMaterialConfirmed { get; set; }

    public Chapter? ExistingChapter { get; set; }
    public string PreviousRequirements => ExistingChapter is null ? string.Empty : string.Join("\n\n",
        new[] { ExistingChapter.GenerationPrompt }.Concat(ExistingChapter.AdditionalGenerationPrompts).Where(text => !string.IsNullOrWhiteSpace(text)));

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名大类" : Name;
    public string StatusText => ExistingChapter is not null
        ? $"已有 {ExistingChapter.Questions?.Count ?? 0} 道题，{(string.IsNullOrWhiteSpace(Prompt) ? "可追加出题" : "已填写追加要求")}" : CompletedQuestionCount > 0 ? $"已完成，共 {CompletedQuestionCount} 道题"
        : string.IsNullOrWhiteSpace(Prompt) ? "待填写出题要求" : "已填写出题要求";

    public void RefreshSavedState() => OnPropertyChanged(nameof(StatusText));

    public RuleReviewCategory ToRequest() => new() { Name = Name, Prompt = Prompt, IsAppending = ExistingChapter is not null, Materials = Materials.ToList(),
        ExistingQuestionTexts = (ExistingChapter?.Questions ?? []).SelectMany(question => new[] { question.Text }
            .Concat(question.ReviewFacets.Select(facet => facet.Question.Text))).Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text!).ToList() };
}
