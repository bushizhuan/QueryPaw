using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SqlAnalyzer.App.ViewModels;
using SqlAnalyzer.App.Views;
using SqlAnalyzer.Core.Services;
using SqlAnalyzer.Data.Common;
using SqlAnalyzer.Data.Catalog;
using SqlAnalyzer.Data.Execution;
using SqlAnalyzer.Data.Explorer;
using SqlAnalyzer.Data.Formatting;
using SqlAnalyzer.Infrastructure.Storage;

namespace SqlAnalyzer.App;

public partial class App : Application
{
    private static readonly string StartupLogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAnalyzer.Next", "startup.log");
    private static void AppendStartupLog(string message)
    {
        string? directory = Path.GetDirectoryName(StartupLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(StartupLogPath, message + Environment.NewLine);
    }
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            try
            {
                AppendStartupLog($"FrameworkInit: {DateTime.Now:O}");
                WorkspacePathResolver paths = new(new WorkspaceLayoutOptions
                {
                    BaseDirectory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SqlAnalyzer.Next")
                });

                IDatabaseProviderCatalog providerCatalog = new DatabaseProviderCatalog();
                IConnectionProfileStore connectionStore = new JsonConnectionProfileStore(paths);
                IEditorSessionStore sessionStore = new JsonEditorSessionStore(paths);
                ILocalizationImportService localizationImportService = new LocalizationImportService(providerCatalog);
                ILocalizationResolver localizationResolver = new LocalizationResolver(localizationImportService);
                IDatabaseExplorerService explorerService = new DatabaseExplorerService(providerCatalog, localizationResolver);
                ICommentMaintenanceService commentMaintenanceService = new CommentMaintenanceService(providerCatalog);
                IModelDiagramService modelDiagramService = new ModelDiagramService(providerCatalog);
                IObjectEditorService objectEditorService = new ObjectEditorService(providerCatalog);
                ISqlExecutionService sqlExecutionService = new SqlExecutionService(providerCatalog);
                ISqlFormatterService sqlFormatterService = new PassthroughSqlFormatterService();

                MainWindowViewModel viewModel = new(
                    providerCatalog,
                    explorerService,
                    commentMaintenanceService,
                    modelDiagramService,
                    objectEditorService,
                    connectionStore,
                    sessionStore,
                    localizationResolver,
                    sqlExecutionService,
                    sqlFormatterService);
                viewModel.CreateDocument();

                MainWindow window = new()
                {
                    DataContext = viewModel,
                };
                desktop.MainWindow = window;
                AppendStartupLog($"MainWindowAssigned: {DateTime.Now:O}");

                base.OnFrameworkInitializationCompleted();

                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        window.EnsureVisibleOnStartup();
                        AppendStartupLog($"WindowEnsureVisibleCalled: {DateTime.Now:O}");
                        await viewModel.InitializeAsync();
                        AppendStartupLog($"ViewModelReady: {DateTime.Now:O}");
                    }
                    catch (Exception initEx)
                    {
                        AppendStartupLog($"AsyncInitError: {initEx}");
                    }
                });

                desktop.Exit += (_, _) =>
                {
                    try
                    {
                        window.PrepareForShutdown();
                        viewModel.PersistAsync().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        DatabaseDriverShutdown.ClearAllPools();
                    }
                };

                return;
            }
            catch (Exception ex)
            {
                AppendStartupLog($"StartupError: {ex}");
                Window errorWindow = new()
                {
                    Title = "Startup Error",
                    Width = 980,
                    Height = 680,
                    Content = new TextBox
                    {
                        Text = ex.ToString(),
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                };
                desktop.MainWindow = errorWindow;
                errorWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
