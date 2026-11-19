using ReciteHelper.Core.DTOs;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Enums;
using ReciteHelper.Wpf.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ReciteHelper.Wpf.ViewModels;

public class QuestionItem : INotifyPropertyChanged
{
    public int Number
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    }
    public Question? Question { get; set; }
    public AnswerStatus Status { get; set; }
    public string? UserAnswer { get; set; }
    internal string? DraftText { get; set; }
    internal IReadOnlyList<string>? DraftParts { get; set; }
    public AiAnswerAssessment? Assessment { get; set; }
    public List<ReviewTag> ReviewTag { get; set; } = [];

    public Style? StatusStyle
    {
        get => field;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
