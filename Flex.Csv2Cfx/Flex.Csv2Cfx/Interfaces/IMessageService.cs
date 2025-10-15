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
        MessageProtocol CurrentProtocol { get; set; }

        Task<bool> ConnectAsync();
        Task<PublishResult> PublishAmqpMessageAsync(string topic, string message);
        Task<PublishResult> PublishMqttMessageAsync(string topic, string message);
        Task<PublishResult> PublishBothAsync(string topic, string message);
        Task<PublishResult> PublishMessageAsync(string topic, string message); // 新增：根据当前协议发送
        Task<List<Message>> GetRecentSentMessagesAsync(int count = 50);
        void Disconnect();
        void SetProtocol(MessageProtocol protocol);
    }
}
