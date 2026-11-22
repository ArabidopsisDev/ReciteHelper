using ReciteHelper.Infrastructure.Services;
using ReciteHelper.Wpf.Views;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace ReciteHelper.Wpf;

public partial class MainWindow
{
    private readonly LocalStudyAccount _studyAccount = new();
    private TabItem? _studyDashboardTab;
    private Task _homeInitialization = Task.CompletedTask;

    private async Task InitializeHomeAsync()
    {
        await LoadRecentProjectsAsync();
        await RegisterStudyProjectsAsync(recentProjects.Where(project => !string.IsNullOrWhiteSpace(project.ProjectPath))
            .Select(project => new StudyProject(project.ProjectPath)));
    }

    private async Task RegisterStudyProjectsAsync(IEnumerable<StudyProject> projects)
    {
        try { await _studyAccount.RegisterAsync(projects); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Trace.WriteLine(ex);
        }
    }

    private async void StudyDashboard_Click(object sender, RoutedEventArgs e)
    {
        await _homeInitialization;
        _studyDashboardTab ??= CreateTab("学习看板", new StudyDashboardView(_studyAccount));
        SelectTab(_studyDashboardTab);
    }
}
