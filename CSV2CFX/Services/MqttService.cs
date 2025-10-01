using CSV2CFX.AppSettings;
using CSV2CFX.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSV2CFX.Services
{
    public class MqttService : IMessagePublisher
    {
        private readonly ILogger<MqttService> _logger;
        private readonly IOptionsMonitor<MessageBrokerSetting> _options;
        private IMqttClient? _mqttClient;
        private MqttClientOptions? _mqttOptions;
        private bool _disposed = false;

        public string ProtocolName => "MQTT";

        public MqttService(ILogger<MqttService> logger, IOptionsMonitor<MessageBrokerSetting> options)
        {
            _logger = logger;
            _options = options;
            InitializeMqttClient();
        }

        private void InitializeMqttClient()
        {
            var mqttFactory = new MqttFactory();
            _mqttClient = mqttFactory.CreateMqttClient();

            var settings = _options.CurrentValue.MQTT;
            if (settings != null)
            {
                var optionsBuilder = new MqttClientOptionsBuilder()
                    .WithTcpServer(settings.BrokerHost, settings.BrokerPort)
                    .WithClientId(settings.ClientId ?? $"CSV2CFX_{Guid.NewGuid():N}")
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(settings.KeepAliveInterval))
                    .WithTimeout(TimeSpan.FromSeconds(settings.ConnectionTimeout))
                    .WithCleanSession();

                if (!string.IsNullOrEmpty(settings.Username) && !string.IsNullOrEmpty(settings.Password))
                {
                    optionsBuilder.WithCredentials(settings.Username, settings.Password);
                }

                if (settings.UseTls)
                {
                    optionsBuilder.WithTls();
                }

                _mqttOptions = optionsBuilder.Build();
            }
        }

        private async Task EnsureConnectedAsync()
        {
            if (_mqttClient == null || _mqttOptions == null)
            {
                throw new InvalidOperationException("MQTT client is not initialized");
            }

            if (!_mqttClient.IsConnected)
            {
                var result = await _mqttClient.ConnectAsync(_mqttOptions);
                if (result.ResultCode != MqttClientConnectResultCode.Success)
                {
                    throw new InvalidOperationException($"Failed to connect to MQTT broker: {result.ReasonString}");
                }
                _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}",
                    _options.CurrentValue.MQTT?.BrokerHost, _options.CurrentValue.MQTT?.BrokerPort);
            }
        }

        public async Task PublishMessageAsync(string topic, string message)
        {
            await EnsureConnectedAsync();

            var settings = _options.CurrentValue.MQTT;
            var qos = settings?.QualityOfService switch
            {
                0 => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce,
                1 => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce,
                2 => MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce,
                _ => MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce
            };

            var mqttMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(message))
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient!.PublishAsync(mqttMessage);
            _logger.LogInformation("Message published to MQTT topic '{Topic}' with QoS {QoS}.", topic, qos);
        }

        public async Task CreateTopicAsync(string topicName)
        {
            // MQTT doesn't require explicit topic creation
            // Topics are created automatically when first message is published
            _logger.LogInformation("MQTT topic '{TopicName}' will be created automatically on first publish.", topicName);
            await Task.CompletedTask;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                await EnsureConnectedAsync();
                return _mqttClient?.IsConnected == true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to test MQTT connection");
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    if (_mqttClient?.IsConnected == true)
                    {
                        _mqttClient.DisconnectAsync().Wait(5000);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error occurred while disconnecting MQTT client");
                }
                finally
                {
                    _mqttClient?.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}
