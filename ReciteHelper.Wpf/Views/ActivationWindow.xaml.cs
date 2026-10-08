using ReciteHelper.Core.Configuration;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Infrastructure.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ReciteHelper.Wpf.Views;

public partial class ActivationWindow : Window
{
    private readonly HostedModelService _hostedModelService;
    private readonly IConfigService _configService;
    private readonly IPiModelService _piModelService;
    private readonly string _configPath;

    public ActivationWindow(
        HostedModelService hostedModelService,
        IConfigService configService,
        IPiModelService piModelService)
    {
        _hostedModelService = hostedModelService;
        _configService = configService;
        _piModelService = piModelService;
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config.xml");
        InitializeComponent();
    }

    private async void SaveDirectKeysButton_Click(object sender, RoutedEventArgs e)
    {
        var deepSeekKey = DeepSeekKeyBox.Password.Trim();
        var qwenKey = QwenKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(deepSeekKey) || string.IsNullOrWhiteSpace(qwenKey))
        {
            ShowError("直连方案需要同时填写 DeepSeek 与 Qwen API Key。");
            return;
        }

        try
        {
            var config = await LoadConfigAsync();
            config.DeepSeekKey = deepSeekKey;
            config.QwenKey = qwenKey;
            config.PiOAuthProvider = config.PiOAuthModel = null;
            await _configService.SaveAsync(config);
            Complete("DeepSeek + Qwen 配置已保存。");
        }
        catch (Exception ex)
        {
            ShowError($"保存配置失败：{ex.Message}");
        }
    }

    private async void SaveOpenRouterButton_Click(object sender, RoutedEventArgs e)
    {
        var openRouterKey = OpenRouterKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(openRouterKey))
        {
            ShowError("请输入 OpenRouter API Key。");
            return;
        }

        try
        {
            var config = await LoadConfigAsync();
            config.OpenRouterKey = openRouterKey;
            config.DeepSeekKey = null;
            config.PiOAuthProvider = config.PiOAuthModel = null;
            if (string.IsNullOrWhiteSpace(config.OpenRouterChatModel))
                config.OpenRouterChatModel = "deepseek/deepseek-v3.2";
            if (string.IsNullOrWhiteSpace(config.OpenRouterEmbeddingModel))
                config.OpenRouterEmbeddingModel = "baai/bge-m3";

            await _configService.SaveAsync(config);
            Complete("OpenRouter 配置已保存。");
        }
        catch (Exception ex)
        {
            ShowError($"保存配置失败：{ex.Message}");
        }
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        var code = ActivationCodeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowError("请输入激活码。");
            return;
        }

        ActivateButton.IsEnabled = false;
        ShowStatus("正在验证激活码...", Brushes.DimGray);

        try
        {
            var result = await _hostedModelService.ActivateAsync(code);
            if (!result.IsValid)
            {
                ShowError(string.IsNullOrWhiteSpace(result.Message)
                    ? "激活失败，请检查激活码或服务连接。"
                    : $"激活失败：{result.Message}");
                return;
            }

            var config = await LoadConfigAsync();
            config.PiOAuthProvider = config.PiOAuthModel = null;
            config.DeepSeekKey = config.OpenRouterKey = null;
            await _configService.SaveAsync(config);
            Complete($"激活成功，剩余额度：{FormatQuota(result.QuotaRemaining)}。");
        }
        catch (Exception ex)
        {
            ShowError($"激活失败：{ex.Message}");
        }
        finally
        {
            ActivateButton.IsEnabled = true;
        }
    }

    private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureConfigFile();
            Process.Start(new ProcessStartInfo
            {
                FileName = _configPath,
                UseShellExecute = true
            });
            ShowStatus("已打开 Config.xml；修改保存后请重新启动软件。", Brushes.ForestGreen);
        }
        catch (Exception ex)
        {
            ShowError($"无法打开配置文件：{ex.Message}");
        }
    }

    private void PiOAuthButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PiOAuthWindow(_piModelService, _configService) { Owner = this };
        if (window.ShowDialog() == true) Complete("模型账号配置已保存。");
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private async Task<ConfigOptions> LoadConfigAsync()
    {
        try
        {
            return await _configService.LoadAsync();
        }
        catch
        {
            return new ConfigOptions();
        }
    }

    private void EnsureConfigFile()
    {
        if (File.Exists(_configPath))
            return;

        File.WriteAllText(
            _configPath,
            """
            <Config>
              <Version>v3</Version>
              <DeepSeekKey></DeepSeekKey>
              <QwenKey></QwenKey>
              <OpenRouterKey></OpenRouterKey>
              <OpenRouterChatModel>deepseek/deepseek-v3.2</OpenRouterChatModel>
              <OpenRouterEmbeddingModel>baai/bge-m3</OpenRouterEmbeddingModel>
              <PiOAuthProvider></PiOAuthProvider>
              <PiOAuthModel></PiOAuthModel>
              <ResourceCenterServerUrl>http://localhost:5000</ResourceCenterServerUrl>
              <HostedServiceUrl></HostedServiceUrl>
              <HostedLicenseCode></HostedLicenseCode>
              <HostedLicenseId></HostedLicenseId>
              <MissingStrategy>Ignore</MissingStrategy>
            </Config>
            """);
    }

    private void Complete(string message)
    {
        ShowStatus(message, Brushes.ForestGreen);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ShowStatus(message, Brushes.Firebrick);
    }

    private void ShowStatus(string message, Brush color)
    {
        StatusText.Foreground = color;
        StatusText.Text = message;
    }

    private static string FormatQuota(int quotaRemaining)
    {
        return quotaRemaining < 0 ? "不限" : quotaRemaining.ToString();
    }
}
