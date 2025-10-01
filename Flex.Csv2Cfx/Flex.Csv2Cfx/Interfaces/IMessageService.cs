using Flex.Csv2Cfx.Models;
using Flex.Csv2Cfx.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Interfaces
{
    public interface IMessageService : IDisposable
    {
        bool IsConnected { get; }
        IReadOnlyList<Message> SentMessages { get; }

        Task<bool> ConnectAsync();
        Task<PublishResult> PublishAmqpMessageAsync(string exchange, string routingKey, string message);
        Task<PublishResult> PublishMqttMessageAsync(string topic, string message);
        Task<PublishResult> PublishBothAsync(string amqpRoutingKey, string mqttTopic, string message);
        Task<List<Message>> GetRecentSentMessagesAsync(int count = 50);
        void Disconnect();
    }
}
