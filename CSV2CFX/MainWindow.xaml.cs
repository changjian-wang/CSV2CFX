using AutoUpdaterDotNET;
using CSV2CFX.AppSettings;
using CSV2CFX.Interfaces;
using CSV2CFX.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace CSV2CFX
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isServiceRunning = false;
        private readonly ILogger<MainWindow> _logger;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isManualCheck = false;
        private readonly IMachineService _machineService;
        private readonly IRabbitMQService _rabbitMQService;
        private readonly IHttpClientFactory _httpClientFactory;
        private Task? _backgroundTask;
        private readonly IOptionsMonitor<MachineInfoSetting> _machineInfoOptions;
        private readonly IOptionsMonitor<ApiSetting> _apiOptions;
        private readonly IConfiguration _configuration;
        private readonly List<IDisposable> _optionsChangeTokens = new();

        // Login相关字段
        private bool isLoggedIn = false;
        private string currentUser = ""; // 当前登录用户
        private string currentRole = ""; // 当前用户角色

        public MainWindow(
            ILogger<MainWindow> logger,
            IMachineService machineService,
            IRabbitMQService rabbitMQService,
            IHttpClientFactory httpClientFactory,
            IOptionsMonitor<MachineInfoSetting> machineInfoOptions,
            IOptionsMonitor<ApiSetting> apiOptions,
            IConfiguration configuration)
        {
            _logger = logger;
            _machineService = machineService;
            _rabbitMQService = rabbitMQService;
            _httpClientFactory = httpClientFactory;
            _machineInfoOptions = machineInfoOptions;
            _apiOptions = apiOptions;
            _configuration = configuration;

            InitializeComponent();

            UpdateUIForLoginState();
            UpdateVersionDisplay();
            ConfigureAutoUpdater();
            _cancellationTokenSource = new CancellationTokenSource();
            SetupConfigurationChangeHandlers();
            StartDailyUpdateCheck();

            Task.Delay(3000).ContinueWith(_ =>
            {
                if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Dispatcher.Invoke(() => CheckForUpdatesAsync(false));
                }
            });
        }

        private void SetupConfigurationChangeHandlers()
        {
            _optionsChangeTokens.Add(
                _machineInfoOptions.OnChange(OnMachineInfoChanged)
            );

            _optionsChangeTokens.Add(
                _apiOptions.OnChange(OnApiSettingChanged)
            );
        }

        private void OnMachineInfoChanged(MachineInfoSetting newValue)
        {
            _logger?.LogInformation("MachineInfo 配置已更新: UniqueId={UniqueId}, Version={Version}, HeartbeatFrequency={HeartbeatFrequency}",
                newValue.UniqueId, newValue.Version, newValue.HeartbeatFrequency);

            if (isServiceRunning)
            {
                Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show(
                        "机器信息配置已更改，建议重启服务以应用新配置。\n\n是否立即重启服务？",
                        "配置更改通知",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        RestartService();
                    }
                });
            }
        }

        private void OnApiSettingChanged(ApiSetting newValue)
        {
            _logger?.LogInformation("API 配置已更新: Endpoint={Endpoint}, LoginUri={LoginUri}",
                newValue.Endpoint, newValue.LoginUri);
        }

        private void RestartService()
        {
            try
            {
                _logger?.LogInformation("正在重启服务以应用新配置...");

                ServiceStatusText.Text = "服务状态：正在重启...";
                ServiceStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                StartServiceButton.IsEnabled = false;
                StopBackgroundProcessing();

                Task.Delay(1000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            StartBackgroundProcessing();
                            ServiceStatusText.Text = "服务状态：运行中";
                            ServiceStatusText.Foreground = System.Windows.Media.Brushes.Green;
                            StartServiceButton.Content = "⏹️ 停止服务";
                            StartServiceButton.IsEnabled = true;

                            _logger?.LogInformation("服务已成功重启");
                            MessageBox.Show("服务已成功重启，新配置已生效。", "重启完成",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "重启服务时发生错误");
                            ServiceStatusText.Text = "服务状态：已停止";
                            ServiceStatusText.Foreground = System.Windows.Media.Brushes.Red;
                            StartServiceButton.Content = "▶️ 启动服务";
                            StartServiceButton.IsEnabled = true;
                            isServiceRunning = false;

                            MessageBox.Show($"重启服务时发生错误：{ex.Message}", "重启失败",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "重启服务时发生错误");
                MessageBox.Show($"重启服务时发生错误：{ex.Message}", "重启失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                ServiceStatusText.Text = "服务状态：已停止";
                ServiceStatusText.Foreground = System.Windows.Media.Brushes.Red;
                StartServiceButton.Content = "▶️ 启动服务";
                StartServiceButton.IsEnabled = true;
                isServiceRunning = false;
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var client = _httpClientFactory.CreateClient("FlexApiClient");
            var loginWindow = new LoginWindow(client, _apiOptions);
            loginWindow.Owner = this;

            if (loginWindow.ShowDialog() == true)
            {
                isLoggedIn = true;
                currentUser = GetCurrentUserFromLogin(loginWindow);
                currentRole = GetCurrentRoleFromLogin(loginWindow);

                _logger?.LogInformation($"用户 {currentUser} ({currentRole}) 登录成功");
                UpdateUIForLoginState();
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("确定要退出登录吗？\n\n注意：退出登录将停止所有正在运行的服务。",
                "确认退出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _logger?.LogInformation($"用户 {currentUser} 退出登录");
                if (isServiceRunning)
                {
                    StopBackgroundProcessing();
                    isServiceRunning = false;
                }
                isLoggedIn = false;
                currentUser = "";
                currentRole = "";
                UpdateUIForLoginState();
            }
        }

        private void UpdateUIForLoginState()
        {
            if (isLoggedIn)
            {
                MainContentPanel.Visibility = Visibility.Visible;
                LoginPromptPanel.Visibility = Visibility.Collapsed;
                UserInfoText.Text = $"{currentUser} ({currentRole})";
                UserInfoText.Visibility = Visibility.Visible;
                LoginButton.Visibility = Visibility.Collapsed;
                LogoutButton.Visibility = Visibility.Visible;
                WelcomeText.Text = $"欢迎回来，{currentUser}！您当前的角色是：{currentRole}";
                UpdateUIForRole();
            }
            else
            {
                MainContentPanel.Visibility = Visibility.Collapsed;
                LoginPromptPanel.Visibility = Visibility.Visible;
                UserInfoText.Visibility = Visibility.Collapsed;
                LoginButton.Visibility = Visibility.Visible;
                LogoutButton.Visibility = Visibility.Collapsed;
                StartServiceButton.Content = "▶️ 启动服务";
                ServiceStatusText.Text = "服务状态：已停止";
                ServiceStatusText.Foreground = System.Windows.Media.Brushes.Red;
                ServiceLogText.Text = "";
            }
        }

        private void UpdateUIForRole()
        {
            // 这里根据角色调整界面功能
        }

        private string GetCurrentUserFromLogin(LoginWindow loginWindow)
        {
            return loginWindow?.LoggedInUser ?? string.Empty;
        }

        private string GetCurrentRoleFromLogin(LoginWindow loginWindow)
        {
            return loginWindow?.LoggedInRole ?? string.Empty;
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isLoggedIn)
            {
                MessageBox.Show("请先登录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var configWindow = new Window
                {
                    Title = $"系统配置 - {currentUser}",
                    Width = 1200,
                    Height = 800,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.CanResize,
                    Icon = this.Icon
                };

                // 构造 ConfigurationPage 时不要传 dynamicConfigService
                var configPage = new ConfigurationPage(
                    _machineInfoOptions,
                    _apiOptions,
                    _configuration
                );
                configWindow.Content = configPage;

                configWindow.Closed += (s, args) =>
                {
                    _logger?.LogInformation($"用户 {currentUser} 关闭了配置页面");
                };

                configWindow.ShowDialog();
                _logger?.LogInformation($"用户 {currentUser} 打开了配置页面");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "打开配置页面时发生错误");
                MessageBox.Show($"打开配置页面时发生错误：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ViewLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isLoggedIn)
            {
                MessageBox.Show("请先登录！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("日志查看功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void StartBackgroundProcessing()
        {
            _logger?.LogInformation("开始启动后台处理服务...");

            // 重新创建取消令牌
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            _backgroundTask = Task.Run(async () =>
            {
                var currentMachineInfo = _machineInfoOptions.CurrentValue;

                if (!string.IsNullOrWhiteSpace(currentMachineInfo.UniqueId))
                {
                    await _machineService.CreateRabbitmqAsync(uniqueId: currentMachineInfo.UniqueId).ConfigureAwait(false);
                }

                while (!_cancellationTokenSource!.Token.IsCancellationRequested)
                {
                    try
                    {
                        await RunAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogInformation("Background processing task was cancelled.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "An error occurred during background processing.");
                        await Task.Delay(1000, _cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
            }, _cancellationTokenSource!.Token);
        }

        private void StopBackgroundProcessing()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                try
                {
                    _backgroundTask?.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException ex)
                {
                    _logger?.LogWarning(ex, "等待后台任务完成时发生异常");
                }
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                var currentMachineInfo = _machineInfoOptions.CurrentValue;
                var uniqueId = currentMachineInfo.UniqueId;

                if (!string.IsNullOrWhiteSpace(uniqueId))
                {
                    var tasks = new Func<string, Task>[]
                    {
                    _machineService.PublishHeartbeatAsync,
                    _machineService.PublishWorkProcessAsync,
                    _machineService.PublishMachineStateAsync,
                    };

                    await Parallel.ForEachAsync(tasks, new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = tasks.Length
                    }, async (task, ct) =>
                    {
                        await task(uniqueId).ConfigureAwait(false);
                    });

                    var delayMs = Math.Max(currentMachineInfo.HeartbeatFrequency * 1000, 1000);
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("RunAsync operation was cancelled.");
                throw; // 重新抛出取消异常
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "An error occurred in RunAsync.");
                throw;
            }
        }

        private void UpdateVersionDisplay()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                System.Diagnostics.Debug.WriteLine($"当前版本: {version}");
                System.Diagnostics.Debug.WriteLine($"程序集位置: {assembly.Location}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取版本信息失败: {ex.Message}");
            }
        }

        private void ConfigureAutoUpdater()
        {
            AutoUpdater.UpdateMode = Mode.Normal;
            AutoUpdater.Mandatory = false;
            AutoUpdater.UpdateFormSize = new System.Drawing.Size(800, 600);
            AutoUpdater.AppTitle = "CSV2CFX Service";
            AutoUpdater.AppCastURL = "https://raw.githubusercontent.com/changjian-wang/lc-materials/refs/heads/main/updates.xml";
            AutoUpdater.RunUpdateAsAdmin = false;
            AutoUpdater.ReportErrors = false;

            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.ApplicationExitEvent += AutoUpdater_ApplicationExitEvent;
        }

        private void StartDailyUpdateCheck()
        {
            Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var now = DateTime.UtcNow;
                        var nextMidnight = now.Date.AddDays(1);
                        var delay = nextMidnight - now;

                        System.Diagnostics.Debug.WriteLine($"当前UTC时间: {now:yyyy-MM-dd HH:mm:ss}");
                        System.Diagnostics.Debug.WriteLine($"下次更新检查: {nextMidnight:yyyy-MM-dd HH:mm:ss} UTC");

                        await Task.Delay(delay, _cancellationTokenSource.Token).ConfigureAwait(false);

                        if (!_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                System.Diagnostics.Debug.WriteLine($"执行每日自动更新检查: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                                CheckForUpdatesAsync(false);
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"每日更新检查异常: {ex.Message}");
                        try
                        {
                            await Task.Delay(TimeSpan.FromHours(1), _cancellationTokenSource.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args?.Error != null)
            {
                if (_isManualCheck)
                {
                    MessageBox.Show($"检查更新时发生错误:\n{args.Error.Message}",
                                  "更新检查错误",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                }
                System.Diagnostics.Debug.WriteLine($"更新检查错误: {args.Error.Message}");
                _isManualCheck = false;
                return;
            }

            if (args?.IsUpdateAvailable == true)
            {
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                var message = $"发现新版本可用!\n\n" +
                            $"当前版本: {currentVersion}\n" +
                            $"最新版本: {args.CurrentVersion}\n\n" +
                            $"是否立即下载并安装更新？";

                var result = MessageBox.Show(message,
                                            "发现更新",
                                            MessageBoxButton.YesNo,
                                            MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (AutoUpdater.DownloadUpdate(args))
                        {
                            Application.Current.Shutdown();
                        }
                    }
                    catch (Exception exception)
                    {
                        MessageBox.Show($"下载更新时发生错误:\n{exception.Message}",
                                      "更新错误",
                                      MessageBoxButton.OK,
                                      MessageBoxImage.Error);
                    }
                }
            }
            else if (args != null)
            {
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                if (_isManualCheck)
                {
                    MessageBox.Show($"当前版本 {currentVersion} 已是最新版本，无需更新。",
                                  "检查更新",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
            }
            else
            {
                if (_isManualCheck)
                {
                    MessageBox.Show("无法连接到更新服务器，请检查网络连接后重试。",
                                  "更新检查失败",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Warning);
                }
            }

            _isManualCheck = false;
        }

        private void AutoUpdater_ApplicationExitEvent()
        {
            Application.Current.Shutdown();
        }

        private void CheckForUpdatesAsync(bool isManualCheck)
        {
            _isManualCheck = isManualCheck;
            AutoUpdater.ReportErrors = isManualCheck;
            UpdateVersionDisplay();
            AutoUpdater.Start();
        }

        private void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckForUpdatesAsync(true);
        }

        private void ManualUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("手动更新功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void StartServiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isServiceRunning)
            {
                StartBackgroundProcessing();
                StartServiceButton.Content = "⏹️ 停止服务";
                ServiceStatusText.Text = "服务状态：运行中";
                ServiceStatusText.Foreground = System.Windows.Media.Brushes.Green;
                isServiceRunning = true;
                _logger?.LogInformation("后台服务已启动");
            }
            else
            {
                StopBackgroundProcessing();
                StartServiceButton.Content = "▶️ 启动服务";
                ServiceStatusText.Text = "服务状态：已停止";
                ServiceStatusText.Foreground = System.Windows.Media.Brushes.Red;
                isServiceRunning = false;
                _logger?.LogInformation("后台服务已停止");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _logger?.LogInformation("主窗口正在关闭，清理资源...");
                _cancellationTokenSource?.Cancel();
                foreach (var token in _optionsChangeTokens)
                {
                    token?.Dispose();
                }
                _optionsChangeTokens.Clear();
                AutoUpdater.CheckForUpdateEvent -= AutoUpdaterOnCheckForUpdateEvent;
                AutoUpdater.ApplicationExitEvent -= AutoUpdater_ApplicationExitEvent;
                _cancellationTokenSource?.Dispose();
                _logger?.LogInformation("资源清理完成");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "清理资源时发生错误");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
    }
}