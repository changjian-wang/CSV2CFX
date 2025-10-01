using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Services;
using Flex.Csv2Cfx.ViewModels;
using Flex.Csv2Cfx.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet.Adapter;
using MQTTnet.Implementations;
using System.Configuration;
using System.Data;
using System.Windows;

namespace Flex.Csv2Cfx
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static IHost? _host;

        public static IHost Host => _host ??= Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                // 配置应用程序设置
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).AddEnvironmentVariables();
            })
            .ConfigureServices(ConfigureServices)
            .Build();

        private static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            // 注册MQTT客户端工厂
            services.AddSingleton<IMqttClientAdapterFactory, MqttClientAdapterFactory>();

            // 注册其他服务
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IMessageService, MessageService>();
            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<IUserContext, UserContext>();

            // 注册ViewModels
            services.AddTransient<LoginViewModel>();
            services.AddTransient<MainViewModel>();

            // 注册Views
            services.AddSingleton<LoginWindow>();
            services.AddSingleton<MainWindow>();
        }

        public static T GetService<T>() where T : class
        {
            return Host.Services.GetRequiredService<T>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await Host.StartAsync();

            // 显示登录窗口
            var loginWindow = GetService<LoginWindow>();
            if (loginWindow.ShowDialog() == true)
            {
                // 登录成功，显示主窗口
                var mainWindow = GetService<MainWindow>();
                mainWindow.Show();
            }
            else
            {
                // 登录失败，退出应用
                Shutdown();
            }

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            using (var scope = Host.Services.CreateScope())
            {
                var messageService = scope.ServiceProvider.GetRequiredService<IMessageService>();
                messageService.Dispose();
            }

            await Host.StopAsync();
            Host.Dispose();
            base.OnExit(e);
        }
    }
}
