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
using System.Threading.Tasks;
using System.Xml;

namespace Flex.Csv2Cfx.Services
{
    public class MessageService : IMessageService, IDisposable
    {
        private IConnection? _amqpConnection;
        private IChannel? _amqpChannel;
        private IMqttClient? _mqttClient;
        private readonly IConfigurationService _configuration;
        private readonly AppSettings _settings;
        private readonly ConnectionFactory _amqpFactory;
        private readonly MqttClientFactory _mqttFactory;
        private bool _disposed = false;
        private const string EXCHANGE_NAME = "amq.topic";
        private const string QUEUE_NAME = "ipc-cfx";
        private readonly List<Message> _sentMessages = new();
        private bool _enableEncoding = true;  // 添加编码开关
        private string _uniqueId = "";

        public bool IsConnected => (_amqpConnection?.IsOpen ?? false) || (_mqttClient?.IsConnected ?? false);
        public IReadOnlyList<Message> SentMessages => _sentMessages.AsReadOnly();
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

            _amqpFactory = new ConnectionFactory()
            {
                HostName = _settings.RabbitMqSettings.HostName,
                UserName = _settings.RabbitMqSettings.Username,
                Password = _settings.RabbitMqSettings.Password,
                VirtualHost = _settings.RabbitMqSettings.VirtualHost,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

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

                // 始终使用 AMQP 创建队列（无论当前协议是什么）
                try
                {
                    var amqpConnection = await _amqpFactory.CreateConnectionAsync();
                    var amqpChannel = await amqpConnection.CreateChannelAsync();

                    // 声明统一的队列
                    string queueName = QUEUE_NAME;

                    await amqpChannel.QueueDeclareAsync(
                        queue: queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false);

                    // 重要：绑定多个可能的 routing key 格式
                    // 1. 原始格式（如果 _uniqueId 本身包含 /）
                    await amqpChannel.QueueBindAsync(
                        queue: queueName,
                        exchange: EXCHANGE_NAME,
                        routingKey: _uniqueId);

                    // 2. MQTT 格式（/ 转换为 .）
                    string mqttRoutingKey = _uniqueId.Replace("/", ".");
                    if (mqttRoutingKey != _uniqueId)
                    {
                        await amqpChannel.QueueBindAsync(
                            queue: queueName,
                            exchange: EXCHANGE_NAME,
                            routingKey: mqttRoutingKey);
                    }

                    // 3. 如果需要支持通配符，可以添加：
                    // await amqpChannel.QueueBindAsync(
                    //     queue: queueName,
                    //     exchange: EXCHANGE_NAME,
                    //     routingKey: "#");  // 匹配所有消息（仅用于调试）

                    AddSystemMessage("系统", $"AMQP队列创建成功，绑定 routing keys: {_uniqueId}, {mqttRoutingKey}");

                    // 如果当前协议是 AMQP，保持连接；否则关闭
                    if (CurrentProtocol == MessageProtocol.AMQP)
                    {
                        _amqpConnection = amqpConnection;
                        _amqpChannel = amqpChannel;
                        amqpConnected = true;
                        AddSystemMessage("系统", "AMQP连接成功");
                    }
                    else
                    {
                        // 队列创建完成后关闭 AMQP 连接
                        await amqpChannel.CloseAsync();
                        await amqpConnection.CloseAsync();
                        AddSystemMessage("系统", "AMQP队列创建完成，连接已关闭");
                    }
                }
                catch (Exception ex)
                {
                    AddSystemMessage("系统", $"AMQP队列创建失败: {ex.Message}");

                    // 如果当前协议是 AMQP，这是一个致命错误
                    if (CurrentProtocol == MessageProtocol.AMQP)
                    {
                        return false;
                    }
                }

                // 连接MQTT (如果需要)
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
                            // MQTT 订阅使用原始格式（保持 / 分隔符）
                            // RabbitMQ MQTT 插件会自动将其转换为 . 用于路由
                            string mqttTopic = _uniqueId;  // 保持原始格式

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
                               (CurrentProtocol == MessageProtocol.MQTT && mqttConnected) ||
                               (amqpConnected || mqttConnected);

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

        // 新增：根据当前协议发送消息
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
                if (_amqpChannel == null || !_amqpConnection!.IsOpen)
                {
                    return new PublishResult(false, "AMQP connection is not established.");
                }

                // 编码消息
                string messageToSend = _enableEncoding ? MessageEncoder.EncodeMessage(message) : message;
                var body = Encoding.UTF8.GetBytes(messageToSend);

                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = _enableEncoding ? "application/gzip+base64" : "application/json",  // 修改 ContentType
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                // 如果启用了编码，可以添加自定义头部标识
                if (_enableEncoding)
                {
                    properties.Headers = new Dictionary<string, object>
                    {
                        { "encoding", "gzip+base64" },
                        { "original-size", message.Length }
                    };
                }

                await _amqpChannel.BasicPublishAsync(
                    exchange: EXCHANGE_NAME,
                    routingKey: _uniqueId,
                    mandatory: false,
                    basicProperties: properties,
                    body: body);

                var sendMessage = new Message
                {
                    Protocol = "AMQP",
                    Topic = topic,
                    Content = message,  // 保存原始消息用于显示
                    Timestamp = DateTime.Now,
                    Status = _enableEncoding ? "成功 (已编码)" : "成功"
                };

                _sentMessages.Insert(0, sendMessage);
                return new PublishResult(true, _enableEncoding ? "AMQP消息发布成功 (已编码)" : "AMQP消息发布成功");
            }
            catch (Exception ex)
            {
                var errorMessage = new Message
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

                var sentMessage = new Message
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
            _amqpChannel?.CloseAsync();
            _amqpConnection?.CloseAsync();
            _ = _mqttClient?.DisconnectAsync();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _amqpChannel?.Dispose();
                _amqpConnection?.Dispose();
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