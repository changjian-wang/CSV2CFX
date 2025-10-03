using Flex.Csv2Cfx.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            return settings;
        }

        public async Task SaveSettingsAsync(AppSettings settings)
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

            // 更新 MqttSettings 和 RabbitMqSettings
            configObject["MqttSettings"] = settings.MqttSettings;
            configObject["RabbitMqSettings"] = settings.RabbitMqSettings;

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
