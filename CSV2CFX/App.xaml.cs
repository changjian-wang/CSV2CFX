using CSV2CFX.AppSettings;
using CSV2CFX.Extensions;
using CSV2CFX.Interfaces;
using CSV2CFX.Models;
using CSV2CFX.Services;
using CSV2CFX.ViewModels;
using CSV2CFX.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CSV2CFX
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;
        private ILogger<App>? _logger;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 自动更新
            AutoUpdate(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            var configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    // 清除默认配置源
                    config.Sources.Clear();

                    // 添加基础 JSON 配置
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                    // 添加我们的动态配置源
                    config.Add(new DynamicConfigurationSource(configFilePath));

                    // 添加环境变量
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((context, services) =>
                {
                    // 注册动态配置服务
                    services.AddSingleton<IDynamicConfigurationService, DynamicConfigurationService>();

                    // 绑定配置到 IOptions<T> - 这些会自动创建 IOptionsMonitor<T>
                    services.Configure<RabbitmqSetting>(context.Configuration.GetSection("RabbitMQ"));
                    services.Configure<BackgroundTaskSetting>(context.Configuration.GetSection("BackgroundTask"));
                    services.Configure<MachineInfoSetting>(context.Configuration.GetSection("MachineInfo"));
                    services.Configure<MachineMetadataSetting>(context.Configuration.GetSection("MachineMetadata"));
                    services.Configure<CsvFilePathSetting>(context.Configuration.GetSection("CsvFilePath"));
                    services.Configure<RabbitMQPublisherSettings>(context.Configuration.GetSection("RabbitMQPublisherSettings"));
                    services.Configure<ApiSetting>(context.Configuration.GetSection("Api"));
                    // 注册 ViewModel，注入 IConfiguration
                    services.AddTransient<ConfigurationViewModel>(provider =>
                    {
                        var machineInfoOptions = provider.GetRequiredService<IOptionsMonitor<MachineInfoSetting>>();
                        var apiOptions = provider.GetRequiredService<IOptionsMonitor<ApiSetting>>();
                        var configuration = provider.GetRequiredService<IConfiguration>();
                        return new ConfigurationViewModel(machineInfoOptions, apiOptions, configuration);
                    });

                    services.AddTransient<ConfigurationPage>(provider =>
                    {
                        var machineInfoOptions = provider.GetRequiredService<IOptionsMonitor<MachineInfoSetting>>();
                        var apiOptions = provider.GetRequiredService<IOptionsMonitor<ApiSetting>>();
                        var configuration = provider.GetRequiredService<IConfiguration>();
                        return new ConfigurationPage(machineInfoOptions, apiOptions, configuration);
                    });

                    // RabbitMQ 连接工厂 - 使用 IOptionsMonitor 以支持动态更新
                    services.AddSingleton<IConnectionFactory>(provider =>
                    {
                        var rabbitMQOptions = provider.GetRequiredService<IOptionsMonitor<RabbitmqSetting>>();
                        var rabbitMQSetting = rabbitMQOptions.CurrentValue;

                        var factory = new ConnectionFactory()
                        {
                            HostName = rabbitMQSetting.HostName ?? "localhost",
                            UserName = rabbitMQSetting.UserName ?? "admin",
                            Password = rabbitMQSetting.Password ?? "123456",
                            Port = rabbitMQSetting.Port,
                            VirtualHost = rabbitMQSetting.VirtualHost ?? "/",
                            AutomaticRecoveryEnabled = rabbitMQSetting.AutomaticRecoveryEnabled,
                            NetworkRecoveryInterval = TimeSpan.FromSeconds(rabbitMQSetting.NetworkRecoveryIntervalSeconds)
                        };

                        // 订阅配置更改事件，更新连接工厂
                        rabbitMQOptions.OnChange(newSetting =>
                        {
                            factory.HostName = newSetting.HostName ?? "localhost";
                            factory.UserName = newSetting.UserName ?? "admin";
                            factory.Password = newSetting.Password ?? "123456";
                            factory.Port = newSetting.Port;
                            factory.VirtualHost = newSetting.VirtualHost ?? "/";
                            factory.AutomaticRecoveryEnabled = newSetting.AutomaticRecoveryEnabled;
                            factory.NetworkRecoveryInterval = TimeSpan.FromSeconds(newSetting.NetworkRecoveryIntervalSeconds);

                            var logger = provider.GetRequiredService<ILogger<App>>();
                            logger.LogInformation("RabbitMQ 连接配置已更新");
                        });

                        return factory;
                    });

                    // 注册 RabbitMQ 连接工厂包装器
                    services.AddSingleton<IRabbitMQConnectionFactory, RabbitMQConnectionFactory>();

                    // 注册 RabbitMQ 服务
                    services.AddSingleton<IRabbitMQService, RabbitMQService>();

                    // 注册其他服务
                    services.AddSingleton<IMachineService, MachineService>();

                    // HttpClient 配置 - 支持动态 API 端点更新
                    services.AddHttpClient();
                    services.AddHttpClient("FlexApiClient", (provider, client) =>
                    {
                        var apiOptions = provider.GetRequiredService<IOptionsMonitor<ApiSetting>>();
                        var apiSetting = apiOptions.CurrentValue;

                        if (!string.IsNullOrEmpty(apiSetting.Endpoint))
                        {
                            client.BaseAddress = new Uri(apiSetting.Endpoint);
                        }

                        // 订阅 API 配置更改
                        apiOptions.OnChange(newApiSetting =>
                        {
                            if (!string.IsNullOrEmpty(newApiSetting.Endpoint))
                            {
                                client.BaseAddress = new Uri(newApiSetting.Endpoint);
                            }

                            var logger = provider.GetRequiredService<ILogger<App>>();
                            logger.LogInformation("API 端点配置已更新: {Endpoint}", newApiSetting.Endpoint);
                        });
                    });

                    // 注册 MainWindow 及其依赖项
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("应用程序启动中...");

            // 设置全局异常处理
            SetupGlobalExceptionHandling();

            _host.Start();

            // 显示主窗口
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            _logger.LogInformation("应用程序启动完成");

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _logger?.LogInformation("应用程序正在关闭...");

            try
            {
                _host?.StopAsync(TimeSpan.FromSeconds(5)).Wait();
                _host?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "关闭应用程序时发生错误");
            }

            _logger?.LogInformation("应用程序已关闭");
            base.OnExit(e);
        }

        private void SetupGlobalExceptionHandling()
        {
            // 处理 UI 线程上的未捕获异常
            DispatcherUnhandledException += (sender, e) =>
            {
                _logger?.LogError(e.Exception, "UI 线程上发生未捕获的异常");

                MessageBox.Show($"应用程序发生错误:\n\n{e.Exception.Message}\n\n请重启应用程序。",
                    "应用程序错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                e.Handled = true;
                Shutdown(1);
            };

            // 处理非 UI 线程上的未捕获异常
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception exception)
                {
                    _logger?.LogCritical(exception, "应用程序域中发生未捕获的异常");
                }
            };

            // 处理 Task 中的未捕获异常
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                _logger?.LogError(e.Exception, "Task 中发生未观察到的异常");
                e.SetObserved();
            };
        }

        private void AutoUpdate(StartupEventArgs e)
        {
            for (int i = 0; i != e.Args.Length; ++i)
            {
                if (e.Args[i] == "/update")
                {
                    MessageBox.Show("应用程序已成功更新!", "更新完成",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }
            }
        }
    }
}