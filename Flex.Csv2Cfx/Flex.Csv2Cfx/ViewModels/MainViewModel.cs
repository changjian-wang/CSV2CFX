using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using Flex.Csv2Cfx.Views;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Flex.Csv2Cfx.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private readonly IPageService _pageService;
        private readonly IAuthService _authService;
        private readonly IMessageService _messageService;
        private string _connectionStatus = "未连接";
        private ObservableCollection<Message> _sentMessages = new();
        private string topic = "flex/heartbeat";
        private string _messageContent = "测试消息内容：Hello, World!";
        private int _successCount = 0;
        private int _failCount = 0;

        public ObservableCollection<NavigationItem> MenuItems { get; }
        public ObservableCollection<Message> SentMessages
        {
            get => _sentMessages;
            set => SetProperty(ref _sentMessages, value);
        }

        public User CurrentUser => _authService.CurrentUser ?? new User();
        private MessageProtocol _selectedProtocol;
        public string ProtocolDisplayText => $"当前协议: {SelectedProtocol}";
        public IEnumerable<MessageProtocol> AvailableProtocols => Enum.GetValues<MessageProtocol>();

        public MessageProtocol SelectedProtocol
        {
            get => _selectedProtocol;
            set
            {
                if (SetProperty(ref _selectedProtocol, value))
                {
                    _messageService.SetProtocol(value);
                    OnPropertyChanged(nameof(ProtocolDisplayText));
                }
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public string MessageContent
        {
            get => _messageContent;
            set => SetProperty(ref _messageContent, value);
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

        public string StatsText => $"成功: {SuccessCount} 失败: {FailCount}";

        public ICommand? NavigateCommand { get; }
        public ICommand? SendAmqpCommand { get; }
        public ICommand? SendMqttCommand { get; }
        public ICommand? SendBothCommand { get; }
        public ICommand? SendMessageCommand { get; }
        public ICommand? ReconnectCommand { get; }
        public ICommand? ClearMessagesCommand { get; }
        public ICommand OpenSettingsCommand { get; }


        public MainViewModel(IPageService pageService, IAuthService authService, IMessageService messageService)
        {
            _pageService = pageService;
            _authService = authService;
            _messageService = messageService;

            MenuItems = new ObservableCollection<NavigationItem>(_pageService.GetNavigationItems(CurrentUser));

            NavigateCommand = new RelayCommand(ExecuteNavigate);
            ClearMessagesCommand = new RelayCommand(ExecuteClearMessages);
            SendAmqpCommand = new RelayCommand(async _ => await ExecuteSendAmqpAsync());
            SendMqttCommand = new RelayCommand(async _ => await ExecuteSendMqttAsync());
            SendBothCommand = new RelayCommand(async _ => await ExecuteSendBothAsync());
            SendMessageCommand = new RelayCommand(async _ => await ExecuteSendMessageAsync());
            ReconnectCommand = new RelayCommand(async _ => await ExecuteReconnectAsync());
            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);

            // 初始化消息服务连接
            _ = InitializeMessageServiceAsync();
        }

        private async Task InitializeMessageServiceAsync()
        {
            ConnectionStatus = "连接中...";
            var connected = await _messageService.ConnectAsync();

            if (connected)
            {
                ConnectionStatus = "已连接";
                await LoadSentMessagesAsync();
            }
            else
            {
                ConnectionStatus = "连接失败";
            }
        }

        private void ExecuteOpenSettings(object? parameter)
        {
            var settingsWindow = App.GetService<SettingsWindow>();
            settingsWindow.ShowDialog();
        }

        private void UpdateStats(Message message)
        {
            if (message.Status == "成功")
                SuccessCount++;
            else if (message.Status.StartsWith("失败"))
                FailCount++;

            OnPropertyChanged(nameof(StatsText));
        }

        private async Task LoadSentMessagesAsync()
        {
            var messages = await _messageService.GetRecentSentMessagesAsync(50);
            foreach (var msg in messages)
            {
                SentMessages.Add(msg);
                UpdateStats(msg);
            }
        }

        private void ExecuteNavigate(object? parameter)
        {
            if (parameter is NavigationItem item)
            {
                System.Diagnostics.Debug.WriteLine($"导航到: {item.Content}");
            }
        }

        // 新增统一发送方法
        private async Task ExecuteSendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(MessageContent))
                return;

            var result = await _messageService.PublishMessageAsync(topic, MessageContent);

            if (result.Success)
            {
                SuccessCount++;
                System.Diagnostics.Debug.WriteLine($"消息发送成功: {result.Message}");
            }
            else
            {
                FailCount++;
                System.Diagnostics.Debug.WriteLine($"消息发送失败: {result.Message}");
            }

            await RefreshMessagesAsync();
            OnPropertyChanged(nameof(StatsText));
        }

        private async Task ExecuteSendAmqpAsync()
        {
            if (string.IsNullOrWhiteSpace(MessageContent))
                return;

            var result = await _messageService.PublishAmqpMessageAsync(
                topic,
                MessageContent);

            if (result.Success)
            {
                SuccessCount++;
                System.Diagnostics.Debug.WriteLine("AMQP消息发送成功");
            }
            else
            {
                FailCount++;
                System.Diagnostics.Debug.WriteLine($"AMQP消息发送失败: {result.Message}");
            }

            await RefreshMessagesAsync();
            OnPropertyChanged(nameof(StatsText));
        }

        private async Task ExecuteSendMqttAsync()
        {
            if (string.IsNullOrWhiteSpace(MessageContent))
                return;

            var result = await _messageService.PublishMqttMessageAsync(
                topic,
                MessageContent);

            if (result.Success)
            {
                SuccessCount++;
                System.Diagnostics.Debug.WriteLine("MQTT消息发送成功");
            }
            else
            {
                FailCount++;
                System.Diagnostics.Debug.WriteLine($"MQTT消息发送失败: {result.Message}");
            }

            await RefreshMessagesAsync();
            OnPropertyChanged(nameof(StatsText));
        }

        private async Task ExecuteSendBothAsync()
        {
            if (string.IsNullOrWhiteSpace(MessageContent))
                return;

            var result = await _messageService.PublishBothAsync(
                topic,
                MessageContent);

            if (result.Success)
            {
                SuccessCount++;
                System.Diagnostics.Debug.WriteLine("双协议消息发送成功");
            }
            else
            {
                FailCount++;
                System.Diagnostics.Debug.WriteLine($"消息发送结果: {result.Message}");
            }

            await RefreshMessagesAsync();
            OnPropertyChanged(nameof(StatsText));
        }

        private async Task ExecuteReconnectAsync()
        {
            ConnectionStatus = "重新连接中...";
            _messageService.Disconnect();
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

        private async Task StartServiceAsync()
        {
            
        }
    }
}
