using CFX;
using CFX.Production;
using CFX.Production.Processing;
using CFX.ResourcePerformance;
using CFX.Structures;
using CFX.Transport;
using Flex.Csv2Cfx.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Services
{
    public class CfxAmqpService : ICfxAmqpService
    {
        private readonly ILogger<CfxAmqpService> _logger;
        private readonly IConfigurationService _configurationService;

        private AmqpCFXEndpoint? _myEndpoint;
        private bool _isBeat = false;
        private List<string>? _beatMsg = null;
        private const string EXCHANGE_NAME = "amq.topic";

        private Metadata _metadata = new Metadata();
        private string _uniqueId = "";
        private string _myCFXHandle = "";
        private string _cfxEventBroker = "";
        private string _cfxBrokerExchange = "";
        private string _routingKey = "";

        public CfxAmqpService(
            ILogger<CfxAmqpService> logger,
            IConfigurationService configurationService)
        {
            _logger = logger;
            _configurationService = configurationService;

            // 从配置加载
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            var settings = _configurationService.GetSettings();
            var machineMetadata = settings.MachineSettings.Metadata;
            var rabbitMq = settings.RabbitMqSettings;
            var cfx = settings.MachineSettings.Cfx;

            // 创建 Metadata
            _metadata = new Metadata
            {
                building = machineMetadata.Building,
                device = machineMetadata.Device,
                area_name = machineMetadata.AreaName,
                org = machineMetadata.Organization,
                line_name = machineMetadata.LineName,
                site_name = machineMetadata.SiteName,
                station_name = machineMetadata.StationName,
                Process_type = machineMetadata.ProcessType,
                machine_name = machineMetadata.MachineName,
                previus_status = "Running",
                time_last_status = "00:00:00"
            };

            _uniqueId = cfx.UniqueId;
            //"AmqpBrokerUri": "amqp://guest:guest@localhost:5672"
            _cfxEventBroker = $"amqp://{rabbitMq.Username}:{rabbitMq.Password}@{rabbitMq.HostName}:5672";
            _cfxBrokerExchange = EXCHANGE_NAME;
            _routingKey = _uniqueId ?? string.Empty;
            _myCFXHandle = _routingKey; // 使用 routing key 作为 CFX Handle
        }

        public bool IsConnected => _isBeat;

        public bool IsOpen
        {
            get
            {
                if (_myEndpoint == null)
                {
                    return false;
                }
                return _myEndpoint.IsOpen;
            }
        }

        /// <summary>
        /// 打开 AMQP 终端连接（带 Routing Key）
        /// </summary>
        public async Task<(bool success, string message)> OpenEndpointTopicAsync()
        {
            return await Task.Run(() =>
            {
                CloseEndpoint();

                try
                {
                    _myEndpoint = new AmqpCFXEndpoint(_metadata, _uniqueId);
                    _myEndpoint.Open(_myCFXHandle);

                    Exception? error = null;
                    if (!_myEndpoint.TestPublishChannel(new Uri(_cfxEventBroker), _cfxBrokerExchange, out error))
                    {
                        var msg = $"ERROR: Unable to connect to broker {_cfxEventBroker} Exchange {_cfxBrokerExchange}\nERROR MESSAGE: {error?.Message}";
                        _logger.LogError(msg);
                        CloseEndpoint();
                        return (false, msg);
                    }
                    else
                    {
                        _myEndpoint.AddPublishChannel(new Uri(_cfxEventBroker), _cfxBrokerExchange, _routingKey);
                        _myEndpoint.OnConnectionEvent -= State_OnConnectionEvent;
                        _myEndpoint.OnConnectionEvent += State_OnConnectionEvent;

                        _isBeat = true;
                        _beatMsg = null;

                        _logger.LogInformation("AMQP 终端连接成功: {Broker}, Exchange: {Exchange}, RoutingKey: {RoutingKey}",
                            _cfxEventBroker, _cfxBrokerExchange, _routingKey);

                        return (true, "Open Success");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "打开 AMQP 终端时发生错误");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 打开 AMQP 终端连接（不带 Routing Key）
        /// </summary>
        public async Task<(bool success, string message)> OpenEndpointAsync()
        {
            return await Task.Run(() =>
            {
                CloseEndpoint();

                try
                {
                    _myEndpoint = new AmqpCFXEndpoint(_metadata, _uniqueId);
                    _myEndpoint.Open(_myCFXHandle);

                    Exception? error = null;
                    if (!_myEndpoint.TestPublishChannel(new Uri(_cfxEventBroker), _cfxBrokerExchange, out error))
                    {
                        var msg = $"ERROR: Unable to connect to broker {_cfxEventBroker} Exchange {_cfxBrokerExchange}\nERROR MESSAGE: {error?.Message}";
                        _logger.LogError(msg);
                        CloseEndpoint();
                        return (false, msg);
                    }
                    else
                    {
                        _myEndpoint.AddPublishChannel(new Uri(_cfxEventBroker), _cfxBrokerExchange);
                        _myEndpoint.OnConnectionEvent -= State_OnConnectionEvent;
                        _myEndpoint.OnConnectionEvent += State_OnConnectionEvent;

                        _isBeat = true;
                        _beatMsg = null;

                        _logger.LogInformation("AMQP 终端连接成功: {Broker}, Exchange: {Exchange}",
                            _cfxEventBroker, _cfxBrokerExchange);

                        return (true, "Open Success");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "打开 AMQP 终端时发生错误");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 连接事件处理
        /// </summary>
        private void State_OnConnectionEvent(
            ConnectionEvent eventType,
            Uri uri,
            int spoolSize,
            string errorInformation,
            Exception? errorException = null)
        {
            if (eventType == ConnectionEvent.ConnectionEstablished)
            {
                _isBeat = true;
                _beatMsg = null;
                _logger.LogInformation("AMQP 连接已建立: {Uri}", uri);
            }

            if (errorException != null)
            {
                _isBeat = false;

                if (_beatMsg == null)
                {
                    _beatMsg = new List<string>();
                }

                if (_beatMsg.Count < 2)
                {
                    _beatMsg.Add(errorInformation);
                }

                _logger.LogError(errorException, "AMQP 连接错误: {ErrorInfo}", errorInformation);
                CloseEndpoint();
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
        /// 将 DateTime 字符串转换为 DateTime
        /// </summary>
        private DateTime GetDateTimeFromStr(string inputDateTime)
        {
            long ticks = DateTime.Parse(inputDateTime).Ticks;
            return new DateTime(ticks, DateTimeKind.Local);
        }

        /// <summary>
        /// 发送通用 JSON 数据
        /// </summary>
        public async Task<(bool success, string json)> SendCommonJsonDataAsync(string jsonData)
        {
            return await Task.Run(() =>
            {
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    CFXEnvelope envelope = CFXEnvelope.FromJson(jsonData);
                    _myEndpoint!.Publish(envelope);
                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("通用 JSON 消息已发送");
                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送通用 JSON 消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    List<Fault> faultList = new List<Fault>();
                    foreach (string faultJson in activeFaults)
                    {
                        Fault fault = JsonConvert.DeserializeObject<Fault>(faultJson);
                        faultList.Add(fault);
                    }

                    List<ActiveRecipe> recipeList = new List<ActiveRecipe>();
                    foreach (string recipeJson in activeRecipes)
                    {
                        ActiveRecipe recipe = JsonConvert.DeserializeObject<ActiveRecipe>(recipeJson);
                        recipeList.Add(recipe);
                    }

                    TimeSpan frequency = TimeSpan.Parse(heartbeatFrequency);

                    CFXEnvelope envelope = new CFXEnvelope(new Heartbeat(_metadata)
                    {
                        ActiveFaults = faultList,
                        ActiveRecipes = recipeList,
                        CFXHandle = _myCFXHandle,
                        HeartbeatFrequency = frequency
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;

                    _myEndpoint!.HeartbeatFrequency = frequency;
                    _myEndpoint.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogDebug("心跳消息已发送");
                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送心跳消息时发生错误");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    Guid txId = Guid.Parse(transactionId);

                    CFXEnvelope envelope = new CFXEnvelope(new WorkStarted
                    {
                        Lane = lane,
                        PrimaryIdentifier = primaryIdentifier,
                        HermesIdentifier = null,
                        TransactionID = txId,
                        UnitCount = null,
                        Metadata = _metadata
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("工作开始消息已发送: TransactionID={TransactionId}, Lane={Lane}",
                        transactionId, lane);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送工作开始消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    Guid txId = Guid.Parse(transactionId);

                    CFXEnvelope envelope = new CFXEnvelope(new WorkCompleted
                    {
                        PrimaryIdentifier = primaryIdentifier,
                        HermesIdentifier = null,
                        TransactionID = txId,
                        UnitCount = null,
                        Result = (WorkResult)result,
                        Metadata = _metadata
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("工作完成消息已发送: TransactionID={TransactionId}, Result={Result}",
                        transactionId, result);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送工作完成消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    Guid txId = Guid.Parse(transactionId);

                    CFXEnvelope envelope = new CFXEnvelope(new UnitsProcessed
                    {
                        OverallResult = (ProcessingResult)overallResult,
                        TransactionId = txId,
                        RecipeName = recipeName,
                        CommonProcessData = commonProcessData,
                        Metadata = _metadata
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("单元处理消息已发送: TransactionID={TransactionId}, Recipe={Recipe}",
                        transactionId, recipeName);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送单元处理消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    CFXEnvelope envelope = new CFXEnvelope(new FaultOccurred
                    {
                        Fault = fault,
                        Metadata = _metadata
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("故障发生消息已发送: FaultCode={FaultCode}", fault.FaultCode);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送故障发生消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    Guid faultOccurrenceId = fault.FaultOccurrenceId;

                    CFXEnvelope envelope = new CFXEnvelope(new FaultCleared
                    {
                        FaultOccurrenceId = faultOccurrenceId,
                        Operator = @operator,
                        Metadata = _metadata
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("故障清除消息已发送: FaultOccurrenceId={FaultOccurrenceId}",
                        faultOccurrenceId);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送故障清除消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    CFXEnvelope envelope = new CFXEnvelope(new StationStateChanged
                    {
                        OldState = (ResourceState)oldState,
                        OldStateDuration = TimeSpan.Parse(oldStateDuration),
                        NewState = (ResourceState)newState,
                        // RelatedFault 可以根据需要设置
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("机器状态变更消息已发送: {OldState} -> {NewState}",
                        oldState, newState);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "发送机器状态变更消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    Guid txId = Guid.Parse(transactionId);

                    CFXEnvelope envelope = new CFXEnvelope(new WorkStarted
                    {
                        Lane = lane,
                        PrimaryIdentifier = primaryIdentifier,
                        HermesIdentifier = null,
                        TransactionID = txId,
                        UnitCount = null,
                        Metadata = _metadata
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;
                    envelope.TimeStamp = GetDateTimeFromStr(timeStamp);

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("重新连接 - 工作开始消息已发送: TransactionID={TransactionId}", transactionId);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新连接 - 发送工作开始消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    Guid txId = Guid.Parse(transactionId);

                    CFXEnvelope envelope = new CFXEnvelope(new WorkCompleted
                    {
                        PrimaryIdentifier = primaryIdentifier,
                        HermesIdentifier = null,
                        TransactionID = txId,
                        UnitCount = null,
                        Result = (WorkResult)result,
                        Metadata = _metadata
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;
                    envelope.TimeStamp = GetDateTimeFromStr(timeStamp);

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("重新连接 - 工作完成消息已发送: TransactionID={TransactionId}", transactionId);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新连接 - 发送工作完成消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    Guid txId = Guid.Parse(transactionId);

                    CFXEnvelope envelope = new CFXEnvelope(new UnitsProcessed
                    {
                        OverallResult = (ProcessingResult)overallResult,
                        TransactionId = txId,
                        RecipeName = recipeName,
                        CommonProcessData = commonProcessData,
                        Metadata = _metadata
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;
                    envelope.TimeStamp = GetDateTimeFromStr(timeStamp);

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("重新连接 - 单元处理消息已发送: TransactionID={TransactionId}", transactionId);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新连接 - 发送单元处理消息时发生错误");
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
                string jsonSend = "";

                if (!IsOpen)
                {
                    jsonSend = GetConnectionErrors();
                    return (false, jsonSend);
                }

                try
                {
                    CFXEnvelope envelope = new CFXEnvelope(new StationStateChanged
                    {
                        OldState = (ResourceState)oldState,
                        OldStateDuration = TimeSpan.Parse(oldStateDuration),
                        NewState = (ResourceState)newState
                    });

                    envelope.Source = _uniqueId;
                    envelope.UniqueID = _uniqueId;
                    envelope.TimeStamp = GetDateTimeFromStr(timeStamp);

                    _myEndpoint!.Publish(envelope);

                    jsonSend = envelope.ToJson(formatted: true);

                    if (_beatMsg != null && _beatMsg.Count > 0)
                    {
                        jsonSend = string.Join(",", _beatMsg);
                    }

                    _logger.LogInformation("重新连接 - 机器状态变更消息已发送: {OldState} -> {NewState}",
                        oldState, newState);

                    return (_isBeat, jsonSend);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "重新连接 - 发送机器状态变更消息时发生错误");
                    return (false, $"错误: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 关闭终端
        /// </summary>
        public void CloseEndpoint()
        {
            try
            {
                if (_myEndpoint != null)
                {
                    _myEndpoint.OnConnectionEvent -= State_OnConnectionEvent;
                    _myEndpoint.Close();
                    _myEndpoint = null;
                    _logger.LogInformation("AMQP 终端已关闭");
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