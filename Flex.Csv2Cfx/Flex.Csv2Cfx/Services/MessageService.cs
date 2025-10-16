using Amqp;
using Amqp.Framing;
using Flex.Csv2Cfx.Extensions;  // 添加这个引用
using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using MQTTnet;
using MQTTnet.Channel;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;

namespace Flex.Csv2Cfx.Services
{
    public class MessageService : IMessageService, IDisposable
    {
        private Connection? _amqpConnection;
        private Session? _amqpSession;
        private SenderLink? _amqpSender;
        private IMqttClient _mqttClient;
        private readonly IConfigurationService _configuration;
        private readonly AppSettings _settings;
        private readonly MqttClientFactory _mqttFactory;
        private bool _disposed = false;
        private const string EXCHANGE_NAME = "amq.topic";
        private const string QUEUE_NAME = "ipc-cfx";
        private readonly List<Models.Message> _sentMessages = new();
        private bool _enableEncoding = true;  // 添加编码开关
        private string _uniqueId = "";

        //public bool IsConnected => (_amqpConnection?.IsOpen ?? false) || (_mqttClient?.IsConnected ?? false);
        public bool IsConnected => (_amqpConnection?.IsClosed == false) || (_mqttClient?.IsConnected ?? false);
        public IReadOnlyList<Models.Message> SentMessages => _sentMessages.AsReadOnly();
        public MessageProtocol CurrentProtocol { get; set; }
        public bool EnableEncoding  // 添加属性以便外部控制
        {
            get => _enableEncoding;
            set => _enableEncoding = value;
        }

        public MessageService(IConfigurationService configuration)
        {
            _configuration = configuration;
            _settings = _configuration.GetSettings();
            _uniqueId = _settings.MachineSettings.Cfx.UniqueId;
            CurrentProtocol = _settings.PreferredProtocol;
            _mqttFactory = new MqttClientFactory();
            _mqttClient = _mqttFactory.CreateMqttClient();
        }

        public void SetProtocol(MessageProtocol protocol)
        {
            CurrentProtocol = protocol;
            AddSystemMessage("系统", $"切换到协议: {protocol}");
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                bool amqpConnected = false;
                bool mqttConnected = false;

                // 使用AMQPNetLite创建AMQP连接
                if (CurrentProtocol == MessageProtocol.AMQP)
                {
                    try
                    {
                        // 步骤1（可选）: 确保队列存在 - 只在第一次运行时需要
                        await EnsureQueueExistsAsync();

                        Address address = new Address(
                            _settings.RabbitMqSettings.HostName,
                            5672,
                            _settings.RabbitMqSettings.Username,
                            _settings.RabbitMqSettings.Password,
                            _settings.RabbitMqSettings.VirtualHost,
                            "AMQP");

                        // 创建连接
                        _amqpConnection = await Connection.Factory.CreateAsync(address);

                        // 创建会话
                        _amqpSession = new Session(_amqpConnection);

                        // 创建发送者链接到队列
                        // 使用topic语法：/exchange/交换机名/routing-key
                        string targetAddress = $"/exchange/{EXCHANGE_NAME}/{_uniqueId}";
                        _amqpSender = new SenderLink(_amqpSession, "sender-link", targetAddress);

                        amqpConnected = true;
                        AddSystemMessage("系统", $"AMQP连接成功，目标: {targetAddress}");
                    }
                    catch (Exception ex)
                    {
                        AddSystemMessage("系统", $"AMQP连接失败: {ex.Message}");
                        return false;
                    }
                }

                // 连接 MQTT (如果需要)
                if (CurrentProtocol == MessageProtocol.MQTT)
                {
                    try
                    {
                        var mqttOptions = new MqttClientOptionsBuilder()
                            .WithTcpServer(_settings.MqttSettings.Server, _settings.MqttSettings.Port)
                            .WithCredentials(_settings.MqttSettings.Username, _settings.MqttSettings.Password)
                            .WithClientId($"{_settings.MqttSettings.ClientIdPrefix}-{Environment.MachineName}")
                            .WithCleanSession(false)
                            .Build();

                        var connectResult = await _mqttClient!.ConnectAsync(mqttOptions, CancellationToken.None);

                        if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                        {
                            string mqttTopic = _uniqueId;

                            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                                .WithTopicFilter(f =>
                                {
                                    f.WithTopic(mqttTopic);
                                    f.WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
                                })
                                .Build();

                            var subscribeResult = await _mqttClient.SubscribeAsync(subscribeOptions, CancellationToken.None);

                            AddSystemMessage("系统", $"MQTT订阅主题成功: {mqttTopic}");

                            mqttConnected = true;
                            AddSystemMessage("系统", "MQTT连接成功");
                        }
                        else
                        {
                            AddSystemMessage("系统", $"MQTT连接失败，结果代码: {connectResult.ResultCode}");
                        }
                    }
                    catch (Exception ex)
                    {
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
                AddSystemMessage("系统", $"连接消息服务失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 确保队列存在（使用 RabbitMQ.Client）
        /// </summary>
        private async Task EnsureQueueExistsAsync()
        {
            try
            {
                var factory = new RabbitMQ.Client.ConnectionFactory
                {
                    HostName = _settings.RabbitMqSettings.HostName,
                    UserName = _settings.RabbitMqSettings.Username,
                    Password = _settings.RabbitMqSettings.Password,
                    VirtualHost = _settings.RabbitMqSettings.VirtualHost
                };

                using var connection = await factory.CreateConnectionAsync();
                using var channel = await connection.CreateChannelAsync();

                // 声明队列（如果已存在则不会重复创建）
                await channel.QueueDeclareAsync(
                    queue: QUEUE_NAME,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                // 绑定到 exchange
                await channel.QueueBindAsync(
                    queue: QUEUE_NAME,
                    exchange: EXCHANGE_NAME,
                    routingKey: _uniqueId);

                // 也绑定 MQTT 格式
                string mqttRoutingKey = _uniqueId.Replace("/", ".");
                if (mqttRoutingKey != _uniqueId)
                {
                    await channel.QueueBindAsync(
                        queue: QUEUE_NAME,
                        exchange: EXCHANGE_NAME,
                        routingKey: mqttRoutingKey);
                }

                await channel.CloseAsync();
                await connection.CloseAsync();

                AddSystemMessage("系统", $"队列 {QUEUE_NAME} 已就绪");
            }
            catch (Exception ex)
            {
                AddSystemMessage("系统", $"队列初始化警告: {ex.Message}");
                // 不抛出异常，因为队列可能已经手动创建了
            }
        }

        public async Task<PublishResult> PublishMessageAsync(string topic, string message)
        {
            return CurrentProtocol switch
            {
                MessageProtocol.MQTT => await PublishMqttMessageAsync(topic, message),
                MessageProtocol.AMQP => await PublishAmqpMessageAsync(topic, message),
                // MessageProtocol.Both => await PublishBothAsync(topic, message),
                _ => new PublishResult(false, "未知的协议类型")
            };
        }

        public async Task<PublishResult> PublishAmqpMessageAsync(string topic, string message)
        {
            try
            {
                if (_amqpSender == null || _amqpConnection?.IsClosed == true)
                {
                    return new PublishResult(false, "AMQP连接未打开");
                }

                // 编码消息
                string messageToSend = _enableEncoding ? MessageEncoder.EncodeMessage(message) : message;

                // 解析消息以获取 CFX 相关信息
                var messageInfo = ParseCfxMessage(message);

                // 创建AMQP消息
                var amqpMessage = new Amqp.Message(messageToSend)
                {
                    Properties = new Properties
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        ContentType = "application/json; charset=\"utf-8\"",
                        ContentEncoding = "gzip",
                        CreationTime = DateTime.UtcNow,
                        ReplyTo = _uniqueId // 或者使用messageInfo.Source
                    },
                    ApplicationProperties = new ApplicationProperties
                    {
                        ["cfx-handle"] = _uniqueId,
                        ["cfx-message"] = messageInfo.MessageName,
                        ["cfx-target"] = messageInfo.Target ?? "",
                        ["cfx-topic"] = "CFX"
                    }
                };

                // 发送消息
                await _amqpSender.SendAsync(amqpMessage);

                var sendMessage = new Models.Message
                {
                    Protocol = "AMQP",
                    Topic = topic,
                    Content = message,
                    Timestamp = DateTime.Now,
                    Status = _enableEncoding ? "成功 (已编码)" : "成功"
                };

                _sentMessages.Insert(0, sendMessage);
                return new PublishResult(true, _enableEncoding ? "AMQP消息发布成功 (已编码)" : "AMQP消息发布成功");
            }
            catch (Exception ex)
            {
                var errorMessage = new Models.Message
                {
                    Protocol = "AMQP",
                    Topic = topic,
                    Content = message,
                    Timestamp = DateTime.Now,
                    Status = $"失败: {ex.Message}"
                };

                _sentMessages.Insert(0, errorMessage);
                return new PublishResult(false, $"AMQP消息发布失败: {ex.Message}");
            }
        }

        public async Task<PublishResult> PublishMqttMessageAsync(string topic, string message)
        {
            try
            {
                if (_mqttClient == null || !_mqttClient.IsConnected)
                {
                    return new PublishResult(false, "MQTT客户端未连接");
                }

                // 编码消息
                string messageToSend = _enableEncoding ? MessageEncoder.EncodeMessage(message) : message;

                // MQTT topic 应该使用 _uniqueId（保持原始格式）
                // RabbitMQ 会自动将 / 转换为 . 进行路由
                string mqttTopic = _uniqueId;

                var applicationMessageBuilder = new MqttApplicationMessageBuilder()
                    .WithTopic(mqttTopic)  // 使用 _uniqueId 而不是传入的 topic
                    .WithPayload(messageToSend)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false);

                // 如果启用了编码，添加自定义属性
                if (_enableEncoding)
                {
                    applicationMessageBuilder.WithContentType("application/gzip+base64");
                }

                var applicationMessage = applicationMessageBuilder.Build();

                MqttClientPublishResult result = await _mqttClient.PublishAsync(applicationMessage, CancellationToken.None);

                var sentMessage = new Models.Message
                {
                    Protocol = "MQTT",
                    Topic = mqttTopic,  // 记录实际使用的 topic
                    Content = message,
                    Timestamp = DateTime.Now,
                    Status = result.IsSuccess
                        ? (_enableEncoding ? "成功 (已编码)" : "成功")
                        : $"失败: {result.ReasonString}"
                };

                _sentMessages.Insert(0, sentMessage);

                AddSystemMessage("系统", $"MQTT消息发布到 topic: {mqttTopic}");

                return new PublishResult(true, _enableEncoding ? "MQTT消息发布成功 (已编码)" : "MQTT消息发布成功");
            }
            catch (Exception ex)
            {
                var errorMessage = new Models.Message
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

            // AMQP，发布到 amq.topic 交换机，routing key 使用 topic
            var amqpResult = await PublishAmqpMessageAsync(topic, message);
            results.Add(amqpResult);

            // MQTT，topic 使用相同值
            var mqttResult = await PublishMqttMessageAsync(topic, message);
            results.Add(mqttResult);

            var successCount = results.Count(r => r.Success);
            var messageText = successCount == 2 ? "两条消息都发布成功" : successCount == 1 ? "一条消息发布成功" : "两条消息都发布失败";

            return new PublishResult(successCount > 0, messageText);
        }

        public async Task<List<Models.Message>> GetRecentSentMessagesAsync(int count = 100)
        {
            return await Task.FromResult(_sentMessages.Take(count).ToList());
        }

        private void AddSystemMessage(string type, string content)
        {
            var message = new Models.Message
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
            _amqpSender?.CloseAsync().Wait();
            _amqpSession?.CloseAsync().Wait();
            _amqpConnection?.CloseAsync().Wait();
            _ = _mqttClient?.DisconnectAsync();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _amqpSender = null;
                _amqpSession = null;
                _amqpConnection = null;
                _mqttClient?.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// 解析 CFX 消息以提取关键信息
        /// </summary>
        private CfxMessageInfo ParseCfxMessage(string jsonMessage)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonMessage);
                var root = doc.RootElement;

                return new CfxMessageInfo
                {
                    MessageName = root.TryGetProperty("MessageName", out var messageName)
                        ? messageName.GetString() ?? "Unknown"
                        : "Unknown",
                    Version = root.TryGetProperty("Version", out var version)
                        ? version.GetString() ?? "1.7"
                        : "1.7",
                    MessageId = root.TryGetProperty("RequestID", out var requestId)
                        ? requestId.GetString() ?? Guid.NewGuid().ToString()
                        : Guid.NewGuid().ToString(),
                    RequestId = root.TryGetProperty("RequestID", out var reqId)
                        ? reqId.GetString()
                        : null,
                    Source = root.TryGetProperty("Source", out var source)
                        ? source.GetString()
                        : null,
                    Target = root.TryGetProperty("Target", out var target)
                        ? target.GetString()
                        : null,
                    UniqueId = root.TryGetProperty("UniqueID", out var uniqueId)
                        ? uniqueId.GetString()
                        : null
                };
            }
            catch
            {
                return new CfxMessageInfo
                {
                    MessageName = "Unknown",
                    Version = "1.7",
                    MessageId = Guid.NewGuid().ToString()
                };
            }
        }

        /// <summary>
        /// CFX 消息信息类
        /// </summary>
        private class CfxMessageInfo
        {
            public string MessageName { get; set; } = "Unknown";
            public string Version { get; set; } = "1.7";
            public string MessageId { get; set; } = "";
            public string? RequestId { get; set; }
            public string? Source { get; set; }
            public string? Target { get; set; }
            public string? UniqueId { get; set; }
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