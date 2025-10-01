using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using MQTTnet;
using MQTTnet.Channel;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Services
{
    public class MessageService : IMessageService, IDisposable
    {
        private IConnection? _amqpConnection;
        private IChannel? _amqpChannel;
        private IMqttClient? _mqttClient;
        private readonly ConnectionFactory _amqpFactory;
        private readonly MqttClientFactory _mqttFactory;
        // private readonly AppSettings _settings;
        private bool _disposed = false;
        private readonly List<Message> _sentMessages = new();
        public bool IsConnected => (_amqpConnection?.IsOpen ?? false) || (_mqttClient?.IsConnected ?? false);

        public IReadOnlyList<Message> SentMessages => _sentMessages.AsReadOnly();

        public MessageService()
        {
            _amqpFactory = new ConnectionFactory()
            {
                HostName = "localhost",
                UserName = "guest",
                Password = "guest",
                VirtualHost = "/",
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

                // 声明交换机
                await _amqpChannel.ExchangeDeclareAsync(
                    exchange: "wpf.system.exchange",
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false);

                // 连接MQTT
                var mqttOptions = new MqttClientOptionsBuilder()
                    .WithTcpServer("localhost", 1883)
                    .WithCredentials("guest", "guest")
                    .WithClientId($"wpf-system-{Guid.NewGuid()}")
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

        public async Task<PublishResult> PublishAmqpMessageAsync(string exchange, string routingKey, string message)
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
                    exchange: exchange,
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

        public async Task<PublishResult> PublishBothAsync(string amqpRoutingKey, string mqttTopic, string message)
        {
            var results = new List<PublishResult>();

            // 发布AMQP消息
            var amqpResult = await PublishAmqpMessageAsync("wpf.system.exchange", amqpRoutingKey, message);
            results.Add(amqpResult);

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
