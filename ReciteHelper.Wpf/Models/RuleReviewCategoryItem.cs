using CommunityToolkit.Mvvm.ComponentModel;
using ReciteHelper.Core.DTOs;

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

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名大类" : Name;
    public string StatusText => CompletedQuestionCount > 0 ? $"已完成 · {CompletedQuestionCount} 道题"
        : string.IsNullOrWhiteSpace(Prompt) ? "待填写出题要求" : "已填写出题要求";

    public RuleReviewCategory ToRequest() => new() { Name = Name, Prompt = Prompt };
}
