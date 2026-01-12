using AquaAvgFramework.StoryLineComponents;
using ReciteHelper.Model;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace ReciteHelper.View;

/// <summary>
/// Interaction logic for GalWindow.xaml
/// </summary>
public partial class GalWindow : Window
{
    private Project _currentProject;
    public GalWindow(Project project)
    {
        InitializeComponent();

        _currentProject = project;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var gamePath = Path.Combine(_currentProject.StoragePath!, _currentProject.ProjectName!, "game.rhgal");

        var text = File.ReadAllText(gamePath);
        var options = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true
        };

        var storyLine = JsonSerializer.Deserialize<StoryLine>(text, options);

        GamePanel.StoryLines = [storyLine!];
    }
}
