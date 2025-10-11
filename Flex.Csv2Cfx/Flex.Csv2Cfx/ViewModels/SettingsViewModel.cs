using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // Machine Metadata Settings
        private string _building = string.Empty;
        public string Building
        {
            get => _building;
            set => SetProperty(ref _building, value);
        }

        private string _device = string.Empty;
        public string Device
        {
            get => _device;
            set => SetProperty(ref _device, value);
        }

        private string _areaName = string.Empty;
        public string AreaName
        {
            get => _areaName;
            set => SetProperty(ref _areaName, value);
        }

        private string _organization = string.Empty;
        public string Organization
        {
            get => _organization;
            set => SetProperty(ref _organization, value);
        }

        private string _lineName = string.Empty;
        public string LineName
        {
            get => _lineName;
            set => SetProperty(ref _lineName, value);
        }

        private string _siteName = string.Empty;
        public string SiteName
        {
            get => _siteName;
            set => SetProperty(ref _siteName, value);
        }

        private string _stationName = string.Empty;
        public string StationName
        {
            get => _stationName;
            set => SetProperty(ref _stationName, value);
        }

        private string _processType = string.Empty;
        public string ProcessType
        {
            get => _processType;
            set => SetProperty(ref _processType, value);
        }

        private string _machineName = string.Empty;
        public string MachineName
        {
            get => _machineName;
            set => SetProperty(ref _machineName, value);
        }

        private string _createdBy = string.Empty;
        public string CreatedBy
        {
            get => _createdBy;
            set => SetProperty(ref _createdBy, value);
        }

        // MachineSettingsCsv
        private string _productionInformationFilePath = string.Empty;
        public string ProductionInformationFilePath
        {
            get => _productionInformationFilePath;
            set => SetProperty(ref _productionInformationFilePath, value);
        }

        private string _machineStatusInformationFilePath = string.Empty;
        public string MachineStatusInformationFilePath
        {
            get => _machineStatusInformationFilePath;
            set => SetProperty(ref _machineStatusInformationFilePath, value);
        }

        private string _processDataFilesFilePath = string.Empty;
        public string ProcessDataFilesFilePath
        {
            get => _processDataFilesFilePath;
            set => SetProperty(ref _processDataFilesFilePath, value);
        }

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
        public ICommand SelectProductionFileCommand { get; }
        public ICommand SelectMachineStatusFileCommand { get; }
        public ICommand SelectProcessDataFolderCommand { get; }

        public SettingsViewModel(IConfigurationService configurationService)
        {
            _configurationService = configurationService;

            SaveCommand = new RelayCommand(ExecuteSave);
            CancelCommand = new RelayCommand(ExecuteCancel);
            SelectProductionFileCommand = new RelayCommand(ExecuteSelectProductionFile);
            SelectMachineStatusFileCommand = new RelayCommand(ExecuteSelectMachineStatusFile);
            SelectProcessDataFolderCommand = new RelayCommand(ExecuteSelectProcessDataFolder);

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

            // 加载 Machine Metadata
            Building = settings.MachineSettings.Metadata.Building ?? string.Empty;
            Device = settings.MachineSettings.Metadata.Device ?? string.Empty;
            AreaName = settings.MachineSettings.Metadata.AreaName ?? string.Empty;
            Organization = settings.MachineSettings.Metadata.Organization ?? string.Empty;
            LineName = settings.MachineSettings.Metadata.LineName ?? string.Empty;
            SiteName = settings.MachineSettings.Metadata.SiteName ?? string.Empty;
            StationName = settings.MachineSettings.Metadata.StationName ?? string.Empty;
            ProcessType = settings.MachineSettings.Metadata.ProcessType ?? string.Empty;
            MachineName = settings.MachineSettings.Metadata.MachineName ?? string.Empty;
            CreatedBy = settings.MachineSettings.Metadata.CreatedBy ?? string.Empty;

            // 加载 MachineSettingsCsv
            ProductionInformationFilePath = settings.MachineSettings.Csv.ProductionInformationFilePath ?? string.Empty;
            MachineStatusInformationFilePath = settings.MachineSettings.Csv.MachineStatusInformationFilePath ?? string.Empty;
            ProcessDataFilesFilePath = settings.MachineSettings.Csv.ProcessDataFilesFilePath ?? string.Empty;

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
                    },
                    MachineSettings = new MachineSettings
                    {
                        Metadata = new MachineSettingsMetadata
                        {
                            Building = Building,
                            Device = Device,
                            AreaName = AreaName,
                            Organization = Organization,
                            LineName = LineName,
                            SiteName = SiteName,
                            StationName = StationName,
                            ProcessType = ProcessType,
                            MachineName = MachineName,
                            CreatedBy = CreatedBy
                        },
                        Csv = new MachineSettingsCsv
                        {
                            ProductionInformationFilePath = ProductionInformationFilePath,
                            MachineStatusInformationFilePath = MachineStatusInformationFilePath,
                            ProcessDataFilesFilePath = ProcessDataFilesFilePath
                        }
                    },
                    PreferredProtocol = SelectedProtocol
                    // 注意：这里不设置 LoginApiUrl，因为我们要保留它
                };

                // 使用 preserveLoginApiUrl = true 保存，这样不会覆盖 Login API URL
                await _configurationService.SaveSettingsAsync(settings, preserveLoginApiUrl: true);

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

        private void ExecuteSelectProductionFile(object? obj)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择生产信息文件",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                ProductionInformationFilePath = dialog.FileName;
            }
        }

        private void ExecuteSelectMachineStatusFile(object? obj)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择机器状态信息文件",
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                MachineStatusInformationFilePath = dialog.FileName;
            }
        }

        private void ExecuteSelectProcessDataFolder(object? obj)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择过程数据文件夹",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                ProcessDataFilesFilePath = dialog.FolderName;
            }
        }
    }
}