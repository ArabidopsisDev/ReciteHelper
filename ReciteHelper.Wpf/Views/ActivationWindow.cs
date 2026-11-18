using System.Windows;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.Wpf.Views;

public sealed class ActivationWindow : Window
{
    public ActivationWindow(HostedModelService hostedModelService, IConfigService configService, IPiModelService piModelService)
    {
        Style = (Style)FindResource("AppWindowStyle");
        Title = "模型服务设置";
        Width = 960;
        Height = 680;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var settings = new ModelSettingsView(hostedModelService, configService, piModelService);
        settings.SettingsSaved += (_, _) => DialogResult = true;
        settings.CloseRequested += (_, _) => DialogResult = false;
        Content = settings;
    }
}
