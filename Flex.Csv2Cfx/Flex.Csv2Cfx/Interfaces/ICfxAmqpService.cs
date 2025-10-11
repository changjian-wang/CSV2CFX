using CFX.Production.Processing;
using CFX.Structures;
using System;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Interfaces
{
    public interface ICfxAmqpService : IDisposable
    {
        bool IsConnected { get; }
        bool IsOpen { get; }

        /// <summary>
        /// 打开 AMQP 终端连接（带 Routing Key）
        /// </summary>
        Task<(bool success, string message)> OpenEndpointTopicAsync();

        /// <summary>
        /// 打开 AMQP 终端连接（不带 Routing Key）
        /// </summary>
        Task<(bool success, string message)> OpenEndpointAsync();

        /// <summary>
        /// 发送心跳消息
        /// </summary>
        Task<(bool success, string json)> SendHeartbeatAsync(
            string[] activeFaults,
            string[] activeRecipes,
            string heartbeatFrequency);

        /// <summary>
        /// 发送工作开始消息
        /// </summary>
        Task<(bool success, string json)> SendWorkStartedAsync(
            string transactionId,
            int lane,
            string primaryIdentifier);

        /// <summary>
        /// 发送工作完成消息
        /// </summary>
        Task<(bool success, string json)> SendWorkCompletedAsync(
            string transactionId,
            int result,
            string primaryIdentifier);

        /// <summary>
        /// 发送单元处理消息
        /// </summary>
        Task<(bool success, string json)> SendUnitsProcessedAsync(
            string transactionId,
            int overallResult,
            string recipeName,
            ProcessData commonProcessData);

        /// <summary>
        /// 发送故障发生消息
        /// </summary>
        Task<(bool success, string json)> SendFaultOccurredAsync(Fault fault);

        /// <summary>
        /// 发送故障清除消息
        /// </summary>
        Task<(bool success, string json)> SendFaultClearedAsync(Fault fault, Operator @operator);

        /// <summary>
        /// 发送机器状态变更消息
        /// </summary>
        Task<(bool success, string json)> SendStationStateChangedAsync(
            int oldState,
            string oldStateDuration,
            int newState,
            string relatedFault);

        /// <summary>
        /// 发送通用 JSON 数据
        /// </summary>
        Task<(bool success, string json)> SendCommonJsonDataAsync(string jsonData);

        /// <summary>
        /// 重新连接 - 发送工作开始消息
        /// </summary>
        Task<(bool success, string json)> ReconnectWorkStartedAsync(
            string timeStamp,
            string transactionId,
            int lane,
            string primaryIdentifier);

        /// <summary>
        /// 重新连接 - 发送工作完成消息
        /// </summary>
        Task<(bool success, string json)> ReconnectWorkCompletedAsync(
            string timeStamp,
            string transactionId,
            int result,
            string primaryIdentifier);

        /// <summary>
        /// 重新连接 - 发送单元处理消息
        /// </summary>
        Task<(bool success, string json)> ReconnectUnitsProcessedAsync(
            string timeStamp,
            string transactionId,
            int overallResult,
            string recipeName,
            ProcessData commonProcessData);

        /// <summary>
        /// 重新连接 - 发送机器状态变更消息
        /// </summary>
        Task<(bool success, string json)> ReconnectStationStateChangedAsync(
            string timeStamp,
            int oldState,
            string oldStateDuration,
            int newState,
            string relatedFault);

        /// <summary>
        /// 关闭终端
        /// </summary>
        void CloseEndpoint();
    }
}