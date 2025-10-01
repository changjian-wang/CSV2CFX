using CSV2CFX.AppSettings;
using CSV2CFX.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSV2CFX.Services
{
    public class RabbitMqPublisherAdapter : IMessagePublisher
    {
        private readonly IOptionsMonitor<RabbitMQPublisherSettings> _rabbitMQPublisherOptions;
        private readonly IRabbitMQService _rabbitMQService;
        private readonly ILogger<RabbitMqPublisherAdapter> _logger;
        private const string QUEUE_SUFFIX = "queue";
        private const string EXCHANGE_SUFFIX = "exchange";
        private const string ROUTINGKEY_SUFFIX = "routing-key";

        public string ProtocolName => "AMQP/RabbitMQ";

        public RabbitMqPublisherAdapter(
            IOptionsMonitor<RabbitMQPublisherSettings> rabbitMQPublisherOptions,
            IRabbitMQService rabbitMQService,
            ILogger<RabbitMqPublisherAdapter> logger)
        {
            _rabbitMQPublisherOptions = rabbitMQPublisherOptions;
            _rabbitMQService = rabbitMQService;
            _logger = logger;
        }

        public async Task PublishMessageAsync(string topic, string message)
        {
            // 将 MQTT 风格的 topic 转换为 RabbitMQ 的 exchange 和 routing key
            var exchangeName = $"{_rabbitMQPublisherOptions.CurrentValue.Prefix}.{EXCHANGE_SUFFIX}";
            var routingKey = topic.Replace("/", $".{ROUTINGKEY_SUFFIX}");

            await _rabbitMQService.PublishMessageAsync(exchangeName, routingKey, message);
            _logger.LogInformation("Message published via RabbitMQ adapter to topic '{Topic}' (exchange: '{Exchange}', routing key: '{RoutingKey}')",
                topic, exchangeName, routingKey);
        }

        public async Task CreateTopicAsync(string topicName)
        {
            var exchangeName = $"{_rabbitMQPublisherOptions.CurrentValue.Prefix}.{EXCHANGE_SUFFIX}";
            await _rabbitMQService.CreateExchangeAsync(exchangeName);

            // 创建队列
            var queueName = topicName.Replace("/", $".{QUEUE_SUFFIX}");
            await _rabbitMQService.CreateQueueAsync(queueName);
            await _rabbitMQService.BindQueueAsync(queueName, exchangeName, queueName);

            _logger.LogInformation("Topic '{TopicName}' created via RabbitMQ adapter (queue: '{QueueName}', exchange: '{Exchange}')",
                topicName, queueName, exchangeName);
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // 尝试创建一个临时 exchange 来测试连接
                await _rabbitMQService.CreateExchangeAsync("test.connection");
                _logger.LogInformation("RabbitMQ connection test successful");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ connection test failed");
                return false;
            }
        }

        public void Dispose()
        {
            if (_rabbitMQService is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
