using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
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
        private readonly ICfxAmqpService _cfxAmqpService;
        private readonly ILogger<MainViewModel> _logger;

        private string _connectionStatus = "未连接";
        private ObservableCollection<Message> _sentMessages = new();
        private int _successCount = 0;
        private int _failCount = 0;
        private bool _isServiceRunning = false;
        private CancellationTokenSource? _serviceCancellationTokenSource;
        private Timer? _heartbeatTimer;
        // ✅ JSON 序列化选项 - 防止特殊字符被编码
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = null
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
                var oldValue = _selectedProtocol;

                if (IsServiceRunning && oldValue != value)
                {
                    MessageBox.Show(
                        "无法在服务运行时切换协议。\n请先停止服务，然后再切换协议。",
                        "提示",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    OnPropertyChanged(nameof(SelectedProtocol));
                    return;
                }

                if (SetProperty(ref _selectedProtocol, value))
                {
                    _messageService.SetProtocol(value);
                    OnPropertyChanged(nameof(ProtocolDisplayText));
                    _logger.LogInformation("协议已切换为: {Protocol}", value);
                    AddSystemMessage("系统", $"协议已切换为: {value}");
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
            ICfxAmqpService cfxAmqpService,
            ILogger<MainViewModel> logger)
        {
            _pageService = pageService;
            _authService = authService;
            _messageService = messageService;
            _machineService = machineService;
            _configurationService = configurationService;
            _cfxAmqpService = cfxAmqpService;
            _logger = logger;

            MenuItems = new ObservableCollection<NavigationItem>(_pageService.GetNavigationItems(CurrentUser));

            NavigateCommand = new RelayCommand(ExecuteNavigate);
            ClearMessagesCommand = new RelayCommand(ExecuteClearMessages);
            ReconnectCommand = new RelayCommand(async _ => await ExecuteReconnectAsync());
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);
            ToggleServiceCommand = new RelayCommand(async _ => await ExecuteToggleServiceAsync());

            var settings = _configurationService.GetSettings();
            _selectedProtocol = settings.PreferredProtocol;
            _messageService.SetProtocol(_selectedProtocol);

            _ = InitializeMessageServiceAsync();
        }

        private async Task InitializeMessageServiceAsync()
        {
            ConnectionStatus = "连接中...";

            try
            {
                bool connected = false;

                if (SelectedProtocol == MessageProtocol.AMQP)
                {
                    // AMQP 模式：使用 CfxAmqpService
                    var (success, message) = await _cfxAmqpService.OpenEndpointTopicAsync();
                    connected = success;

                    if (connected)
                    {
                        _logger.LogInformation("AMQP 服务连接成功");
                    }
                    else
                    {
                        _logger.LogWarning("AMQP 服务连接失败: {Message}", message);
                    }
                }
                else if (SelectedProtocol == MessageProtocol.MQTT)
                {
                    // MQTT 模式：使用 MessageService
                    connected = await _messageService.ConnectAsync();

                    if (connected)
                    {
                        _logger.LogInformation("MQTT 服务连接成功");
                    }
                    else
                    {
                        _logger.LogWarning("MQTT 服务连接失败");
                    }
                }

                ConnectionStatus = connected ? "已连接" : "连接失败";

                if (connected)
                {
                    await RefreshMessagesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化消息服务时发生错误");
                ConnectionStatus = "连接错误";
                AddSystemMessage("系统", $"初始化失败: {ex.Message}");
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
                // 根据协议类型进行不同的连接处理
                if (SelectedProtocol == MessageProtocol.AMQP)
                {
                    // AMQP 模式：使用 CfxAmqpService
                    if (!_cfxAmqpService.IsConnected)
                    {
                        AddSystemMessage("系统", "正在连接 AMQP 服务...");
                        var (success, message) = await _cfxAmqpService.OpenEndpointTopicAsync();

                        if (!success)
                        {
                            AddSystemMessage("系统", $"AMQP 连接失败: {message}");
                            MessageBox.Show(
                                $"AMQP 连接失败：\n{message}\n\n请检查 RabbitMQ 配置并重试。",
                                "错误",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            return;
                        }

                        AddSystemMessage("系统", "AMQP 连接成功");
                        ConnectionStatus = "已连接";
                    }
                }
                else if (SelectedProtocol == MessageProtocol.MQTT)
                {
                    // MQTT 模式：使用 MessageService
                    if (!_messageService.IsConnected)
                    {
                        AddSystemMessage("系统", "正在连接 MQTT 服务...");
                        var connected = await _messageService.ConnectAsync();

                        if (!connected)
                        {
                            AddSystemMessage("系统", "MQTT 连接失败");
                            MessageBox.Show(
                                "MQTT 连接失败，无法启动服务。\n请检查配置并重试。",
                                "错误",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            return;
                        }

                        AddSystemMessage("系统", "MQTT 连接成功");
                        ConnectionStatus = "已连接";
                    }
                }

                _serviceCancellationTokenSource = new CancellationTokenSource();
                IsServiceRunning = true;

                AddSystemMessage("系统", $"服务启动成功 (协议: {SelectedProtocol})");

                var settings = _configurationService.GetSettings();
                var heartbeatFrequency = settings.MachineSettings.Cfx.HeartbeatFrequency;

                // 启动心跳定时器 - 使用同步回调
                _heartbeatTimer = new Timer(
                    callback: state =>
                    {
                        _ = SendHeartbeatAsync();
                    },
                    state: null,
                    dueTime: TimeSpan.Zero,
                    period: TimeSpan.FromSeconds(heartbeatFrequency));

                // 启动后台任务
                _ = Task.Run(async () => await ProcessWorkflowAsync(_serviceCancellationTokenSource.Token));
                _ = Task.Run(async () => await ProcessMachineStateAsync(_serviceCancellationTokenSource.Token));

                _logger.LogInformation("服务已启动 - 协议: {Protocol}, 心跳频率: {Frequency}秒",
                    SelectedProtocol, heartbeatFrequency);
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

                // 先停止定时器
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _heartbeatTimer.Dispose();
                    _heartbeatTimer = null;
                }

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

        /// <summary>
        /// 发送心跳消息 - 根据协议类型使用不同的实现
        /// </summary>
        private async Task SendHeartbeatAsync()
        {
            if (!IsServiceRunning)
                return;

            try
            {
                var settings = _configurationService.GetSettings();

                if (SelectedProtocol == MessageProtocol.AMQP)
                {
                    // AMQP 模式：使用 CfxAmqpService 直接发送 CFX 格式的心跳
                    var heartbeatFrequency = settings.MachineSettings.Cfx.HeartbeatFrequency;
                    var heartbeatTimeSpan = $"00:00:{heartbeatFrequency:D2}"; // "00:00:05"

                    var result = await _cfxAmqpService.SendHeartbeatAsync(
                        activeFaults: Array.Empty<string>(),
                        activeRecipes: Array.Empty<string>(),
                        heartbeatFrequency: heartbeatTimeSpan
                    );

                    if (result.success)
                    {
                        SuccessCount++;
                        _logger.LogDebug("心跳消息发送成功 (CFX AMQP)");

                        // 添加到消息列表 - 显示简化的内容
                        AddAmqpMessageToList("Heartbeat", "心跳消息", "成功");
                    }
                    else
                    {
                        FailCount++;
                        _logger.LogWarning("心跳消息发送失败: {Json}", result.json);

                        // 添加到消息列表
                        AddAmqpMessageToList("Heartbeat", result.json, "失败");

                        // 如果连接断开，尝试重连
                        if (!_cfxAmqpService.IsConnected)
                        {
                            _logger.LogWarning("检测到 AMQP 断开，尝试重新连接...");
                            ConnectionStatus = "重新连接中...";
                            var (success, message) = await _cfxAmqpService.OpenEndpointTopicAsync();
                            ConnectionStatus = success ? "已连接" : "连接失败";
                        }
                    }
                }
                else if (SelectedProtocol == MessageProtocol.MQTT)
                {
                    // MQTT 模式：使用 MachineService 生成 JSON 然后通过 MessageService 发送
                    var heartbeat = _machineService.GetHeartbeat();
                    var message = JsonSerializer.Serialize(heartbeat, _jsonOptions);
                    var topic = "ipc-cfx";

                    var result = await _messageService.PublishMessageAsync(topic, message);

                    if (result.Success)
                    {
                        SuccessCount++;
                        _logger.LogDebug("心跳消息发送成功 (MQTT)");
                    }
                    else
                    {
                        FailCount++;
                        _logger.LogWarning("心跳消息发送失败: {Message}", result.Message);

                        // 如果 MQTT 连接断开，尝试重连
                        if (!_messageService.IsConnected)
                        {
                            _logger.LogWarning("检测到 MQTT 断开，尝试重新连接...");
                            ConnectionStatus = "重新连接中...";
                            var connected = await _messageService.ConnectAsync();
                            ConnectionStatus = connected ? "已连接" : "连接失败";
                        }
                    }

                    // ✅ MQTT 模式下需要刷新消息列表
                    await RefreshMessagesAsync();
                }

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
            _logger.LogInformation("工作流程处理任务已启动");

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

                            var message = JsonSerializer.Serialize(process, _jsonOptions);
                            var topic = "ipc-cfx";

                            // 根据协议发送
                            if (SelectedProtocol == MessageProtocol.AMQP)
                            {
                                // AMQP: 使用 CfxAmqpService 发送通用 JSON
                                var result = await _cfxAmqpService.SendCommonJsonDataAsync(message);

                                if (result.success)
                                {
                                    SuccessCount++;
                                    // 显示消息类型而不是完整 JSON
                                    var msgName = process.ContainsKey("MessageName") ? process["MessageName"]?.ToString() ?? "Unknown" : "WorkProcess";
                                    AddAmqpMessageToList(topic, $"{msgName} 消息已发送", "成功");
                                }
                                else
                                {
                                    FailCount++;
                                    AddAmqpMessageToList(topic, $"发送失败: {result.json}", "失败");
                                    _logger.LogWarning("工作流程消息发送失败: {Json}", result.json);
                                }
                            }
                            else if (SelectedProtocol == MessageProtocol.MQTT)
                            {
                                // MQTT: 使用 MessageService
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
                            }

                            OnPropertyChanged(nameof(StatsText));
                        }

                        // 只在 MQTT 模式下刷新消息列表
                        if (SelectedProtocol == MessageProtocol.MQTT)
                        {
                            await RefreshMessagesAsync();
                        }
                    }

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
            _logger.LogInformation("机器状态处理任务已启动");

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

                            var message = JsonSerializer.Serialize(state, _jsonOptions);
                            var topic = "ipc-cfx";

                            // 根据协议发送
                            if (SelectedProtocol == MessageProtocol.AMQP)
                            {
                                // AMQP: 使用 CfxAmqpService 发送通用 JSON
                                var result = await _cfxAmqpService.SendCommonJsonDataAsync(message);

                                if (result.success)
                                {
                                    SuccessCount++;
                                    var msgName = state.ContainsKey("MessageName") ? state["MessageName"]?.ToString() ?? "Unknown" : "MachineState";
                                    AddAmqpMessageToList(topic, $"{msgName} 消息已发送", "成功");
                                }
                                else
                                {
                                    FailCount++;
                                    AddAmqpMessageToList(topic, $"发送失败: {result.json}", "失败");
                                    _logger.LogWarning("机器状态消息发送失败: {Json}", result.json);
                                }
                            }
                            else if (SelectedProtocol == MessageProtocol.MQTT)
                            {
                                // MQTT: 使用 MessageService
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
                            }

                            OnPropertyChanged(nameof(StatsText));
                        }

                        // 只在 MQTT 模式下刷新消息列表
                        if (SelectedProtocol == MessageProtocol.MQTT)
                        {
                            await RefreshMessagesAsync();
                        }
                    }

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

            if (SelectedProtocol == MessageProtocol.AMQP)
            {
                _cfxAmqpService.CloseEndpoint();
            }
            else if (SelectedProtocol == MessageProtocol.MQTT)
            {
                _messageService.Disconnect();
            }

            await InitializeMessageServiceAsync();
        }

        /// <summary>
        /// 刷新消息列表
        /// </summary>
        private async Task RefreshMessagesAsync()
        {
            try
            {
                if (SelectedProtocol == MessageProtocol.MQTT)
                {
                    // MQTT 模式：从 MessageService 获取消息列表
                    var messages = await _messageService.GetRecentSentMessagesAsync(100);

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        SentMessages.Clear();
                        foreach (var msg in messages)
                        {
                            SentMessages.Add(msg);
                        }

                        _logger.LogDebug("MQTT 消息列表已刷新，共 {Count} 条消息", messages.Count);
                    });
                }
                // AMQP 模式不需要刷新，因为消息已经实时添加到 SentMessages
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新消息列表时发生错误");
            }
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

        /// <summary>
        /// 添加 AMQP 消息到列表（因为 CfxAmqpService 不使用 MessageService 的消息列表）
        /// </summary>
        private void AddAmqpMessageToList(string topic, string content, string status)
        {
            var message = new Message
            {
                Protocol = "AMQP",
                Topic = topic,
                Content = content,
                Timestamp = DateTime.Now,
                Status = status
            };

            App.Current.Dispatcher.Invoke(() =>
            {
                SentMessages.Insert(0, message);

                // 限制列表大小
                if (SentMessages.Count > 1000)
                {
                    SentMessages.RemoveAt(SentMessages.Count - 1);
                }
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