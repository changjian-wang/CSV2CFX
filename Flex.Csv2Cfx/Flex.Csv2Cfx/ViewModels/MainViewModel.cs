using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Flex.Csv2Cfx.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly IPageService _pageService;
        private readonly IAuthService _authService;
        private readonly IMessageService _messageService;
        private readonly IMachineService _machineService;
        private readonly IConfigurationService _configurationService;
        private readonly ILogger<MainViewModel> _logger;

        private string _connectionStatus = "未连接";
        private const string QUEUE_NAME = "ipc-cfx";
        private ObservableCollection<Message> _sentMessages = new();
        private int _successCount = 0;
        private int _failCount = 0;
        private bool _isServiceRunning = false;
        private CancellationTokenSource? _serviceCancellationTokenSource;
        private Timer? _heartbeatTimer;
        private readonly JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public ObservableCollection<NavigationItem> MenuItems { get; }
        public ObservableCollection<Message> SentMessages
        {
            get => _sentMessages;
            set => SetProperty(ref _sentMessages, value);
        }

        public User CurrentUser => _authService.CurrentUser ?? new User();

        private MessageProtocol _selectedProtocol;
        public MessageProtocol SelectedProtocol
        {
            get => _selectedProtocol;
            set
            {
                // 保存旧值
                var oldValue = _selectedProtocol;

                // 如果服务正在运行，阻止切换并提示用户
                if (IsServiceRunning && oldValue != value)
                {
                    // 显示提示对话框
                    MessageBox.Show(
                        "无法在服务运行时切换协议。\n请先停止服务，然后再切换协议。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    // 强制恢复到旧值，触发UI更新
                    OnPropertyChanged(nameof(SelectedProtocol));
                    return;
                }

                // 只有在值真正改变时才更新
                if (SetProperty(ref _selectedProtocol, value))
                {
                    _messageService.SetProtocol(value);
                    OnPropertyChanged(nameof(ProtocolDisplayText));
                    _logger.LogInformation("协议已切换为: {Protocol}", value);
                    AddSystemMessage("系统", $"协议已切换为: {value}");

                    // 自动重新连接以应用新协议
                    _ = ReconnectAfterProtocolChangeAsync();
                }
            }
        }

        public string ProtocolDisplayText => $"当前协议: {SelectedProtocol}";
        public IEnumerable<MessageProtocol> AvailableProtocols => Enum.GetValues<MessageProtocol>();

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public int SuccessCount
        {
            get => _successCount;
            set => SetProperty(ref _successCount, value);
        }

        public int FailCount
        {
            get => _failCount;
            set => SetProperty(ref _failCount, value);
        }

        public bool IsServiceRunning
        {
            get => _isServiceRunning;
            set
            {
                if (SetProperty(ref _isServiceRunning, value))
                {
                    OnPropertyChanged(nameof(ServiceStatusText));
                    OnPropertyChanged(nameof(StartButtonText));
                    OnPropertyChanged(nameof(CanChangeProtocol));
                }
            }
        }

        public bool CanChangeProtocol => !IsServiceRunning;

        public string ServiceStatusText => IsServiceRunning ? "服务运行中" : "服务已停止";
        public string StartButtonText => IsServiceRunning ? "停止服务" : "启动服务";
        public string StatsText => $"成功: {SuccessCount} 失败: {FailCount}";

        public ICommand NavigateCommand { get; }
        public ICommand ClearMessagesCommand { get; }
        public ICommand ReconnectCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ToggleServiceCommand { get; }

        public MainViewModel(
            IPageService pageService,
            IAuthService authService,
            IMessageService messageService,
            IMachineService machineService,
            IConfigurationService configurationService,
            ILogger<MainViewModel> logger)
        {
            _pageService = pageService;
            _authService = authService;
            _messageService = messageService;
            _machineService = machineService;
            _configurationService = configurationService;
            _logger = logger;

            MenuItems = new ObservableCollection<NavigationItem>(_pageService.GetNavigationItems(CurrentUser));

            NavigateCommand = new RelayCommand(ExecuteNavigate);
            ClearMessagesCommand = new RelayCommand(ExecuteClearMessages);
            ReconnectCommand = new RelayCommand(async _ => await ExecuteReconnectAsync());
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);
            ToggleServiceCommand = new RelayCommand(async _ => await ExecuteToggleServiceAsync());

            // 加载协议设置
            var settings = _configurationService.GetSettings();
            _selectedProtocol = settings.PreferredProtocol;
            _messageService.SetProtocol(_selectedProtocol);

            // 初始化消息服务连接
            _ = InitializeMessageServiceAsync();
        }

        private async Task InitializeMessageServiceAsync()
        {
            try
            {
                ConnectionStatus = "连接中...";
                AddSystemMessage("系统", $"正在使用 {SelectedProtocol} 协议连接...");

                var connected = await _messageService.ConnectAsync();

                if (connected)
                {
                    ConnectionStatus = $"已连接 ({SelectedProtocol})";
                    AddSystemMessage("系统", $"{SelectedProtocol} 协议连接成功");
                    await RefreshMessagesAsync();
                }
                else
                {
                    ConnectionStatus = "连接失败";
                    AddSystemMessage("系统", $"{SelectedProtocol} 协议连接失败");

                    MessageBox.Show(
                        $"使用 {SelectedProtocol} 协议连接失败。\n请检查配置或尝试切换协议。",
                        "连接失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化消息服务失败");
                ConnectionStatus = "连接错误";
                AddSystemMessage("系统", $"连接错误: {ex.Message}");
            }
        }

        /// <summary>
        /// 协议切换后自动重新连接
        /// </summary>
        private async Task ReconnectAfterProtocolChangeAsync()
        {
            try
            {
                AddSystemMessage("系统", $"协议已切换为 {SelectedProtocol}，正在重新连接...");

                // 断开当前连接
                _messageService.Disconnect();
                await Task.Delay(500); // 给一点时间让连接完全关闭

                // 使用新协议重新连接
                await InitializeMessageServiceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "协议切换后重新连接失败");
                AddSystemMessage("系统", $"重新连接失败: {ex.Message}");
            }
        }

        private void ExecuteNavigate(object? parameter)
        {
            if (parameter is NavigationItem item)
            {
                _logger.LogDebug($"导航到: {item.Content}");
            }
        }

        private async Task ExecuteToggleServiceAsync()
        {
            if (IsServiceRunning)
            {
                await StopServiceAsync();
            }
            else
            {
                await StartServiceAsync();
            }
        }

        private async Task StartServiceAsync()
        {
            try
            {
                if (!_messageService.IsConnected)
                {
                    AddSystemMessage("系统", "消息服务未连接，正在重新连接...");
                    var connected = await _messageService.ConnectAsync();
                    if (!connected)
                    {
                        AddSystemMessage("系统", "消息服务连接失败，无法启动服务");
                        MessageBox.Show(
                            $"使用 {SelectedProtocol} 协议连接失败，无法启动服务。\n请检查配置或尝试切换协议后重试。",
                            "错误",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }
                }

                _serviceCancellationTokenSource = new CancellationTokenSource();
                IsServiceRunning = true;

                AddSystemMessage("系统", $"服务启动成功 (协议: {SelectedProtocol})");

                // 获取配置并启动心跳定时器
                var settings = _configurationService.GetSettings();
                var heartbeatFrequency = settings.MachineSettings.Cfx.HeartbeatFrequency;

                _heartbeatTimer = new Timer(
                    async _ => await SendHeartbeatAsync(),
                    null,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(heartbeatFrequency));

                // 启动后台任务处理工作流程和机器状态
                _ = Task.Run(async () => await ProcessWorkflowAsync(_serviceCancellationTokenSource.Token));
                _ = Task.Run(async () => await ProcessMachineStateAsync(_serviceCancellationTokenSource.Token));

                _logger.LogInformation("服务已启动，协议: {Protocol}, 心跳频率: {Frequency}秒", SelectedProtocol, heartbeatFrequency);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动服务失败");
                AddSystemMessage("系统", $"启动服务失败: {ex.Message}");
                MessageBox.Show(
                    $"启动服务失败：\n{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                IsServiceRunning = false;
            }
        }

        private async Task StopServiceAsync()
        {
            try
            {
                _serviceCancellationTokenSource?.Cancel();
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;

                // 等待一小段时间让后台任务完成
                await Task.Delay(500);

                IsServiceRunning = false;
                AddSystemMessage("系统", "服务已停止");

                _logger.LogInformation("服务已停止");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止服务失败");
                AddSystemMessage("系统", $"停止服务失败: {ex.Message}");
            }
        }

        private async Task SendHeartbeatAsync()
        {
            try
            {
                var heartbeat = _machineService.GetHeartbeat();
                var message = JsonSerializer.Serialize(heartbeat, jsonSerializerOptions);
                var topic = QUEUE_NAME;

                var result = await _messageService.PublishMessageAsync(topic, message);

                if (result.Success)
                {
                    SuccessCount++;
                    _logger.LogDebug("心跳消息发送成功 (协议: {Protocol})", SelectedProtocol);
                }
                else
                {
                    FailCount++;
                    _logger.LogWarning("心跳消息发送失败 (协议: {Protocol}): {Message}", SelectedProtocol, result.Message);
                }

                await RefreshMessagesAsync();
                OnPropertyChanged(nameof(StatsText));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送心跳消息时发生错误");
                FailCount++;
                OnPropertyChanged(nameof(StatsText));
            }
        }

        private async Task ProcessWorkflowAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("工作流程处理任务已启动 (协议: {Protocol})", SelectedProtocol);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var workProcesses = await _machineService.GetWorkProcessesAsync();

                    if (workProcesses.Count > 0)
                    {
                        _logger.LogInformation("检测到 {Count} 条工作流程消息", workProcesses.Count);

                        foreach (var process in workProcesses)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            var message = JsonSerializer.Serialize(process, jsonSerializerOptions);
                            var topic = QUEUE_NAME;

                            var result = await _messageService.PublishMessageAsync(topic, message);

                            if (result.Success)
                            {
                                SuccessCount++;
                            }
                            else
                            {
                                FailCount++;
                                _logger.LogWarning("工作流程消息发送失败: {Message}", result.Message);
                            }

                            OnPropertyChanged(nameof(StatsText));
                        }

                        await RefreshMessagesAsync();
                    }

                    // 等待5秒再检查新文件
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("工作流程处理任务已取消");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理工作流程时发生错误");
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }
        }

        private async Task ProcessMachineStateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("机器状态处理任务已启动 (协议: {Protocol})", SelectedProtocol);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var machineStates = await _machineService.GetMachineStateAsync();

                    if (machineStates.Count > 0)
                    {
                        _logger.LogInformation("检测到 {Count} 条机器状态消息", machineStates.Count);

                        foreach (var state in machineStates)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            var message = JsonSerializer.Serialize(state, jsonSerializerOptions);
                            var topic = QUEUE_NAME;

                            var result = await _messageService.PublishMessageAsync(topic, message);

                            if (result.Success)
                            {
                                SuccessCount++;
                            }
                            else
                            {
                                FailCount++;
                                _logger.LogWarning("机器状态消息发送失败: {Message}", result.Message);
                            }

                            OnPropertyChanged(nameof(StatsText));
                        }

                        await RefreshMessagesAsync();
                    }

                    // 等待5秒再检查新文件
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("机器状态处理任务已取消");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理机器状态时发生错误");
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                }
            }
        }

        private async Task ExecuteReconnectAsync()
        {
            if (IsServiceRunning)
            {
                var result = MessageBox.Show(
                    "服务正在运行中。\n是否停止服务并重新连接？",
                    "确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await StopServiceAsync();
                }
                else
                {
                    return;
                }
            }

            ConnectionStatus = "重新连接中...";
            AddSystemMessage("系统", $"使用 {SelectedProtocol} 协议重新连接...");
            _messageService.Disconnect();
            await Task.Delay(500); // 给一点时间让连接完全关闭
            await InitializeMessageServiceAsync();
        }

        private async Task RefreshMessagesAsync()
        {
            var messages = await _messageService.GetRecentSentMessagesAsync();
            App.Current.Dispatcher.Invoke(() =>
            {
                SentMessages.Clear();
                foreach (var msg in messages)
                {
                    SentMessages.Add(msg);
                }
            });
        }

        private void ExecuteClearMessages(object? parameter = null)
        {
            SentMessages.Clear();
            SuccessCount = 0;
            FailCount = 0;
            OnPropertyChanged(nameof(StatsText));
        }

        private void AddSystemMessage(string type, string content)
        {
            var message = new Message
            {
                Protocol = "SYSTEM",
                Topic = type,
                Content = content,
                Timestamp = DateTime.Now,
                Status = "信息"
            };

            App.Current.Dispatcher.Invoke(() =>
            {
                SentMessages.Insert(0, message);
            });
        }

        private void ExecuteOpenSettings(object? parameter)
        {
            if (IsServiceRunning)
            {
                MessageBox.Show(
                    "服务正在运行中，无法打开配置。\n请先停止服务，然后再打开配置。",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var settingsWindow = App.GetService<Views.SettingsWindow>();
            settingsWindow?.ShowDialog();
        }

        public void Cleanup()
        {
            _serviceCancellationTokenSource?.Cancel();
            _heartbeatTimer?.Dispose();
            _serviceCancellationTokenSource?.Dispose();
        }
    }
}