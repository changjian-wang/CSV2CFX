using Flex.Csv2Cfx.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Services
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly IConfiguration _configuration;
        private readonly string _configFilePath;
        private FileSystemWatcher? _fileWatcher;

        public event EventHandler? ConfigurationChanged;

        public ConfigurationService(IConfiguration configuration)
        {
            _configuration = configuration;
            _configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            SetupFileWatcher();
        }

        private void SetupFileWatcher()
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            if (directory == null) return;

            _fileWatcher = new FileSystemWatcher(directory, "appsettings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _fileWatcher.Changed += (sender, e) =>
            {
                // 延迟触发，避免多次触发
                Task.Delay(500).ContinueWith(_ => ConfigurationChanged?.Invoke(this, EventArgs.Empty));
            };

            _fileWatcher.EnableRaisingEvents = true;
        }

        public AppSettings GetSettings()
        {
            var settings = new AppSettings();

            _configuration.GetSection("MqttSettings").Bind(settings.MqttSettings);
            _configuration.GetSection("RabbitMqSettings").Bind(settings.RabbitMqSettings);
            _configuration.GetSection("MachineSettings:Metadata").Bind(settings.MachineSettings.Metadata);
            _configuration.GetSection("MachineSettings:Cfx").Bind(settings.MachineSettings.Cfx);
            _configuration.GetSection("MachineSettings:Csv").Bind(settings.MachineSettings.Csv);

            // 读取 LoginApiUrl
            settings.LoginApiUrl = _configuration["LoginApiUrl"] ?? "https://api.example.com/auth/login";

            // 读取 PreferredProtocol
            if (Enum.TryParse<MessageProtocol>(_configuration["PreferredProtocol"], out var protocol))
            {
                settings.PreferredProtocol = protocol;
            }

            return settings;
        }

        public async Task SaveSettingsAsync(AppSettings settings)
        {
            await SaveSettingsAsync(settings, preserveLoginApiUrl: false);
        }

        // 新增：支持保留 LoginApiUrl 的保存方法
        public async Task SaveSettingsAsync(AppSettings settings, bool preserveLoginApiUrl)
        {
            // 读取现有的 JSON
            var json = await File.ReadAllTextAsync(_configFilePath);
            var jsonDocument = JsonDocument.Parse(json);
            var root = jsonDocument.RootElement;

            // 创建新的配置对象
            var configObject = new Dictionary<string, object>();

            // 保留 Logging 配置
            if (root.TryGetProperty("Logging", out var loggingElement))
            {
                configObject["Logging"] = JsonSerializer.Deserialize<object>(loggingElement.GetRawText())!;
            }

            // 如果需要保留 LoginApiUrl，从现有配置读取；否则使用传入的值
            if (preserveLoginApiUrl && root.TryGetProperty("LoginApiUrl", out var loginApiElement))
            {
                configObject["LoginApiUrl"] = loginApiElement.GetString() ?? "https://api.example.com/auth/login";
            }
            else
            {
                configObject["LoginApiUrl"] = settings.LoginApiUrl;
            }

            // 更新 PreferredProtocol
            configObject["PreferredProtocol"] = settings.PreferredProtocol.ToString();

            // 更新 MqttSettings 和 RabbitMqSettings
            configObject["MqttSettings"] = settings.MqttSettings;
            configObject["RabbitMqSettings"] = settings.RabbitMqSettings;

            // 更新 MachineSettings
            configObject["MachineSettings"] = new
            {
                Metadata = settings.MachineSettings.Metadata,
                Cfx = settings.MachineSettings.Cfx,
                Csv = settings.MachineSettings.Csv
            };

            // 写入文件
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var jsonString = JsonSerializer.Serialize(configObject, options);

            // 临时禁用文件监视以避免触发 Changed 事件
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
            }

            await File.WriteAllTextAsync(_configFilePath, jsonString);

            // 重新启用文件监视
            await Task.Delay(1000);

            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = true;
            }
        }
    }
}