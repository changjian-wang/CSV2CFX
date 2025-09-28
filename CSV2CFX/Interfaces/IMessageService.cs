using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSV2CFX.Interfaces
{
    public interface IMessageService
    {
        Task PublishMessageAsync(string topic, string message);
        Task CreateTopicAsync(string topicName);
        Task<bool> TestConnectionAsync();
        Task InitializeAsync();
    }
}
