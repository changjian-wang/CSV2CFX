using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSV2CFX.Interfaces
{
    public interface IMessagePublisher : IDisposable
    {
        /// <summary>
        /// 发布消息到指定的主题/交换机
        /// </summary>
        /// <param name="topic">主题名称（MQTT）或路由键（AMQP）</param>
        /// <param name="message">消息内容</param>
        /// <returns></returns>
        Task PublishMessageAsync(string topic, string message);

        /// <summary>
        /// 创建主题/交换机
        /// </summary>
        /// <param name="topicName">主题名称</param>
        /// <returns></returns>
        Task CreateTopicAsync(string topicName);

        /// <summary>
        /// 测试连接
        /// </summary>
        /// <returns></returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// 获取协议类型
        /// </summary>
        string ProtocolName { get; }
    }
}
