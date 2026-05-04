using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using OrderLog.Features.Services;
using OrderLog.Features.ViewModels;
using OrderLog.Services;
using OrderLog.Windows;

namespace OrderLog;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        Directory.CreateDirectory(Core.AppPaths.LogsDir);
        var logPath = Path.Combine(Core.AppPaths.LogsDir, "orderlog-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        try
        {
            Log.Information("Starting SS Command Centre");

            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                var ex = ev.ExceptionObject as Exception;
                Log.Fatal(ex, "Unhandled exception in AppDomain");
            };

            TaskScheduler.UnobservedTaskException += (s, ev) =>
            {
                Log.Fatal(ev.Exception, "Unobserved task exception");
                ev.SetObserved();
            };

            this.DispatcherUnhandledException += (s, ev) =>
            {
                Log.Fatal(ev.Exception, "Dispatcher unhandled exception");
                ev.Handled = true;
            };

            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services);
                })
                .Build();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize application");
            throw;
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        Core.AppPaths.EnsureDirectoriesExist();

        // OrderLog services
        services.AddSingleton<IOrderLogService>(sp =>
            OrderLogRepository.GetInstance(
                sp.GetService<ILogger<OrderLogRepository>>()));
        services.AddSingleton<OrderLogViewModel>();
        services.AddSingleton<GroupStateStore>();

        // Shared services
        services.AddSingleton(ThemeService.Instance);
        services.AddSingleton<DialogService>();
        services.AddSingleton<Infrastructure.Services.SettingsService>();

        // Window
        services.AddTransient<OrderLogWidgetWindow>();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            await _host.StartAsync();

            // Initialize theme
            Dispatcher.Invoke(() =>
            {
                var themeService = _host.Services.GetRequiredService<ThemeService>();
                themeService.Initialize();
            });

            // Show widget window
            var viewModel = _host.Services.GetRequiredService<OrderLogViewModel>();
            var window = new OrderLogWidgetWindow(viewModel, _host.Services);
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed during application startup");
            MessageBox.Show($"Failed to start: {ex.Message}", "Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            // Dispose static singletons not managed by the DI container.
            // SpotifyService holds two System.Timers.Timer instances and WinRT SMTC
            // event subscriptions (COM apartment thread); not disposing these prevents
            // the process from exiting cleanly.
            SpotifyService.Instance.Dispose();

            using (_host)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during application shutdown");
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    public static T GetService<T>() where T : class
    {
        if (Current is not App app)
            throw new InvalidOperationException("App is not initialized");

        return app._host.Services.GetRequiredService<T>();
    }
}
