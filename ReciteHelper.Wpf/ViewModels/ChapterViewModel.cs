using ReciteHelper.Core.Entities;
using ReciteHelper.Wpf.Models;
using System.ComponentModel;

namespace ReciteHelper.Wpf.ViewModels;

public class ChapterViewModel : INotifyPropertyChanged
{
    public Chapter Chapter { get; }

    public int QuestionCount => Chapter.Questions?.Count(question => !question.IsArchived) ?? 0;

    private double _masteryLevel;
    public double MasteryLevel
    {
        get => _masteryLevel;
        set
        {
            _masteryLevel = value;
            OnPropertyChanged(nameof(MasteryLevel));
        }
    }

    public ChapterViewModel(Chapter chapter)
    {
        Chapter = chapter;
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
