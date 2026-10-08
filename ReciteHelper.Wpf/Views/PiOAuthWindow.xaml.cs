using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;

namespace ReciteHelper.Wpf.Views;

public partial class PiOAuthWindow : Window
{
    private readonly IPiModelService _pi;
    private readonly IConfigService _config;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _operation;
    private TaskCompletionSource<string>? _pendingPrompt;
    private bool _loading;
    private bool _busy;

    public PiOAuthWindow(IPiModelService pi, IConfigService config)
    {
        _pi = pi;
        _config = config;
        InitializeComponent();
        Loaded += async (_, _) => await ExecuteAsync(async token =>
        {
            var saved = await _config.LoadAsync();
            QwenEmbeddingKeyBox.Password = saved.QwenKey ?? string.Empty;
            await RefreshProvidersAsync(saved.PiOAuthProvider, token);
            await LoadModelsAsync(saved.PiOAuthModel, token);
        });
        Closing += (_, _) => _lifetime.Cancel();
    }

    private PiOAuthProvider? SelectedProvider => ProviderBox.SelectedItem as PiOAuthProvider;

    private async Task RefreshProvidersAsync(string? selected, CancellationToken token)
    {
        var providers = await _pi.GetProvidersAsync(token);
        _loading = true;
        try
        {
            ProviderBox.ItemsSource = providers;
            ProviderBox.SelectedValue = selected;
            if (ProviderBox.SelectedIndex < 0 && providers.Count > 0) ProviderBox.SelectedIndex = 0;
        }
        finally { _loading = false; }
    }

    private async Task LoadModelsAsync(string? selected, CancellationToken token)
    {
        ModelBox.ItemsSource = null;
        if (SelectedProvider is not { } provider) return;
        ModelBox.ItemsSource = await _pi.GetModelsAsync(provider.Id, token);
        ModelBox.SelectedValue = selected;
        if (ModelBox.SelectedIndex < 0 && ModelBox.Items.Count > 0) ModelBox.SelectedIndex = 0;
        ShowStatus(provider.LoggedIn ? "已登录，请选择模型后保存。" : "请先登录所选服务。", Brushes.DimGray);
    }

    private async void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _busy) return;
        AuthPanel.Visibility = InfoBox.Visibility = Visibility.Collapsed;
        await ExecuteAsync(token => LoadModelsAsync(null, token));
    }

    private void ModelBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider is not { } provider) return;
        await ExecuteAsync(async token =>
        {
            AuthPanel.Visibility = InfoBox.Visibility = Visibility.Collapsed;
            ShowStatus("正在开始授权…", Brushes.DimGray);
            await _pi.LoginAsync(provider.Id, AskAsync, Notify, token);
            await RefreshProvidersAsync(provider.Id, token);
            await LoadModelsAsync(null, token);
            AuthPanel.Visibility = InfoBox.Visibility = Visibility.Collapsed;
        });
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider is not { } provider) return;
        await ExecuteAsync(async token =>
        {
            await _pi.LogoutAsync(provider.Id, token);
            var config = await _config.LoadAsync();
            if (config.PiOAuthProvider == provider.Id)
            {
                config.PiOAuthProvider = config.PiOAuthModel = null;
                await _config.SaveAsync(config);
                Models.Config.Use(config);
            }
            await RefreshProvidersAsync(provider.Id, token);
            await LoadModelsAsync(null, token);
            ShowStatus("本机授权已移除。可在服务商账号设置中撤销授权。", Brushes.ForestGreen);
        });
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider is not { LoggedIn: true } provider || ModelBox.SelectedItem is not PiChatModel model) return;
        await ExecuteAsync(async _ =>
        {
            var config = await _config.LoadAsync();
            config.PiOAuthProvider = provider.Id;
            config.PiOAuthModel = model.Id;
            config.QwenKey = QwenEmbeddingKeyBox.Password.Trim();
            await _config.SaveAsync(config);
            Models.Config.Use(config);
            DialogResult = true;
        });
    }

    private async Task<string> AskAsync(PiAuthPrompt prompt, CancellationToken token)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.InvokeAsync(() =>
        {
            _pendingPrompt = completion;
            PromptText.Text = prompt.Message + (string.IsNullOrWhiteSpace(prompt.Placeholder) ? "" : $"\n{prompt.Placeholder}");
            PromptInput.Clear();
            PromptSecret.Clear();
            PromptInput.Visibility = prompt.Type is "text" or "manual_code" ? Visibility.Visible : Visibility.Collapsed;
            PromptSecret.Visibility = prompt.Type == "secret" ? Visibility.Visible : Visibility.Collapsed;
            PromptChoices.Visibility = prompt.Type == "select" ? Visibility.Visible : Visibility.Collapsed;
            PromptChoices.ItemsSource = prompt.Options;
            PromptChoices.SelectedIndex = 0;
            PromptPanel.Visibility = Visibility.Visible;
            if (prompt.Type == "secret") PromptSecret.Focus(); else PromptInput.Focus();
        });
        using var registration = token.Register(() => completion.TrySetCanceled(token));
        try { return await completion.Task.ConfigureAwait(false); }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_pendingPrompt != completion) return;
                _pendingPrompt = null;
                PromptInput.Clear();
                PromptSecret.Clear();
                PromptPanel.Visibility = Visibility.Collapsed;
            });
        }
    }

    private void SubmitPrompt_Click(object sender, RoutedEventArgs e)
    {
        var value = PromptSecret.Visibility == Visibility.Visible ? PromptSecret.Password
            : PromptChoices.Visibility == Visibility.Visible ? PromptChoices.SelectedValue as string ?? ""
            : PromptInput.Text;
        _pendingPrompt?.TrySetResult(value);
    }

    private void Notify(PiAuthEvent authEvent)
    {
        Dispatcher.Invoke(() =>
        {
            if (_lifetime.IsCancellationRequested) return;
            if (authEvent.Type is "auth_url" or "device_code")
            {
                AuthPanel.Visibility = Visibility.Visible;
                AuthUrlBox.Text = authEvent.Url ?? authEvent.VerificationUri ?? "";
                AuthInstructions.Text = authEvent.Type == "device_code"
                    ? $"请在授权页面输入设备码：{authEvent.UserCode}"
                    : authEvent.Instructions ?? "请在浏览器中完成授权。若回调无法返回，可在下方粘贴授权结果。";
                OpenBrowser(AuthUrlBox.Text);
            }
            else if (authEvent.Type == "info")
            {
                InfoBox.Text = authEvent.Message + string.Concat((authEvent.Links ?? []).Select(link => $"\n{link.Label}: {link.Url}"));
                InfoBox.Visibility = Visibility.Visible;
            }
            else if (authEvent.Type == "progress") ShowStatus(authEvent.Message ?? "正在授权…", Brushes.DimGray);
        });
    }

    private void OpenAuthPage_Click(object sender, RoutedEventArgs e) => OpenBrowser(AuthUrlBox.Text);

    private void OpenBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { ShowStatus("无法自动打开浏览器，请复制授权链接。", Brushes.DarkOrange); }
    }

    private void CancelOperationButton_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void BackButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        UpdateButtons();
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { ShowStatus("操作已取消。", Brushes.DimGray); }
        catch (Exception ex) { ShowStatus(ex.Message, Brushes.Firebrick); }
        finally
        {
            _operation.Dispose();
            _operation = null;
            _busy = false;
            UpdateButtons();
        }
    }

    private void UpdateButtons()
    {
        if (SaveButton is null) return;
        ProviderBox.IsEnabled = ModelBox.IsEnabled = QwenEmbeddingKeyBox.IsEnabled = !_busy;
        LoginButton.IsEnabled = !_busy && SelectedProvider is not null;
        LogoutButton.IsEnabled = !_busy && SelectedProvider?.LoggedIn == true;
        CancelOperationButton.IsEnabled = _busy;
        SaveButton.IsEnabled = !_busy && SelectedProvider?.LoggedIn == true && ModelBox.SelectedItem is PiChatModel;
    }

    private void ShowStatus(string text, Brush color)
    {
        StatusText.Text = text;
        StatusText.Foreground = color;
    }
}
