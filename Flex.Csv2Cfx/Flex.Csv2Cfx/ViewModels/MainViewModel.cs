using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
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
        private string _amqpTopic = "wpf.system.message";
        private string _mqttTopic = "wpf/system/message";
        private string _messageContent = "测试消息内容：Hello, World!";
        private int _successCount = 0;
        private int _failCount = 0;

        public ObservableCollection<NavigationItem> MenuItems { get; }
        public ObservableCollection<Message> SentMessages
        {
            get => _sentMessages;
            set => SetProperty(ref _sentMessages, value);
        }

        public User CurrentUser => _authService.CurrentUser;

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public string AmqpTopic
        {
            get => _amqpTopic;
            set => SetProperty(ref _amqpTopic, value);
        }

        public string MqttTopic
        {
            get => _mqttTopic;
            set => SetProperty(ref _mqttTopic, value);
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
        public ICommand? ReconnectCommand { get; }
        public ICommand? ClearMessagesCommand { get; }

        public MainViewModel(IPageService pageService, IAuthService authService, IMessageService messageService)
        {
            _pageService = pageService;
            _authService = authService;
            _messageService = messageService;

            MenuItems = new ObservableCollection<NavigationItem>(_pageService.GetNavigationItems(CurrentUser));

            NavigateCommand = new RelayCommand(ExecuteNavigate);
            ClearMessagesCommand = new RelayCommand(ExecuteClearMessages);
            SendAmqpCommand = new RelayCommand(async _ => await ExecuteSendAmqp());
            SendMqttCommand = new RelayCommand(async _ => await ExecuteSendMqtt());
            SendBothCommand = new RelayCommand(async _ => await ExecuteSendBoth());
            ReconnectCommand = new RelayCommand(async _ => await ExecuteReconnect());

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
                await LoadSentMessages();
            }
            else
            {
                ConnectionStatus = "连接失败";
            }
        }

        private void UpdateStats(Message message)
        {
            if (message.Status == "成功")
                SuccessCount++;
            else if (message.Status.StartsWith("失败"))
                FailCount++;

            OnPropertyChanged(nameof(StatsText));
        }

        private async Task LoadSentMessages()
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

        private async Task ExecuteSendAmqp()
        {
            if (string.IsNullOrWhiteSpace(MessageContent))
                return;

            var result = await _messageService.PublishAmqpMessageAsync(
                "wpf.system.exchange",
                AmqpTopic,
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

            await RefreshMessages();
            OnPropertyChanged(nameof(StatsText));
        }

        private async Task ExecuteSendMqtt()
        {
            if (string.IsNullOrWhiteSpace(MessageContent))
                return;

            var result = await _messageService.PublishMqttMessageAsync(
                MqttTopic,
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

            await RefreshMessages();
            OnPropertyChanged(nameof(StatsText));
        }

        private async Task ExecuteSendBoth()
        {
            if (string.IsNullOrWhiteSpace(MessageContent))
                return;

            var result = await _messageService.PublishBothAsync(
                AmqpTopic,
                MqttTopic,
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

            await RefreshMessages();
            OnPropertyChanged(nameof(StatsText));
        }

        private async Task ExecuteReconnect()
        {
            ConnectionStatus = "重新连接中...";
            _messageService.Disconnect();
            await InitializeMessageServiceAsync();
        }

        private async Task RefreshMessages()
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
    }
}
