using CSV2CFX.AppSettings;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CSV2CFX.ViewModels
{
    public class ConfigurationViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IOptionsMonitor<MachineInfoSetting> _machineInfoOptions;
        private readonly IOptionsMonitor<ApiSetting> _apiOptions;
        private readonly List<IDisposable> _optionsChangeTokens = new();

        // 所有属性都需要私有字段和属性通知
        private string _defaultLogLevel;
        private string _microsoftHostingLifetimeLogLevel;
        private string _hostName;
        private string _userName;
        private string _password;
        private int _port;
        private string _virtualHost;
        private bool _automaticRecoveryEnabled;
        private int _networkRecoveryIntervalSeconds;
        private string _prefix;
        private int _maxConcurrency;
        private int _delayBetweenBatchesMs;
        private string _productionInformationFilePath;
        private string _machineStatusInformationFilePath;
        private string _processDataFilesFilePath;
        private string _building;
        private string _device;
        private string _areaName;
        private string _organization;
        private string _lineName;
        private string _siteName;
        private string _stationName;
        private string _processType;
        private string _machineName;
        private string _createdBy;
        private string _uniqueId;
        private string _version;
        private int _heartbeatFrequency;
        private string _endpoint;
        private string _loginUri;

        // 所有属性都实现完整的 getter/setter 和 PropertyChanged
        public string DefaultLogLevel
        {
            get => _defaultLogLevel;
            set { _defaultLogLevel = value; OnPropertyChanged(); }
        }

        public string MicrosoftHostingLifetimeLogLevel
        {
            get => _microsoftHostingLifetimeLogLevel;
            set { _microsoftHostingLifetimeLogLevel = value; OnPropertyChanged(); }
        }

        public string HostName
        {
            get => _hostName;
            set { _hostName = value; OnPropertyChanged(); }
        }

        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }

        public string VirtualHost
        {
            get => _virtualHost;
            set { _virtualHost = value; OnPropertyChanged(); }
        }

        public bool AutomaticRecoveryEnabled
        {
            get => _automaticRecoveryEnabled;
            set { _automaticRecoveryEnabled = value; OnPropertyChanged(); }
        }

        public int NetworkRecoveryIntervalSeconds
        {
            get => _networkRecoveryIntervalSeconds;
            set { _networkRecoveryIntervalSeconds = value; OnPropertyChanged(); }
        }

        public string Prefix
        {
            get => _prefix;
            set { _prefix = value; OnPropertyChanged(); }
        }

        public int MaxConcurrency
        {
            get => _maxConcurrency;
            set { _maxConcurrency = value; OnPropertyChanged(); }
        }

        public int DelayBetweenBatchesMs
        {
            get => _delayBetweenBatchesMs;
            set { _delayBetweenBatchesMs = value; OnPropertyChanged(); }
        }

        // 重点：文件路径相关属性必须有完整的 PropertyChanged 实现
        public string ProductionInformationFilePath
        {
            get => _productionInformationFilePath;
            set
            {
                _productionInformationFilePath = value;
                OnPropertyChanged();
                System.Diagnostics.Debug.WriteLine($"ProductionInformationFilePath changed to: {value}");
            }
        }

        public string MachineStatusInformationFilePath
        {
            get => _machineStatusInformationFilePath;
            set
            {
                _machineStatusInformationFilePath = value;
                OnPropertyChanged();
                System.Diagnostics.Debug.WriteLine($"MachineStatusInformationFilePath changed to: {value}");
            }
        }

        public string ProcessDataFilesFilePath
        {
            get => _processDataFilesFilePath;
            set
            {
                _processDataFilesFilePath = value;
                OnPropertyChanged();
                System.Diagnostics.Debug.WriteLine($"ProcessDataFilesFilePath changed to: {value}");
            }
        }

        public string Building
        {
            get => _building;
            set { _building = value; OnPropertyChanged(); }
        }

        public string Device
        {
            get => _device;
            set { _device = value; OnPropertyChanged(); }
        }

        public string AreaName
        {
            get => _areaName;
            set { _areaName = value; OnPropertyChanged(); }
        }

        public string Organization
        {
            get => _organization;
            set { _organization = value; OnPropertyChanged(); }
        }

        public string LineName
        {
            get => _lineName;
            set { _lineName = value; OnPropertyChanged(); }
        }

        public string SiteName
        {
            get => _siteName;
            set { _siteName = value; OnPropertyChanged(); }
        }

        public string StationName
        {
            get => _stationName;
            set { _stationName = value; OnPropertyChanged(); }
        }

        public string ProcessType
        {
            get => _processType;
            set { _processType = value; OnPropertyChanged(); }
        }

        public string MachineName
        {
            get => _machineName;
            set { _machineName = value; OnPropertyChanged(); }
        }

        public string CreatedBy
        {
            get => _createdBy;
            set { _createdBy = value; OnPropertyChanged(); }
        }

        public string UniqueId
        {
            get => _uniqueId ?? _machineInfoOptions?.CurrentValue?.UniqueId ?? "";
            set { _uniqueId = value; OnPropertyChanged(); }
        }

        public string Version
        {
            get => _version ?? _machineInfoOptions?.CurrentValue?.Version ?? "";
            set { _version = value; OnPropertyChanged(); }
        }

        public int HeartbeatFrequency
        {
            get => _heartbeatFrequency != 0 ? _heartbeatFrequency : (_machineInfoOptions?.CurrentValue?.HeartbeatFrequency ?? 5);
            set { _heartbeatFrequency = value; OnPropertyChanged(); }
        }

        public string Endpoint
        {
            get => _endpoint ?? _apiOptions?.CurrentValue?.Endpoint ?? "";
            set { _endpoint = value; OnPropertyChanged(); }
        }

        public string LoginUri
        {
            get => _loginUri ?? _apiOptions?.CurrentValue?.LoginUri ?? "";
            set { _loginUri = value; OnPropertyChanged(); }
        }

        public ConfigurationViewModel(
            IOptionsMonitor<MachineInfoSetting> machineInfoOptions,
            IOptionsMonitor<ApiSetting> apiOptions,
            IConfiguration configuration)
        {
            _machineInfoOptions = machineInfoOptions;
            _apiOptions = apiOptions;

            // 从配置加载初始值
            _defaultLogLevel = configuration["Logging:LogLevel:Default"] ?? "Information";
            _microsoftHostingLifetimeLogLevel = configuration["Logging:LogLevel:Microsoft.Hosting.Lifetime"] ?? "Information";
            _hostName = configuration["RabbitMQ:HostName"] ?? "localhost";
            _userName = configuration["RabbitMQ:UserName"] ?? "guest";
            _password = configuration["RabbitMQ:Password"] ?? "guest";
            _port = int.TryParse(configuration["RabbitMQ:Port"], out var port) ? port : 5672;
            _virtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";
            _automaticRecoveryEnabled = bool.TryParse(configuration["RabbitMQ:AutomaticRecoveryEnabled"], out var autoRec) ? autoRec : true;
            _networkRecoveryIntervalSeconds = int.TryParse(configuration["RabbitMQ:NetworkRecoveryIntervalSeconds"], out var nrs) ? nrs : 10;
            _prefix = configuration["RabbitMQPublisherSettings:Prefix"] ?? "";
            _maxConcurrency = int.TryParse(configuration["BackgroundTask:MaxConcurrency"], out var mc) ? mc : 3;
            _delayBetweenBatchesMs = int.TryParse(configuration["BackgroundTask:DelayBetweenBatchesMs"], out var db) ? db : 1000;
            _productionInformationFilePath = configuration["CsvFilePath:ProductionInformationFilePath"] ?? "";
            _machineStatusInformationFilePath = configuration["CsvFilePath:MachineStatusInformationFilePath"] ?? "";
            _processDataFilesFilePath = configuration["CsvFilePath:ProcessDataFilesFilePath"] ?? "";
            _building = configuration["MachineMetadata:Building"] ?? "";
            _device = configuration["MachineMetadata:Device"] ?? "";
            _areaName = configuration["MachineMetadata:AreaName"] ?? "";
            _organization = configuration["MachineMetadata:Organization"] ?? "";
            _lineName = configuration["MachineMetadata:LineName"] ?? "";
            _siteName = configuration["MachineMetadata:SiteName"] ?? "";
            _stationName = configuration["MachineMetadata:StationName"] ?? "";
            _processType = configuration["MachineMetadata:ProcessType"] ?? "";
            _machineName = configuration["MachineMetadata:MachineName"] ?? "";
            _createdBy = configuration["MachineMetadata:CreatedBy"] ?? "";
            _uniqueId = configuration["MachineInfo:UniqueId"];
            _version = configuration["MachineInfo:Version"];
            _heartbeatFrequency = int.TryParse(configuration["MachineInfo:HeartbeatFrequency"], out var hf) ? hf : 0;
            _endpoint = configuration["Api:Endpoint"];
            _loginUri = configuration["Api:LoginUri"];

            // 监听配置变化
            if (_machineInfoOptions != null)
            {
                _optionsChangeTokens.Add(_machineInfoOptions.OnChange(newValue =>
                {
                    if (string.IsNullOrEmpty(_uniqueId))
                        OnPropertyChanged(nameof(UniqueId));
                    if (string.IsNullOrEmpty(_version))
                        OnPropertyChanged(nameof(Version));
                    if (_heartbeatFrequency == 0)
                        OnPropertyChanged(nameof(HeartbeatFrequency));
                }));
            }

            if (_apiOptions != null)
            {
                _optionsChangeTokens.Add(_apiOptions.OnChange(newValue =>
                {
                    if (string.IsNullOrEmpty(_endpoint))
                        OnPropertyChanged(nameof(Endpoint));
                    if (string.IsNullOrEmpty(_loginUri))
                        OnPropertyChanged(nameof(LoginUri));
                }));
            }
        }

        // 设计时构造函数
        public ConfigurationViewModel()
        {
            _defaultLogLevel = "Information";
            _microsoftHostingLifetimeLogLevel = "Information";
            _hostName = "localhost";
            _userName = "guest";
            _password = "guest";
            _port = 5672;
            _virtualHost = "/";
            _automaticRecoveryEnabled = true;
            _networkRecoveryIntervalSeconds = 10;
            _prefix = "";
            _maxConcurrency = 3;
            _delayBetweenBatchesMs = 1000;
            _productionInformationFilePath = "";
            _machineStatusInformationFilePath = "";
            _processDataFilesFilePath = "";
            _building = "";
            _device = "";
            _areaName = "";
            _organization = "";
            _lineName = "";
            _siteName = "";
            _stationName = "";
            _processType = "";
            _machineName = "";
            _createdBy = "";
            _uniqueId = "";
            _version = "";
            _heartbeatFrequency = 5;
            _endpoint = "";
            _loginUri = "";
        }

        public void NotifyAllPropertiesChanged()
        {
            OnPropertyChanged(nameof(DefaultLogLevel));
            OnPropertyChanged(nameof(MicrosoftHostingLifetimeLogLevel));
            OnPropertyChanged(nameof(HostName));
            OnPropertyChanged(nameof(UserName));
            OnPropertyChanged(nameof(Password));
            OnPropertyChanged(nameof(Port));
            OnPropertyChanged(nameof(VirtualHost));
            OnPropertyChanged(nameof(AutomaticRecoveryEnabled));
            OnPropertyChanged(nameof(NetworkRecoveryIntervalSeconds));
            OnPropertyChanged(nameof(Prefix));
            OnPropertyChanged(nameof(Endpoint));
            OnPropertyChanged(nameof(LoginUri));
            OnPropertyChanged(nameof(MaxConcurrency));
            OnPropertyChanged(nameof(DelayBetweenBatchesMs));
            OnPropertyChanged(nameof(ProductionInformationFilePath));
            OnPropertyChanged(nameof(MachineStatusInformationFilePath));
            OnPropertyChanged(nameof(ProcessDataFilesFilePath));
            OnPropertyChanged(nameof(UniqueId));
            OnPropertyChanged(nameof(Version));
            OnPropertyChanged(nameof(HeartbeatFrequency));
            OnPropertyChanged(nameof(Building));
            OnPropertyChanged(nameof(Device));
            OnPropertyChanged(nameof(AreaName));
            OnPropertyChanged(nameof(Organization));
            OnPropertyChanged(nameof(LineName));
            OnPropertyChanged(nameof(SiteName));
            OnPropertyChanged(nameof(StationName));
            OnPropertyChanged(nameof(ProcessType));
            OnPropertyChanged(nameof(MachineName));
            OnPropertyChanged(nameof(CreatedBy));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void Dispose()
        {
            foreach (var token in _optionsChangeTokens)
                token?.Dispose();
            _optionsChangeTokens.Clear();
        }
    }
}