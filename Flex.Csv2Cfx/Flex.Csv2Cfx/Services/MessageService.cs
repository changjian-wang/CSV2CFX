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
        // private readonly AppSettings _settings;
        private bool _disposed = false;
        private const string EXCHANGE_NAME = "amq.topic";
        private readonly List<Message> _sentMessages = new();
        public bool IsConnected => (_amqpConnection?.IsOpen ?? false) || (_mqttClient?.IsConnected ?? false);

        public IReadOnlyList<Message> SentMessages => _sentMessages.AsReadOnly();

        public MessageService(IConfigurationService configuration)
        {
            _configuration = configuration;
            _settings = _configuration.GetSettings();
            _amqpFactory = new ConnectionFactory()
            {
                HostName = _settings.RabbitMqSettings.HostName,
                UserName = _settings.RabbitMqSettings.Username,
                Password = _settings.RabbitMqSettings.Password,
                VirtualHost = _settings.RabbitMqSettings.VirtualHost,
                // 新版本推荐设置
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _mqttFactory = new MqttClientFactory();
            _mqttClient = _mqttFactory.CreateMqttClient();
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                // 连接AMQP
                _amqpConnection = await _amqpFactory.CreateConnectionAsync();
                _amqpChannel = await _amqpConnection.CreateChannelAsync();

                // 声明队列并绑定
                List<string> topics = new List<string>
                {
                    "system/heartbeat",
                    "system/works",
                    "system/states"
                };

                // 创建队列
                foreach (var topic in topics)
                {
                    string queueName = $"common.queue.{topic}";

                    await _amqpChannel.QueueDeclareAsync(
                        queue: queueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false);

                    await _amqpChannel.QueueBindAsync(
                        queue: queueName,
                        exchange: EXCHANGE_NAME,
                        routingKey: topic); // 这里的 routingKey 要与 MQTT 发布的 topic 保持一致
                }

                // 连接MQTT
                var mqttOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer(_settings.MqttSettings.Server, _settings.MqttSettings.Port)
                    .WithCredentials(_settings.MqttSettings.Username, _settings.MqttSettings.Password)
                    .WithClientId($"{_settings.MqttSettings.ClientIdPrefix}-{Guid.NewGuid()}")
                    .WithCleanSession()
                    .Build();

                var connectResult = await _mqttClient!.ConnectAsync(mqttOptions, CancellationToken.None);

                if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                {
                    AddSystemMessage("系统", "消息服务连接成功");
                    return true;
                }
                else
                {
                    AddSystemMessage("系统", $"MQTT连接失败，结果代码: {connectResult.ResultCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AddSystemMessage("系统", $"连接消息服务失败: {ex.Message}");
                return false;
            }
        }

        public async Task<PublishResult> PublishAmqpMessageAsync(string routingKey, string message)
        {
            try
            {
                if (_amqpChannel == null || !_amqpConnection!.IsOpen)
                {
                    return new PublishResult(false, "AMQP connection is not established.");
                }

                var body = Encoding.UTF8.GetBytes(message);
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                };

                await _amqpChannel.BasicPublishAsync(
                    exchange: EXCHANGE_NAME,
                    routingKey: routingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body);

                var sendMessage = new Message
                {
                    Protocol = "AMQP",
                    Topic = routingKey,
                    Content = message,
                    Timestamp = DateTime.Now,
                    Status = "成功"
                };

                _sentMessages.Insert(0, sendMessage);
                return new PublishResult(true, "消息发布成功.");
            }
            catch (Exception ex)
            {
                var errorMessage = new Message
                {
                    Protocol = "AMQP",
                    Topic = routingKey,
                    Content = message,
                    Timestamp = DateTime.Now,
                    Status = $"失败: {ex.Message}"
                };

                _sentMessages.Insert(0, errorMessage);
                return new PublishResult(false, $"消息发布失败: {ex.Message}");
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

                var applicationMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(message)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(applicationMessage, CancellationToken.None);

                var sentMessage = new Message
                {
                    Protocol = "MQTT",
                    Topic = topic,
                    Content = message,
                    Timestamp = DateTime.Now,
                    Status = "成功"
                };

                _sentMessages.Insert(0, sentMessage);
                return new PublishResult(true, "消息发布成功");
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
                return new PublishResult(false, $"发布失败: {ex.Message}");
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
