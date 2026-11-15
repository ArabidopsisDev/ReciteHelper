using AquaAvgFramework.StoryLineComponents;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Core.Aggregates;
using System.Windows;

namespace ReciteHelper.Wpf.Views;

/// <summary>
/// Interaction logic for GalWindow.xaml
/// </summary>
public partial class GalWindow : Window
{
    private readonly Project _currentProject;
    private readonly IGalGameService _galGameService;

    public GalWindow(Project project, IGalGameService galGameService)
    {
        _currentProject = project;
        _galGameService = galGameService;

        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var storyLines = await _galGameService.LoadStoryLinesAsync(_currentProject);
        GamePanel.StoryLines = storyLines.Cast<StoryLine>().ToList();
    }
}
