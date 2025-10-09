using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Flex.Csv2Cfx.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly IConfigurationService _configurationService;

        private MessageProtocol _selectedProtocol;
        public MessageProtocol SelectedProtocol
        {
            get => _selectedProtocol;
            set => SetProperty(ref _selectedProtocol, value);
        }

        public IEnumerable<MessageProtocol> AvailableProtocols => Enum.GetValues<MessageProtocol>();

        // MQTT Settings
        public string MqttServer { get; set; } = string.Empty;
        public string MqttPort { get; set; } = string.Empty;
        public string MqttUsername { get; set; } = string.Empty;
        public string MqttPassword { get; set; } = string.Empty;
        public string MqttClientIdPrefix { get; set; } = string.Empty;

        // RabbitMQ Settings
        public string RabbitMqHostName { get; set; } = string.Empty;
        public string RabbitMqUsername { get; set; } = string.Empty;
        public string RabbitMqPassword { get; set; } = string.Empty;
        public string RabbitMqVirtualHost { get; set; } = string.Empty;

        private bool _showSuccessMessage;
        public bool ShowSuccessMessage
        {
            get => _showSuccessMessage;
            set
            {
                _showSuccessMessage = value;
                OnPropertyChanged(string.Empty);
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public SettingsViewModel(IConfigurationService configurationService)
        {
            _configurationService = configurationService;

            SaveCommand = new RelayCommand(ExecuteSave);
            CancelCommand = new RelayCommand(ExecuteCancel);

            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = _configurationService.GetSettings();

            // 加载 MQTT 配置
            MqttServer = settings.MqttSettings.Server;
            MqttPort = settings.MqttSettings.Port.ToString();
            MqttUsername = settings.MqttSettings.Username;
            MqttPassword = settings.MqttSettings.Password;
            MqttClientIdPrefix = settings.MqttSettings.ClientIdPrefix;

            // 加载 RabbitMQ 配置
            RabbitMqHostName = settings.RabbitMqSettings.HostName;
            RabbitMqUsername = settings.RabbitMqSettings.Username;
            RabbitMqPassword = settings.RabbitMqSettings.Password;
            RabbitMqVirtualHost = settings.RabbitMqSettings.VirtualHost;

            // 添加协议设置加载
            SelectedProtocol = settings.PreferredProtocol;

            OnPropertyChanged(string.Empty); // 通知所有属性更新
        }

        private async void ExecuteSave(object? parameter)
        {
            try
            {
                var settings = new AppSettings
                {
                    MqttSettings = new MqttSettings
                    {
                        Server = MqttServer,
                        Port = int.TryParse(MqttPort, out var port) ? port : 1883,
                        Username = MqttUsername,
                        Password = MqttPassword,
                        ClientIdPrefix = MqttClientIdPrefix
                    },
                    RabbitMqSettings = new RabbitMqSettings
                    {
                        HostName = RabbitMqHostName,
                        Username = RabbitMqUsername,
                        Password = RabbitMqPassword,
                        VirtualHost = RabbitMqVirtualHost
                    }
                };

                await _configurationService.SaveSettingsAsync(settings);

                ShowSuccessMessage = true;

                // 3秒后隐藏成功消息
                await Task.Delay(3000);
                ShowSuccessMessage = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteCancel(object? parameter)
        {
            if (parameter is Window window)
            {
                window.Close();
            }
        }
    }
}
