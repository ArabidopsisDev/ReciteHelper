using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReciteHelper.Core.Interfaces.Configuration;
using ReciteHelper.Core.Interfaces.Services;
using ReciteHelper.Application.Services;
using ReciteHelper.Infrastructure.Configuration;
using ReciteHelper.Infrastructure.Services;
using ReciteHelper.Wpf.Views;
using ReciteHelper.Core.Configuration;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ReciteHelper.Wpf;

public partial class App : System.Windows.Application
{
    private IServiceProvider? _serviceProvider;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var services = new ServiceCollection();

        var configService = new ConfigService();
        ReciteHelper.Core.Configuration.ConfigOptions appConfig;
        try
        {
            appConfig = await configService.LoadAsync();
        }
        catch
        {
            appConfig = new ReciteHelper.Core.Configuration.ConfigOptions();
        }
        Models.Config.Use(appConfig);

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton<IConfigService>(configService);
        services.AddSingleton(appConfig);

        services.AddSingleton<IAnswerJudge, SbertModelJudge>();
        services.AddSingleton<IQuizService, QuizService>();
        services.AddSingleton<IReviewGenerator, ReviewGenerator>();
        services.AddSingleton<IExamPaperService, ExamPaperService>();
        services.AddSingleton<IExamSettingsService, ExamSettingsService>();
        services.AddSingleton<IExamAnswerService, ExamAnswerService>();
        services.AddSingleton<IExamSourceTextReader, ExamSourceTextReader>();
        services.AddSingleton<IExamSetRepository, JsonExamSetRepository>();
        services.AddSingleton<IExamSetImportService, ExamSetImportService>();
        services.AddSingleton<IProjectFileService, ProjectFileService>();
        services.AddSingleton<IProjectCreationService, ProjectCreationService>();
        services.AddSingleton<IQuestionBankTextService, QuestionBankTextService>();
        services.AddSingleton<HostedModelService>();
        services.AddSingleton<IPiModelService, PiModelService>();
        services.AddSingleton<IKnowledgeBaseService, KnowledgeBaseService>();
        services.AddSingleton<IAiChatService, AiChatService>();
        services.AddSingleton<IQuestionHelpService, QuestionHelpService>();
        services.AddSingleton<IStartupCompatibilityService, StartupCompatibilityService>();
        services.AddSingleton<IRecentProjectService, RecentProjectService>();
        services.AddSingleton<IFileMergeService, FileMergeService>();
        services.AddSingleton<IPromptProvider, PromptProvider>();
        services.AddSingleton<IPhonkService, PhonkService>();
        services.AddSingleton<IReviewScheduler, FsrsReviewScheduler>();
        services.AddSingleton<IReviewPersonalizationService, ReviewPersonalizationService>();

        services.AddTransient<ActivationWindow>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var hostedModelService = _serviceProvider.GetRequiredService<HostedModelService>();
        if (!await EnsureModelAccessAsync(appConfig, hostedModelService))
        {
            Shutdown();
            return;
        }

        Models.Config.Use(await configService.LoadAsync());

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    private async Task<bool> EnsureModelAccessAsync(
        ReciteHelper.Core.Configuration.ConfigOptions config,
        HostedModelService hostedModelService)
    {
        var accessMode = ModelAccess.Resolve(config);
        if (accessMode is ModelAccessMode.DeepSeekAndQwen or ModelAccessMode.OpenRouter)
            return true;

        if (accessMode == ModelAccessMode.PiOAuth)
        {
            try
            {
                var providers = await _serviceProvider!.GetRequiredService<IPiModelService>().GetProvidersAsync();
                if (providers.Any(provider => provider.Id == config.PiOAuthProvider && provider.LoggedIn)) return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "模型账号配置", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        if (accessMode == ModelAccessMode.Hosted && !string.IsNullOrWhiteSpace(config.HostedLicenseId))
        {
            var validation = await hostedModelService.ValidateAsync();
            if (validation.IsValid)
                return true;
        }

        var activationWindow = _serviceProvider!.GetRequiredService<ActivationWindow>();
        return activationWindow.ShowDialog() == true;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportUnhandledException(e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            ReportUnhandledException(exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportUnhandledException(e.Exception);
        e.SetObserved();
    }

    private static void ReportUnhandledException(Exception exception)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReciteHelper");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Last-resort error reporting must never throw another exception.
        }

        try
        {
            MessageBox.Show(
                $"程序遇到未处理异常：{exception.Message}",
                "未处理异常",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // The dispatcher may already be shutting down.
        }
    }
}
