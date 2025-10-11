using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using MQTTnet;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using wuz_ipc_cfx_test; // 引用 wuz-ipc-cfx.dll
using CFX.Structures;

namespace Flex.Csv2Cfx.Services
{
    public class MessageService : IMessageService, IDisposable
    {
        private dllHelper? _dllHelper; // 使用 dllHelper 替代原 AMQP 连接
        private IMqttClient? _mqttClient;
        private readonly IConfigurationService _configuration;
        private readonly ILogger<MessageService>? _logger;
        private readonly AppSettings _settings;
        private readonly MqttClientFactory _mqttFactory;
        private bool _disposed = false;
        private readonly List<Message> _sentMessages = new();

        // AMQP 相关配置
        private Metadata _metadata = new Metadata();
        private string _uniqueId = "";
        private string _myCFXHandle = "";
        private string _cfxEventBroker = "";
        private string _cfxBrokerExchange = "amq.topic";
        private string _routingKey = "";
        private bool _amqpConnected = false;

        public bool IsConnected => _amqpConnected || (_mqttClient?.IsConnected ?? false);
        public IReadOnlyList<Message> SentMessages => _sentMessages.AsReadOnly();
        public MessageProtocol CurrentProtocol { get; set; }

        public MessageService(IConfigurationService configuration, ILogger<MessageService>? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
            _settings = _configuration.GetSettings();
            CurrentProtocol = _settings.PreferredProtocol;

            // 初始化 MQTT
            _mqttFactory = new MqttClientFactory();
            _mqttClient = _mqttFactory.CreateMqttClient();

            // 加载 AMQP 配置
            LoadAmqpConfiguration();
        }

        private void LoadAmqpConfiguration()
        {
            var machineMetadata = _settings.MachineSettings.Metadata;
            var rabbitMq = _settings.RabbitMqSettings;
            var cfx = _settings.MachineSettings.Cfx;

            // 创建 Metadata
            _metadata = new Metadata
            {
                building = machineMetadata.Building ?? "",
                device = machineMetadata.Device ?? "",
                area_name = machineMetadata.AreaName ?? "",
                org = machineMetadata.Organization ?? "",
                line_name = machineMetadata.LineName ?? "",
                site_name = machineMetadata.SiteName ?? "",
                station_name = machineMetadata.StationName ?? "",
                Process_type = machineMetadata.ProcessType ?? "",
                machine_name = machineMetadata.MachineName ?? "",
                Created_by = string.IsNullOrEmpty(machineMetadata.CreatedBy) ? "GA" : machineMetadata.CreatedBy,
                previus_status = "Running",
                time_last_status = "00:00:00"
            };

            _uniqueId = cfx.UniqueId ?? "";
            _cfxEventBroker = $"amqp://{rabbitMq.Username}:{rabbitMq.Password}@{rabbitMq.HostName}:5672";
            _cfxBrokerExchange = "amq.topic";
            _routingKey = _uniqueId;
            _myCFXHandle = _routingKey;
        }

        public void SetProtocol(MessageProtocol protocol)
        {
            CurrentProtocol = protocol;
            AddSystemMessage("系统", $"切换到协议: {protocol}");
        }

        public async Task<bool> ConnectAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool amqpConnected = false;
                    bool mqttConnected = false;

                    // 连接 AMQP (使用 dllHelper)
                    if (CurrentProtocol == MessageProtocol.AMQP)
                    {
                        try
                        {
                            // 关闭现有连接
                            if (_dllHelper != null)
                            {
                                _dllHelper.CloseEndpoint();
                                _dllHelper = null;
                            }

                            // 创建新的 dllHelper 实例
                            _dllHelper = new dllHelper(
                                _cfxEventBroker,
                                _cfxBrokerExchange,
                                _routingKey,
                                _myCFXHandle,
                                _metadata,
                                _uniqueId);

                            string msg;
                            _dllHelper.OpenEndpointTopic(out msg);

                            if (msg.StartsWith("ERROR"))
                            {
                                _logger?.LogError("AMQP 连接失败: {Message}", msg);
                                AddSystemMessage("系统", $"AMQP连接失败: {msg}");
                                amqpConnected = false;
                            }
                            else
                            {
                                amqpConnected = true;
                                _amqpConnected = true;
                                _logger?.LogInformation("AMQP 连接成功");
                                AddSystemMessage("系统", "AMQP连接成功");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "AMQP 连接异常");
                            AddSystemMessage("系统", $"AMQP连接失败: {ex.Message}");
                            amqpConnected = false;
                        }
                    }

                    // 连接 MQTT
                    if (CurrentProtocol == MessageProtocol.MQTT)
                    {
                        try
                        {
                            var mqttOptions = new MqttClientOptionsBuilder()
                                .WithTcpServer(_settings.MqttSettings.Server, _settings.MqttSettings.Port)
                                .WithCredentials(_settings.MqttSettings.Username, _settings.MqttSettings.Password)
                                .WithClientId($"{_settings.MqttSettings.ClientIdPrefix}-{Guid.NewGuid()}")
                                .WithCleanSession()
                                .Build();

                            var connectResult = _mqttClient!.ConnectAsync(mqttOptions, CancellationToken.None).Result;

                            if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                            {
                                mqttConnected = true;
                                _logger?.LogInformation("MQTT 连接成功");
                                AddSystemMessage("系统", "MQTT连接成功");
                            }
                            else
                            {
                                _logger?.LogWarning("MQTT 连接失败: {ResultCode}", connectResult.ResultCode);
                                AddSystemMessage("系统", $"MQTT连接失败，结果代码: {connectResult.ResultCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "MQTT 连接异常");
                            AddSystemMessage("系统", $"MQTT连接失败: {ex.Message}");
                        }
                    }

                    bool connected = (CurrentProtocol == MessageProtocol.AMQP && amqpConnected) ||
                                   (CurrentProtocol == MessageProtocol.MQTT && mqttConnected);

                    if (connected)
                    {
                        AddSystemMessage("系统", "消息服务连接成功");
                    }

                    return connected;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "连接消息服务失败");
                    AddSystemMessage("系统", $"连接消息服务失败: {ex.Message}");
                    return false;
                }
            });
        }

        /// <summary>
        /// 根据当前协议发送消息
        /// </summary>
        public async Task<PublishResult> PublishMessageAsync(string topic, string message)
        {
            return CurrentProtocol switch
            {
                MessageProtocol.MQTT => await PublishMqttMessageAsync(topic, message),
                MessageProtocol.AMQP => await PublishAmqpMessageAsync(topic, message),
                _ => new PublishResult(false, "未知的协议类型")
            };
        }

        /// <summary>
        /// 使用 dllHelper 发送 AMQP 消息
        /// </summary>
        public async Task<PublishResult> PublishAmqpMessageAsync(string routingKey, string message)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_dllHelper == null || !_amqpConnected)
                    {
                        var errorMsg = new Message
                        {
                            Protocol = "AMQP",
                            Topic = routingKey,
                            Content = message,
                            Timestamp = DateTime.Now,
                            Status = "失败: 未连接"
                        };
                        _sentMessages.Insert(0, errorMsg);
                        return new PublishResult(false, "AMQP 连接未建立");
                    }

                    // 使用 dllHelper 发送通用 JSON 数据
                    string jsonSend;
                    bool success = _dllHelper.sendCommonJsonData(message, out jsonSend);

                    var sentMessage = new Message
                    {
                        Protocol = "AMQP",
                        Topic = routingKey,
                        Content = message,
                        Timestamp = DateTime.Now,
                        Status = success ? "成功" : $"失败"
                    };

                    _sentMessages.Insert(0, sentMessage);

                    if (_sentMessages.Count > 1000)
                    {
                        _sentMessages.RemoveAt(_sentMessages.Count - 1);
                    }

                    if (!success)
                    {
                        _amqpConnected = false;
                        _logger?.LogWarning("AMQP 消息发送失败，可能已断开连接");
                    }

                    return new PublishResult(success, success ? "AMQP消息发布成功" : "AMQP消息发布失败");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "发送 AMQP 消息异常");

                    var errorMessage = new Message
                    {
                        Protocol = "AMQP",
                        Topic = routingKey,
                        Content = message,
                        Timestamp = DateTime.Now,
                        Status = $"失败: {ex.Message}"
                    };

                    _sentMessages.Insert(0, errorMessage);
                    _amqpConnected = false;

                    return new PublishResult(false, $"AMQP消息发布失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 发送 MQTT 消息
        /// </summary>
        public async Task<PublishResult> PublishMqttMessageAsync(string topic, string message)
        {
            try
            {
                if (_mqttClient == null || !_mqttClient.IsConnected)
                {
                    _logger?.LogWarning("MQTT 未连接，尝试发送消息失败");

                    var errorMsg = new Message
                    {
                        Protocol = "MQTT",
                        Topic = topic,
                        Content = message,
                        Timestamp = DateTime.Now,
                        Status = "失败: 未连接"
                    };
                    _sentMessages.Insert(0, errorMsg);
                    return new PublishResult(false, "MQTT客户端未连接");
                }

                var applicationMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(message)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                MqttClientPublishResult result = await _mqttClient.PublishAsync(applicationMessage, CancellationToken.None);

                // ✅ 确保添加到消息列表
                var sentMessage = new Message
                {
                    Protocol = "MQTT",
                    Topic = topic,
                    Content = message,
                    Timestamp = DateTime.Now,
                    Status = result.IsSuccess ? "成功" : $"失败: {result.ReasonString}"
                };

                _sentMessages.Insert(0, sentMessage);

                if (_sentMessages.Count > 1000)
                {
                    _sentMessages.RemoveAt(_sentMessages.Count - 1);
                }

                _logger?.LogDebug("MQTT 消息已发送并添加到列表: Topic={Topic}, Status={Status}", topic, sentMessage.Status);

                return new PublishResult(result.IsSuccess, result.IsSuccess ? "MQTT消息发布成功" : $"MQTT发布失败: {result.ReasonString}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "发送 MQTT 消息异常");

                var errorMessage = new Message
                {
                    Protocol = "MQTT",
                    Topic = topic,
                    Content = message,
                    Timestamp = DateTime.Now,
                    Status = $"失败: {ex.Message}"
                };

                _sentMessages.Insert(0, errorMessage);
                return new PublishResult(false, $"MQTT发布失败: {ex.Message}");
            }
        }

        public async Task<PublishResult> PublishBothAsync(string topic, string message)
        {
            var results = new List<PublishResult>();

            var amqpResult = await PublishAmqpMessageAsync(topic, message);
            results.Add(amqpResult);

            var mqttResult = await PublishMqttMessageAsync(topic, message);
            results.Add(mqttResult);

            var successCount = results.Count(r => r.Success);
            var messageText = successCount == 2 ? "两条消息都发布成功" :
                            successCount == 1 ? "一条消息发布成功" :
                            "两条消息都发布失败";

            return new PublishResult(successCount > 0, messageText);
        }

        public async Task<List<Message>> GetRecentSentMessagesAsync(int count = 100)
        {
            return await Task.FromResult(_sentMessages.Take(count).ToList());
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

            _sentMessages.Insert(0, message);
            if (_sentMessages.Count > 1000)
                _sentMessages.RemoveAt(_sentMessages.Count - 1);
        }

        public void Disconnect()
        {
            try
            {
                if (_dllHelper != null)
                {
                    _dllHelper.CloseEndpoint();
                    _dllHelper = null;
                    _amqpConnected = false;
                    _logger?.LogInformation("AMQP 连接已断开");
                }

                if (_mqttClient != null && _mqttClient.IsConnected)
                {
                    _ = _mqttClient.DisconnectAsync();
                    _logger?.LogInformation("MQTT 连接已断开");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "断开连接时发生错误");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _mqttClient?.Dispose();
                _disposed = true;
            }
        }
    }

    public class PublishResult
    {
        public bool Success { get; }
        public string Message { get; }

        public PublishResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }
    }
}