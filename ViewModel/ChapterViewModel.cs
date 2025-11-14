using ReciteHelper.Model;
using System.ComponentModel;

namespace ReciteHelper.ViewModel;

public class ChapterViewModel : INotifyPropertyChanged
{
    public Chapter Chapter { get; }

    public int QuestionCount => Chapter.Questions?.Count ?? 0;

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
