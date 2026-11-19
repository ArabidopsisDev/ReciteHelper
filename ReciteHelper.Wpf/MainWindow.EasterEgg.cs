using ReciteHelper.Wpf.Views;
using System.Windows.Controls;

namespace ReciteHelper.Wpf;

public partial class MainWindow
{
    private void ShowAiHomeEasterEgg()
    {
        var page = new AiHomeEasterEgg();
        ((Panel)RecentProjectsPanel.Parent).Children.Remove(RecentProjectsPanel);
        page.RecentProjectsHost.Content = RecentProjectsPanel;
        Resources["RecentItemStyle"] = page.FindResource("AiRecentItemStyle");
        page.CreateProjectButton.Click += CreateNewProject_Click;
        page.OpenProjectButton.Click += LoadProject_Click;
        page.ImportProjectButton.Click += OpenFolder_Click;
        page.ResourceCenterButton.Click += ResourceCenter_Click;
        page.ModelSettingsButton.Click += ModelSettings_Click;
        HomeTab.Content = page;
    }
}
