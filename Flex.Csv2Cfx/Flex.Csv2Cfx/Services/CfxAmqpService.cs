using CFX;
using CFX.Production;
using CFX.Production.Processing;
using CFX.ResourcePerformance;
using CFX.Structures;
using Flex.Csv2Cfx.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RabbitMQ.Client; // ✅ 添加 RabbitMQ.Client 引用
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using wuz_ipc_cfx_test;

namespace Flex.Csv2Cfx.Services
{
    public class CfxAmqpService : ICfxAmqpService
    {
        private readonly ILogger<CfxAmqpService> _logger;
        private readonly IConfigurationService _configurationService;

        private dllHelper? _dllHelper;
        private bool _isBeat = false;
        private List<string>? _beatMsg = null;
        private const string EXCHANGE_NAME = "amq.topic";

        // ✅ 添加队列管理连接
        private IConnection? _queueManagementConnection;
        private IChannel? _queueManagementChannel;

        private Metadata _metadata = new Metadata();
        private string _uniqueId = "";
        private string _myCFXHandle = "";
        private string _cfxEventBroker = "";
        private string _cfxBrokerExchange = "";
        private string _routingKey = "";

        // ✅ 定义统一的队列主题
        //private readonly List<string> _queueTopics = new List<string>
        //{
        //    "flex/heartbeat",
        //    "flex/works",
        //    "flex/states"
        //};

        private readonly List<string> _queueTopics = new List<string>
        {
            "ipc-cfx"
        };

        public CfxAmqpService(
            ILogger<CfxAmqpService> logger,
            IConfigurationService configurationService)
        {
            _logger = logger;
            _configurationService = configurationService;
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            try
            {
                var settings = _configurationService.GetSettings();
                var machineMetadata = settings.MachineSettings.Metadata;
                var rabbitMq = settings.RabbitMqSettings;
                var cfx = settings.MachineSettings.Cfx;

                // 创建 Metadata
                _metadata = new Metadata
                {
                    building = machineMetadata.Building ?? "",
                    device = machineMetadata.Device ?? "",
                    area_name = machineMetadata.AreaName ?? "",
                    org = machineMetadata.Organization ?? "",
                    line_name = machineMetadata.LineName ?? "",
                    site_name = machineMetadata.SiteName ?? "",
                    station_name = machineMetadata.StationName ?? "",
                    Process_type = machineMetadata.ProcessType ?? "",
                    machine_name = machineMetadata.MachineName ?? "",
                    Created_by = string.IsNullOrEmpty(machineMetadata.CreatedBy) ? "GA" : machineMetadata.CreatedBy,
                    previus_status = "Running",
                    time_last_status = "00:00:00"
                };

                _uniqueId = cfx.UniqueId;

                _cfxEventBroker = $"amqp://{rabbitMq.Username}:{rabbitMq.Password}@{rabbitMq.HostName}:5672";
                _cfxBrokerExchange = EXCHANGE_NAME;
                _routingKey = _uniqueId;
                _myCFXHandle = _routingKey;

                _logger.LogInformation("AMQP 配置加载完成:");
                _logger.LogInformation("  - Broker: {Broker}", _cfxEventBroker);
                _logger.LogInformation("  - Exchange: {Exchange}", _cfxBrokerExchange);
                _logger.LogInformation("  - RoutingKey: {RoutingKey}", _routingKey);
                _logger.LogInformation("  - CFXHandle: {CFXHandle}", _myCFXHandle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载 AMQP 配置失败");
                throw;
            }
        }

        public bool IsConnected => _isBeat;

        public bool IsOpen
        {
            get
            {
                if (_dllHelper == null)
                {
                    return false;
                }
                return _isBeat;
            }
        }

        /// <summary>
        /// 打开 AMQP 终端连接（带 Routing Key）并创建统一队列
        /// </summary>
        public async Task<(bool success, string message)> OpenEndpointTopicAsync()
        {
            return await Task.Run(async () =>
            {
                CloseEndpoint();

                try
                {
                    _logger.LogDebug("创建新的 dllHelper 实例");

                    _dllHelper = new dllHelper(
                        _cfxEventBroker,
                        _cfxBrokerExchange,
                        _routingKey,
                        _myCFXHandle,
                        _metadata,
                        _uniqueId);

                    string msg;
                    _dllHelper.OpenEndpointTopic(out msg);

                    _logger.LogDebug("OpenEndpointTopic 返回: {Message}", msg);

                    if (msg.StartsWith("ERROR"))
                    {
                        _logger.LogError("AMQP 连接失败: {Message}", msg);
                        CloseEndpoint();
                        return (false, msg);
                    }
                    else
                    {
                        _isBeat = true;
                        _beatMsg = null;
                        _logger.LogInformation("AMQP 终端连接成功 (Topic)");

                        // ✅ 创建统一队列
                        var queueResult = await CreateUnifiedQueuesAsync();
                        if (!queueResult.success)
                        {
                            _logger.LogWarning("队列创建失败，但 CFX 连接仍然可用: {Message}", queueResult.message);
                        }

                        return (true, msg);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "打开 AMQP 终端 (Topic) 时发生错误");
                    CloseEndpoint();
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 打开 AMQP 终端连接（不带 Routing Key）并创建统一队列
        /// </summary>
        public async Task<(bool success, string message)> OpenEndpointAsync()
        {
            return await Task.Run(async () =>
            {
                CloseEndpoint();

                try
                {
                    _dllHelper = new dllHelper(
                        _cfxEventBroker,
                        _cfxBrokerExchange,
                        _routingKey,
                        _myCFXHandle,
                        _metadata,
                        _uniqueId);

                    string msg;
                    _dllHelper.OpenEndpoint(out msg);

                    if (msg.StartsWith("ERROR"))
                    {
                        _logger.LogError("AMQP 连接失败: {Message}", msg);
                        CloseEndpoint();
                        return (false, msg);
                    }
                    else
                    {
                        _isBeat = true;
                        _beatMsg = null;
                        _logger.LogInformation("AMQP 终端连接成功");

                        // ✅ 创建统一队列
                        var queueResult = await CreateUnifiedQueuesAsync();
                        if (!queueResult.success)
                        {
                            _logger.LogWarning("队列创建失败，但 CFX 连接仍然可用: {Message}", queueResult.message);
                        }

                        return (true, msg);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "打开 AMQP 终端时发生错误");
                    CloseEndpoint();
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 创建统一队列（与 MQTT 兼容）
        /// </summary>
        private async Task<(bool success, string message)> CreateUnifiedQueuesAsync()
        {
            try
            {
                var settings = _configurationService.GetSettings();
                var rabbitMq = settings.RabbitMqSettings;

                _logger.LogInformation("开始创建统一队列...");

                // 创建队列管理连接
                var factory = new ConnectionFactory
                {
                    HostName = rabbitMq.HostName,
                    UserName = rabbitMq.Username,
                    Password = rabbitMq.Password,
                    VirtualHost = rabbitMq.VirtualHost,
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(30),
                    RequestedHeartbeat = TimeSpan.FromSeconds(60)
                };

                _queueManagementConnection = await factory.CreateConnectionAsync();
                _queueManagementChannel = await _queueManagementConnection.CreateChannelAsync();

                _logger.LogInformation("队列管理连接已建立");

                int successCount = 0;
                int totalQueues = _queueTopics.Count;

                foreach (var topic in _queueTopics)
                {
                    try
                    {
                        // string queueName = $"unified.queue.{topic.Replace("/", ".")}";
                        string queueName = topic;

                        // 声明队列（幂等操作，如果已存在则不会报错）
                        await _queueManagementChannel.QueueDeclareAsync(
                            queue: queueName,
                            durable: true,
                            exclusive: false,
                            autoDelete: false,
                            arguments: null);

                        _logger.LogDebug("队列已声明: {QueueName}", queueName);

                        // 绑定队列到 amq.topic 交换机 - 使用 "/" 格式（标准 AMQP routing key）
                        await _queueManagementChannel.QueueBindAsync(
                            queue: queueName,
                            exchange: EXCHANGE_NAME,
                            routingKey: _uniqueId,
                            arguments: null);

                        _logger.LogDebug("队列已绑定 (AMQP 格式): {QueueName} -> {RoutingKey}", queueName, topic);

                        // 绑定队列到 amq.topic 交换机 - 使用 "." 格式（MQTT 兼容）
                        await _queueManagementChannel.QueueBindAsync(
                            queue: queueName,
                            exchange: EXCHANGE_NAME,
                            routingKey: _uniqueId,
                            arguments: null);

                        _logger.LogDebug("队列已绑定 (MQTT 格式): {QueueName} -> {RoutingKey}", queueName, _uniqueId);

                        successCount++;
                        _logger.LogInformation("✅ 队列创建并绑定成功: {QueueName} (绑定: {Topic1}, {Topic2})",
                            queueName, topic, _uniqueId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "创建队列失败: {Topic}", topic);
                    }
                }

                if (successCount == totalQueues)
                {
                    _logger.LogInformation("所有统一队列创建成功 ({SuccessCount}/{TotalCount})", successCount, totalQueues);
                    return (true, "所有队列创建成功");
                }
                else
                {
                    _logger.LogWarning("部分队列创建失败 ({SuccessCount}/{TotalCount})", successCount, totalQueues);
                    return (false, $"部分队列创建失败 ({successCount}/{totalQueues})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建统一队列时发生错误");
                return (false, $"队列创建失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取连接错误信息
        /// </summary>
        private string GetConnectionErrors()
        {
            if (_beatMsg != null && _beatMsg.Count > 0)
            {
                return string.Join(", ", _beatMsg);
            }
            return "未连接";
        }

        /// <summary>
        /// 发送通用 JSON 数据
        /// </summary>
        public async Task<(bool success, string json)> SendCommonJsonDataAsync(string jsonData)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    _logger.LogWarning("AMQP 未连接，无法发送消息: {Error}", errorMsg);
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.sendCommonJsonData(jsonData, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogDebug("通用 JSON 数据已发送");
                    }
                    else
                    {
                        _logger.LogWarning("发送通用 JSON 数据失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送通用 JSON 数据时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 发送心跳消息
        /// </summary>
        public async Task<(bool success, string json)> SendHeartbeatAsync(
            string[] activeFaults,
            string[] activeRecipes,
            string heartbeatFrequency)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    _logger.LogWarning("AMQP 未连接，无法发送心跳: {Error}", errorMsg);
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.sendHeartbeat(activeFaults, activeRecipes, heartbeatFrequency, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogDebug("心跳消息已发送");
                    }
                    else
                    {
                        _logger.LogWarning("发送心跳消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送心跳消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        // ✅ 以下方法保持不变（SendWorkStartedAsync, SendWorkCompletedAsync 等）
        // 为了简洁，这里省略，使用之前提供的完整实现

        /// <summary>
        /// 发送工作开始消息
        /// </summary>
        public async Task<(bool success, string json)> SendWorkStartedAsync(
            string transactionId,
            int lane,
            string primaryIdentifier)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.sendWorkStarted(transactionId, lane, primaryIdentifier, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("工作开始消息已发送: TransactionID={TransactionId}", transactionId);
                    }
                    else
                    {
                        _logger.LogWarning("发送工作开始消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送工作开始消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 发送工作完成消息
        /// </summary>
        public async Task<(bool success, string json)> SendWorkCompletedAsync(
            string transactionId,
            int result,
            string primaryIdentifier)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.sendWorkCompleted(transactionId, result, primaryIdentifier, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("工作完成消息已发送: TransactionID={TransactionId}", transactionId);
                    }
                    else
                    {
                        _logger.LogWarning("发送工作完成消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送工作完成消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 发送单元处理消息
        /// </summary>
        public async Task<(bool success, string json)> SendUnitsProcessedAsync(
            string transactionId,
            int overallResult,
            string recipeName,
            ProcessData commonProcessData)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.sendUnitsProcessed(transactionId, overallResult, recipeName, commonProcessData, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("单元处理消息已发送: TransactionID={TransactionId}", transactionId);
                    }
                    else
                    {
                        _logger.LogWarning("发送单元处理消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送单元处理消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 发送故障发生消息
        /// </summary>
        public async Task<(bool success, string json)> SendFaultOccurredAsync(Fault fault)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.sendFaultOccurred(fault, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("故障发生消息已发送: FaultCode={FaultCode}", fault.FaultCode);
                    }
                    else
                    {
                        _logger.LogWarning("发送故障发生消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送故障发生消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 发送故障清除消息
        /// </summary>
        public async Task<(bool success, string json)> SendFaultClearedAsync(Fault fault, Operator @operator)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.sendFaultCleared(fault, @operator, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("故障清除消息已发送: FaultOccurrenceId={FaultOccurrenceId}", fault.FaultOccurrenceId);
                    }
                    else
                    {
                        _logger.LogWarning("发送故障清除消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送故障清除消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 发送机器状态变更消息
        /// </summary>
        public async Task<(bool success, string json)> SendStationStateChangedAsync(
            int oldState,
            string oldStateDuration,
            int newState,
            string relatedFault)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.sendStationStateChanged(oldState, oldStateDuration, newState, relatedFault, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("机器状态变更消息已发送: {OldState} -> {NewState}",
                            (ResourceState)oldState, (ResourceState)newState);
                    }
                    else
                    {
                        _logger.LogWarning("发送机器状态变更消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送机器状态变更消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 重新连接 - 发送工作开始消息
        /// </summary>
        public async Task<(bool success, string json)> ReconnectWorkStartedAsync(
            string timeStamp,
            string transactionId,
            int lane,
            string primaryIdentifier)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.reconnectWorkStarted(timeStamp, transactionId, lane, primaryIdentifier, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("重新连接 - 工作开始消息已发送: TransactionID={TransactionId}", transactionId);
                    }
                    else
                    {
                        _logger.LogWarning("重新连接 - 发送工作开始消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新连接 - 发送工作开始消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 重新连接 - 发送工作完成消息
        /// </summary>
        public async Task<(bool success, string json)> ReconnectWorkCompletedAsync(
            string timeStamp,
            string transactionId,
            int result,
            string primaryIdentifier)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.reconnectWorkCompleted(timeStamp, transactionId, result, primaryIdentifier, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("重新连接 - 工作完成消息已发送: TransactionID={TransactionId}", transactionId);
                    }
                    else
                    {
                        _logger.LogWarning("重新连接 - 发送工作完成消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新连接 - 发送工作完成消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 重新连接 - 发送单元处理消息
        /// </summary>
        public async Task<(bool success, string json)> ReconnectUnitsProcessedAsync(
            string timeStamp,
            string transactionId,
            int overallResult,
            string recipeName,
            ProcessData commonProcessData)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.reconnectUnitsProcessed(timeStamp, transactionId, overallResult, recipeName, commonProcessData, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("重新连接 - 单元处理消息已发送: TransactionID={TransactionId}", transactionId);
                    }
                    else
                    {
                        _logger.LogWarning("重新连接 - 发送单元处理消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新连接 - 发送单元处理消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 重新连接 - 发送机器状态变更消息
        /// </summary>
        public async Task<(bool success, string json)> ReconnectStationStateChangedAsync(
            string timeStamp,
            int oldState,
            string oldStateDuration,
            int newState,
            string relatedFault)
        {
            return await Task.Run(() =>
            {
                if (_dllHelper == null || !IsOpen)
                {
                    string errorMsg = GetConnectionErrors();
                    return (false, errorMsg);
                }

                try
                {
                    string jsonSend;
                    bool success = _dllHelper.reconnectStationStateChanged(timeStamp, oldState, oldStateDuration, newState, relatedFault, out jsonSend);
                    _isBeat = success;

                    if (success)
                    {
                        _logger.LogInformation("重新连接 - 机器状态变更消息已发送");
                    }
                    else
                    {
                        _logger.LogWarning("重新连接 - 发送机器状态变更消息失败: {Error}", jsonSend);
                    }

                    return (success, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新连接 - 发送机器状态变更消息时发生异常");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 关闭终端和队列管理连接
        /// </summary>
        public void CloseEndpoint()
        {
            try
            {
                // 关闭 dllHelper
                if (_dllHelper != null)
                {
                    _dllHelper.CloseEndpoint();
                    _dllHelper = null;
                    _isBeat = false;
                    _beatMsg = null;
                    _logger.LogInformation("AMQP 终端已关闭");
                }

                // ✅ 关闭队列管理连接
                if (_queueManagementChannel != null)
                {
                    _queueManagementChannel.CloseAsync().Wait(TimeSpan.FromSeconds(5));
                    _queueManagementChannel.Dispose();
                    _queueManagementChannel = null;
                    _logger.LogDebug("队列管理通道已关闭");
                }

                if (_queueManagementConnection != null)
                {
                    _queueManagementConnection.CloseAsync().Wait(TimeSpan.FromSeconds(5));
                    _queueManagementConnection.Dispose();
                    _queueManagementConnection = null;
                    _logger.LogDebug("队列管理连接已关闭");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭 AMQP 终端时发生错误");
            }
        }

        public void Dispose()
        {
            CloseEndpoint();
        }
    }
}