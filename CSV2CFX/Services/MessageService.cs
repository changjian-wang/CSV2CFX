using CSV2CFX.AppSettings;
using CSV2CFX.Enums;
using CSV2CFX.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSV2CFX.Services
{
    public class MessageService : IMessageService
    {
        private readonly ILogger<MessageService> _logger;
        private readonly IOptionsMonitor<MessageBrokerSetting> _brokerOptions;
        private readonly IServiceProvider _serviceProvider;
        private IMessagePublisher? _currentPublisher;

        public MessageService(
            ILogger<MessageService> logger,
            IOptionsMonitor<MessageBrokerSetting> brokerOptions,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _brokerOptions = brokerOptions;
            _serviceProvider = serviceProvider;

            // 监听配置变化
            _brokerOptions.OnChange(async _ => await ReinitializePublisher());
        }

        public async Task InitializeAsync()
        {
            await ReinitializePublisher();
        }

        public async Task PublishMessageAsync(string topic, string message)
        {
            if (_currentPublisher == null)
            {
                await InitializeAsync();
            }

            await _currentPublisher!.PublishMessageAsync(topic, message);
        }

        public async Task CreateTopicAsync(string topicName)
        {
            if (_currentPublisher == null)
            {
                await InitializeAsync();
            }

            await _currentPublisher!.CreateTopicAsync(topicName);
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (_currentPublisher == null)
            {
                await InitializeAsync();
            }

            return await _currentPublisher!.TestConnectionAsync();
        }

        private async Task ReinitializePublisher()
        {
            try
            {
                _currentPublisher?.Dispose();

                var settings = _brokerOptions.CurrentValue;
                _currentPublisher = settings.ProtocolType switch
                {
                    ProtocolType.AMQP => new RabbitMqPublisherAdapter(
                        _serviceProvider.GetRequiredService<IOptionsMonitor<RabbitMQPublisherSettings>>(),
                        _serviceProvider.GetRequiredService<IRabbitMQService>(),
                        _serviceProvider.GetRequiredService<ILogger<RabbitMqPublisherAdapter>>()),
                    ProtocolType.MQTT => _serviceProvider.GetRequiredService<MqttService>(),
                    _ => throw new NotSupportedException($"Protocol type {settings.ProtocolType} is not supported")
                };

                _logger.LogInformation("Message publisher switched to {ProtocolType}", settings.ProtocolType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize message publisher");
                throw;
            }
        }
    }
}
