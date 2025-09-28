using CSV2CFX.AppSettings;
using CSV2CFX.Extensions;
using CSV2CFX.Interfaces;
using CSV2CFX.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CSV2CFX.Services
{
    public class MachineService : IMachineService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<MachineInfoSetting> _machineInfoOptions;
        private readonly IOptionsMonitor<MachineMetadataSetting> _machineMetadataOptions;
        private readonly IOptionsMonitor<CsvFilePathSetting> _csvFilePathOptions;
        private readonly IOptionsMonitor<RabbitMQPublisherSettings> _rabbitMQPublisherOptions;
        private readonly IRabbitMQService _rabbitMQService;
        private readonly List<IDisposable> _optionsChangeTokens = new();

        private const string QUEUE_SUFFIX = "queue";
        private const string EXCHANGE_SUFFIX = "exchange";
        private const string ROUTINGKEY_SUFFIX = "routing-key";

        private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public MachineService(
            ILogger<MachineService> logger,
            IOptionsMonitor<MachineInfoSetting> machineInfoOptions,
            IOptionsMonitor<MachineMetadataSetting> machineMetadataOptions,
            IOptionsMonitor<CsvFilePathSetting> csvFilePathOptions,
            IOptionsMonitor<RabbitMQPublisherSettings> rabbitMQPublisherOptions,
            IRabbitMQService rabbitMQService)
        {
            _logger = logger;
            _machineInfoOptions = machineInfoOptions;
            _machineMetadataOptions = machineMetadataOptions;
            _csvFilePathOptions = csvFilePathOptions;
            _rabbitMQPublisherOptions = rabbitMQPublisherOptions;
            _rabbitMQService = rabbitMQService;

            // 设置配置更改监听
            SetupConfigurationChangeHandlers();
        }

        private void SetupConfigurationChangeHandlers()
        {
            // 监听机器信息配置更改
            _optionsChangeTokens.Add(
                _machineInfoOptions.OnChange(OnMachineInfoChanged)
            );

            // 监听机器元数据配置更改
            _optionsChangeTokens.Add(
                _machineMetadataOptions.OnChange(OnMachineMetadataChanged)
            );

            // 监听CSV文件路径配置更改
            _optionsChangeTokens.Add(
                _csvFilePathOptions.OnChange(OnCsvFilePathChanged)
            );

            // 监听RabbitMQ发布者配置更改
            _optionsChangeTokens.Add(
                _rabbitMQPublisherOptions.OnChange(OnRabbitMQPublisherChanged)
            );
        }

        private void OnMachineInfoChanged(MachineInfoSetting newValue)
        {
            _logger.LogInformation("机器信息配置已更新: UniqueId={UniqueId}, Version={Version}, HeartbeatFrequency={HeartbeatFrequency}",
                newValue.UniqueId, newValue.Version, newValue.HeartbeatFrequency);
        }

        private void OnMachineMetadataChanged(MachineMetadataSetting newValue)
        {
            _logger.LogInformation("机器元数据配置已更新: MachineName={MachineName}, StationName={StationName}",
                newValue.MachineName, newValue.StationName);
        }

        private void OnCsvFilePathChanged(CsvFilePathSetting newValue)
        {
            _logger.LogInformation("CSV文件路径配置已更新: ProductionPath={ProductionPath}, ProcessDataPath={ProcessDataPath}",
                newValue.ProductionInformationFilePath, newValue.ProcessDataFilesFilePath);
        }

        private void OnRabbitMQPublisherChanged(RabbitMQPublisherSettings newValue)
        {
            _logger.LogInformation("RabbitMQ发布者配置已更新: Prefix={Prefix}", newValue.Prefix);
        }

        /// <summary>
        /// Create RabbitMQ queues, exchanges, and bindings for CFX messages.
        /// </summary>
        /// <returns></returns>
        public async Task CreateRabbitmqAsync(string uniqueId)
        {
            var rabbitMQPublisherSettings = _rabbitMQPublisherOptions.CurrentValue;

            var keyValues = new Dictionary<string, string>
            {
                ["heartbeat"] = $"{rabbitMQPublisherSettings.Prefix}.heartbeat",
                ["workstarted"] = $"{rabbitMQPublisherSettings.Prefix}.workstarted",
                ["workcompleted"] = $"{rabbitMQPublisherSettings.Prefix}.workcompleted",
                ["unitsprocessed"] = $"{rabbitMQPublisherSettings.Prefix}.unitsprocessed",
                ["stationstatechanged"] = $"{rabbitMQPublisherSettings.Prefix}.stationstatechanged",
                ["faultoccurred"] = $"{rabbitMQPublisherSettings.Prefix}.faultoccurred",
                ["faultcleared"] = $"{rabbitMQPublisherSettings.Prefix}.faultcleared",
            };

            var exchangeName = $"{rabbitMQPublisherSettings.Prefix}.{EXCHANGE_SUFFIX}";
            await _rabbitMQService.CreateExchangeAsync(exchangeName);

            foreach (var item in keyValues)
            {
                var queueName = $"{item.Value}.{QUEUE_SUFFIX}".ToLower();
                var routingKey = $"{item.Value}.{ROUTINGKEY_SUFFIX}".ToLower();

                await _rabbitMQService.CreateQueueAsync(queueName);
                await _rabbitMQService.BindQueueAsync(queueName, exchangeName, routingKey);
            }
        }

        /// <summary>
        /// Publishes a heartbeat message to a RabbitMQ exchange, indicating the current status of the machine.
        /// </summary>
        /// <remarks>This method constructs a heartbeat message containing metadata about the machine,
        /// such as its location, name, and process type, along with other status information. The message is serialized
        /// to JSON and published to a RabbitMQ exchange. The method also includes a delay based on the configured
        /// heartbeat frequency to ensure periodic message publishing.</remarks>
        /// <param name="uniqueId">A unique identifier for the machine, used to construct the RabbitMQ exchange, queue, and routing key names.</param>
        /// <returns></returns>
        public async Task PublishHeartbeatAsync(string uniqueId)
        {
            var machineInfo = _machineInfoOptions.CurrentValue;
            var machineMetadata = _machineMetadataOptions.CurrentValue;
            var rabbitMQPublisher = _rabbitMQPublisherOptions.CurrentValue;

            var body = new Dictionary<string, dynamic?>
            {
                ["$type"] = $"{machineInfo.Heartbeat}, CFX",
                ["CFXHandle"] = Guid.NewGuid().ToString(),
                ["HeartbeatFrequency"] = machineInfo.HeartbeatFrequency,
                ["ActiveFaults"] = 0,
                ["ActiveRecipes"] = Array.Empty<object>(),
                ["Metadata"] = new Dictionary<string, string>
                {
                    ["building"] = machineMetadata.Building ?? "",
                    ["device"] = machineMetadata.Device ?? "",
                    ["area_name"] = machineMetadata.AreaName ?? "",
                    ["org"] = machineMetadata.Organization ?? "",
                    ["line_name"] = machineMetadata.LineName ?? "",
                    ["site_name"] = machineMetadata.SiteName ?? "",
                    ["station_name"] = machineMetadata.StationName ?? "",
                    ["Process_type"] = machineMetadata.ProcessType ?? "",
                    ["machine_name"] = machineMetadata.MachineName ?? "",
                    ["Created_by"] = machineMetadata.CreatedBy ?? "",
                }
            };

            var json = new CFXJsonModel
            {
                MessageName = machineInfo.Heartbeat,
                Version = machineInfo.Version,
                TimeStamp = DateTime.UtcNow.FormatDateTimeToIso8601(0),
                UniqueID = machineInfo.UniqueId,
                Source = machineInfo.UniqueId,
                Target = null,
                RequestID = Guid.NewGuid().ToString(),
                MessageBody = body
            };

            var exchangeName = $"{rabbitMQPublisher.Prefix}.{EXCHANGE_SUFFIX}";
            var routingKey = $"{rabbitMQPublisher.Prefix}.heartbeat.{ROUTINGKEY_SUFFIX}";
            var queueName = $"{rabbitMQPublisher.Prefix}.heartbeat.{QUEUE_SUFFIX}";
            var message = JsonSerializer.Serialize(json, _jsonSerializerOptions);

            await _rabbitMQService.PublishMessageAsync(exchangeName, routingKey, message);

            // 使用当前配置的心跳频率进行延迟
            var heartbeatFrequency = machineInfo.HeartbeatFrequency;
            await Task.Delay(heartbeatFrequency * 1000).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes a "Work Started" message for each production record in the specified CSV file.
        /// </summary>
        /// <remarks>This method reads production data from a CSV file located in the configured process
        /// data folder. If the file does not exist or the folder path is invalid, the method exits without performing
        /// any action.  For each production record in the file, the method constructs a message containing production
        /// details and metadata, and publishes it to a RabbitMQ exchange. The method ensures that the original file is
        /// backed up and deleted after processing.  Exceptions during message publishing are logged, and the file is
        /// deleted in the <c>finally</c> block to ensure cleanup.</remarks>
        /// <param name="uniqueId">A unique identifier used to construct the RabbitMQ exchange, queue, and routing key names.</param>
        /// <returns></returns>
        public async Task PublishWorkProcessAsync(string uniqueId)
        {
            var csvFilePaths = _csvFilePathOptions.CurrentValue;
            var filePath = csvFilePaths.ProductionInformationFilePath ?? "";
            var copyFilePath = $"{filePath}.backup.csv";

            if (!File.Exists(filePath) && !File.Exists(copyFilePath))
            {
                _logger.LogDebug("生产信息文件不存在: {FilePath}", filePath);
                return;
            }

            if (!File.Exists(copyFilePath))
            {
                File.Copy(filePath, copyFilePath, true);
                File.Delete(filePath);
                _logger.LogDebug("已创建备份文件: {CopyFilePath}", copyFilePath);
            }

            filePath = copyFilePath;

            var lines = await File.ReadAllLinesAsync(filePath);

            try
            {
                _logger.LogInformation("开始处理生产信息文件，共 {LineCount} 行数据", lines.Length - 1);

                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = line.Split(',');
                    if (columns.Length < 7) continue;

                    ProductionInfo production = new ProductionInfo
                    {
                        ProductModel = columns[0].Trim(),
                        SN = columns[1].Trim(),
                        PartNum = columns[2].Trim(),
                        CT = columns[3].Trim(),
                        Result = columns[4].Trim(),
                        StartTime = columns[5].Trim(),
                        EndTime = columns[6].Trim()
                    };

                    var transactionID = Guid.NewGuid().ToString();

                    // workstarted
                    await PublishWorkStartedAsync(transactionID, uniqueId, production).ConfigureAwait(false);

                    // unitsprocessed
                    await PublishUnitsProcessedAsync(transactionID, uniqueId, production).ConfigureAwait(false);

                    // workcompleted
                    await PublishWorkCompletedAsync(transactionID, uniqueId, production).ConfigureAwait(false);
                }

                _logger.LogInformation("生产信息文件处理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布WorkStarted消息时发生错误");
                return;
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogDebug("已删除备份文件: {FilePath}", filePath);
                }
                await Task.Delay(5000).ConfigureAwait(false);
            }
        }

        // workstarted
        private async Task PublishWorkStartedAsync(string transactionID, string uniqueId, ProductionInfo production)
        {
            var machineInfo = _machineInfoOptions.CurrentValue;
            var machineMetadata = _machineMetadataOptions.CurrentValue;
            var rabbitMQPublisher = _rabbitMQPublisherOptions.CurrentValue;

            var body = new Dictionary<string, dynamic?>
            {
                ["$type"] = $"{machineInfo.WorkStarted}, CFX",
                ["PrimaryIdentifier"] = production.SN,
                ["HermesIdentifier"] = null,
                ["TransactionID"] = transactionID,
                ["Line"] = 1,
                ["UnitCount"] = null,
                ["Units"] = Array.Empty<object>(),
                ["Metadata"] = CreateMetadataDictionary(machineMetadata)
            };

            var json = new CFXJsonModel
            {
                MessageName = machineInfo.WorkStarted,
                Version = machineInfo.Version,
                TimeStamp = Convert.ToDateTime(production.StartTime).FormatDateTimeToIso8601(8),
                UniqueID = machineInfo.UniqueId,
                Source = machineInfo.UniqueId,
                Target = null,
                RequestID = Guid.NewGuid().ToString(),
                MessageBody = body
            };

            var exchangeName = $"{rabbitMQPublisher.Prefix}.{EXCHANGE_SUFFIX}";
            var routingKey = $"{rabbitMQPublisher.Prefix}.workstarted.{ROUTINGKEY_SUFFIX}";
            var queueName = $"{rabbitMQPublisher.Prefix}.workstarted.{QUEUE_SUFFIX}";
            var message = JsonSerializer.Serialize(json, _jsonSerializerOptions);

            // workstarted
            await _rabbitMQService.PublishMessageAsync(exchangeName, routingKey, message);
        }

        // unitsprocessed
        private async Task PublishUnitsProcessedAsync(string transactionID, string uniqueId, ProductionInfo production)
        {
            var machineInfo = _machineInfoOptions.CurrentValue;
            var machineMetadata = _machineMetadataOptions.CurrentValue;
            var rabbitMQPublisher = _rabbitMQPublisherOptions.CurrentValue;
            var csvFilePaths = _csvFilePathOptions.CurrentValue;

            var directoryPath = csvFilePaths.ProcessDataFilesFilePath ?? "";
            if (!Directory.Exists(directoryPath))
            {
                _logger.LogWarning("过程数据文件夹不存在: {DirectoryPath}", directoryPath);
                return;
            }

            var files = Directory.GetFiles(directoryPath, "*.csv");
            var filePath = files.Where(s => Path.GetFileNameWithoutExtension(s).StartsWith(production.SN ?? "")).FirstOrDefault() ?? "";

            var copyFilePath = $"{filePath}.backup.csv";

            if (!File.Exists(filePath) && !File.Exists(copyFilePath))
            {
                _logger.LogDebug("未找到序列号 {SN} 对应的过程数据文件", production.SN);
                return;
            }

            if (!File.Exists(copyFilePath))
            {
                File.Copy(filePath, copyFilePath, true);
                File.Delete(filePath);
            }

            filePath = copyFilePath;

            try
            {
                var lines = await File.ReadAllLinesAsync(filePath, encoding: System.Text.Encoding.UTF8);
                var list = lines.Where(s => IsValidDateTime(s.Split(',')[0]));
                var personalizedUnits = new List<PersonalizedUnit>();
                var names = lines[1].Split(',');
                var count = 1;

                foreach (var item in list)
                {
                    var columns = item.Split(',');
                    if (columns.Length < 4) continue;

                    personalizedUnits.Add(new PersonalizedUnit
                    {
                        Name = $"{names[1]}{count}",
                        Unit = "Nm",
                        Value = Convert.ToDecimal(columns[1]),
                        Hilim = "",
                        Lolim = "",
                        Status = columns[3],
                        Rule = "",
                        Target = ""
                    });

                    personalizedUnits.Add(new PersonalizedUnit
                    {
                        Name = $"{names[2]}{count++}",
                        Unit = "degree",
                        Value = Convert.ToDecimal(columns[2]),
                        Hilim = "",
                        Lolim = "",
                        Status = columns[3],
                        Rule = "",
                        Target = ""
                    });
                }

                var body = new Dictionary<string, dynamic?>
                {
                    ["$type"] = $"CFX.Structures.SolderReflow.ReflowProcessData, CFX",
                    ["TransactionID"] = transactionID,
                    ["OverallResult"] = production.Result,
                    ["RecipeName"] = "RecipeName1",
                    ["CommonProcessData"] = new Dictionary<string, dynamic>
                    {
                        ["$type"] = "CFX.Structures.ProccessData, CFX",
                        ["PersonalizedUnits"] = personalizedUnits
                    },
                    ["Metadata"] = CreateMetadataDictionary(machineMetadata),
                    ["UnitProcessData"] = Array.Empty<object>()
                };

                var json = new Dictionary<string, dynamic?>
                {
                    ["MessageName"] = machineInfo.UnitsProcessed ?? "",
                    ["Version"] = machineInfo.Version ?? "",
                    ["TimeStamp"] = Convert.ToDateTime(production.EndTime).FormatDateTimeToIso8601(8),
                    ["UniqueID"] = machineInfo.UniqueId ?? "",
                    ["Source"] = machineInfo.UniqueId ?? "",
                    ["Target"] = null,
                    ["RequestID"] = null,
                    ["MessageBody"] = body
                };

                var exchangeName = $"{rabbitMQPublisher.Prefix}.{EXCHANGE_SUFFIX}";
                var routingKey = $"{rabbitMQPublisher.Prefix}.unitsprocessed.{ROUTINGKEY_SUFFIX}";
                var queueName = $"{rabbitMQPublisher.Prefix}.unitsprocessed.{QUEUE_SUFFIX}";
                var message = JsonSerializer.Serialize(json, _jsonSerializerOptions);

                // unitsprocessed
                await _rabbitMQService.PublishMessageAsync(exchangeName, routingKey, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布UnitsProcessed消息时发生错误，SN: {SN}", production.SN);
                return;
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                await Task.Delay(5000).ConfigureAwait(false);
            }
        }

        // workcompleted
        private async Task PublishWorkCompletedAsync(string transactionID, string uniqueId, ProductionInfo production)
        {
            var machineInfo = _machineInfoOptions.CurrentValue;
            var machineMetadata = _machineMetadataOptions.CurrentValue;
            var rabbitMQPublisher = _rabbitMQPublisherOptions.CurrentValue;

            var body = new Dictionary<string, dynamic?>
            {
                ["$type"] = $"{machineInfo.WorkCompleted}, CFX",
                ["PrimaryIdentifier"] = production.SN,
                ["HermesIdentifier"] = null,
                ["TransactionID"] = transactionID,
                ["Result"] = production.Result,
                ["UnitCount"] = null,
                ["Units"] = Array.Empty<object>(),
                ["PerformanceImpacts"] = Array.Empty<object>(),
                ["Metadata"] = CreateMetadataDictionary(machineMetadata)
            };

            var json = new CFXJsonModel
            {
                MessageName = machineInfo.WorkCompleted,
                Version = machineInfo.Version,
                TimeStamp = Convert.ToDateTime(production.EndTime).FormatDateTimeToIso8601(8),
                UniqueID = machineInfo.UniqueId,
                Source = machineInfo.UniqueId,
                Target = null,
                RequestID = null,
                MessageBody = body
            };

            var exchangeName = $"{rabbitMQPublisher.Prefix}.{EXCHANGE_SUFFIX}";
            var routingKey = $"{rabbitMQPublisher.Prefix}.workcompleted.{ROUTINGKEY_SUFFIX}";
            var queueName = $"{rabbitMQPublisher.Prefix}.workcompleted.{QUEUE_SUFFIX}";
            var message = JsonSerializer.Serialize(json, _jsonSerializerOptions);

            // workcompleted
            await _rabbitMQService.PublishMessageAsync(exchangeName, routingKey, message);
        }

        /// <summary>
        /// Publishes the machine state information from a CSV file.
        /// </summary>
        /// <returns></returns>
        public async Task PublishMachineStateAsync(string uniqueId)
        {
            var csvFilePaths = _csvFilePathOptions.CurrentValue;
            var machineInfo = _machineInfoOptions.CurrentValue;
            var machineMetadata = _machineMetadataOptions.CurrentValue;
            var rabbitMQPublisher = _rabbitMQPublisherOptions.CurrentValue;

            var filePath = csvFilePaths.MachineStatusInformationFilePath ?? "";
            var copyFilePath = $"{filePath}.backup.csv";

            if (!File.Exists(filePath) && !File.Exists(copyFilePath))
            {
                _logger.LogDebug("机器状态信息文件不存在: {FilePath}", filePath);
                return;
            }

            if (!File.Exists(copyFilePath))
            {
                File.Copy(filePath, copyFilePath, true);
                File.Delete(filePath);
            }

            filePath = copyFilePath;

            var lines = await File.ReadAllLinesAsync(filePath);
            var list = new List<MachineStatusInfo>();

            try
            {
                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = line.Split(',');
                    if (columns.Length < 4) continue;

                    list.Add(new MachineStatusInfo
                    {
                        OPTime = columns[0],
                        Status = string.IsNullOrWhiteSpace(columns[1]) ? null : Convert.ToInt32(columns[1]),
                        ErrorID = string.IsNullOrWhiteSpace(columns[2]) ? null : Convert.ToInt32(columns[2]),
                        ErrorMsg = columns[3]
                    });
                }

                if (list.Count == 0)
                {
                    _logger.LogWarning("机器状态文件中没有有效数据");
                    return;
                }

                // faultoccurred
                var lastErrorIndex = list.FindLastIndex(s => s.Status == (int)MAPBasicStatusCode.Error);
                if (lastErrorIndex == -1)
                {
                    _logger.LogDebug("未找到错误状态记录");
                    return;
                }

                var lastError = list[lastErrorIndex];
                var guid = Guid.NewGuid().ToString();

                var faultOccurredJson = new Dictionary<string, dynamic?>
                {
                    ["MessageName"] = machineInfo.FaultOccurred,
                    ["Version"] = machineInfo.Version,
                    ["TimeStamp"] = Convert.ToDateTime(lastError.OPTime).FormatDateTimeToIso8601(8),
                    ["UniqueID"] = uniqueId,
                    ["Source"] = uniqueId,
                    ["Target"] = null,
                    ["RequestID"] = null,
                    ["MessageBody"] = new Dictionary<string, dynamic?>
                    {
                        ["$type"] = $"{machineInfo.FaultOccurred}, CFX",
                        ["Fault"] = new Dictionary<string, dynamic?>
                        {
                            ["TransactionID"] = guid,
                            ["Cause"] = lastError.ErrorMsg,
                            ["Severity"] = "Information",
                            ["FaultCode"] = lastError.ErrorID,
                            ["FaultOccurrenceId"] = guid,
                            ["Lane"] = 1,
                            ["Stage"] = new Dictionary<string, dynamic>
                            {
                                ["StageSequence"] = 4,
                                ["StageName"] = "Map_Inspection_4",
                                ["StageType"] = "Inspection"
                            },
                            ["SiteLocation"] = "Unknown",
                            ["AccessType"] = "Unknown",
                            ["Description"] = "",
                            ["DescriptionTranslation"] = new Dictionary<string, dynamic>
                            {
                                ["bool"] = false
                            },
                            ["OccurredAt"] = Convert.ToDateTime(lastError.OPTime).FormatDateTimeToIso8601(8),
                            ["DueDateTime"] = null
                        },
                        ["Metadata"] = CreateMetadataDictionary(machineMetadata)
                    }
                };

                var faultoccurred_exchangeName = $"{rabbitMQPublisher.Prefix}.{EXCHANGE_SUFFIX}";
                var faultoccurred_routingKey = $"{rabbitMQPublisher.Prefix}.faultoccurred.{ROUTINGKEY_SUFFIX}";
                var faultoccurred_queueName = $"{rabbitMQPublisher.Prefix}.faultoccurred.{QUEUE_SUFFIX}";
                var faultoccurred_message = JsonSerializer.Serialize(faultOccurredJson, _jsonSerializerOptions);

                await _rabbitMQService.PublishMessageAsync(faultoccurred_exchangeName, faultoccurred_routingKey, faultoccurred_message);

                // faultcleared
                if (list.Count - 1 == lastErrorIndex)
                {
                    _logger.LogDebug("没有后续的故障清除记录");
                    return;
                }

                var lastClearErrorOPTime = list[lastErrorIndex + 1].OPTime;
                var faultClearedJson = new Dictionary<string, dynamic?>
                {
                    ["MessageName"] = machineInfo.FaultCleared,
                    ["Version"] = machineInfo.Version,
                    ["TimeStamp"] = Convert.ToDateTime(lastClearErrorOPTime).FormatDateTimeToIso8601(8),
                    ["UniqueID"] = uniqueId,
                    ["Source"] = uniqueId,
                    ["Target"] = "Arch",
                    ["RequestID"] = null,
                    ["MessageBody"] = new Dictionary<string, dynamic?>
                    {
                        ["$type"] = "CFX.ResourcePerformance.FaultCleared, CFX",
                        ["FaultOccurrenceId"] = guid,
                        ["Operator"] = new Dictionary<string, string>
                        {
                            ["OperatorIdentifier"] = "",
                            ["ActorType"] = "",
                            ["LastName"] = "",
                            ["FirstName"] = "",
                            ["LogingName"] = ""
                        },
                        ["Metadata"] = CreateMetadataDictionary(machineMetadata)
                    }
                };

                var faultcleared_exchangeName = $"{rabbitMQPublisher.Prefix}.{EXCHANGE_SUFFIX}";
                var faultcleared_routingKey = $"{rabbitMQPublisher.Prefix}.faultcleared.{ROUTINGKEY_SUFFIX}";
                var faultcleared_queueName = $"{rabbitMQPublisher.Prefix}.faultcleared.{QUEUE_SUFFIX}";
                var faultcleared_message = JsonSerializer.Serialize(faultClearedJson, _jsonSerializerOptions);

                await _rabbitMQService.PublishMessageAsync(faultcleared_exchangeName, faultcleared_routingKey, faultcleared_message);

                // StationStateChanged
                if (list.Count >= 2)
                {
                    var oldState = list[list.Count - 2].Status.HasValue ? StatusEventType.GetCfxCode((MAPBasicStatusCode)list[list.Count - 2].Status.Value) : -1;
                    var newState = list.Last().Status.HasValue ? StatusEventType.GetCfxCode((MAPBasicStatusCode)list.Last().Status.Value) : -1;
                    var oldStateDuration = "";
                    var lastOPTime = list.Last().OPTime;
                    var secondToLastOPTime = list[list.Count - 2].OPTime;

                    if (!string.IsNullOrWhiteSpace(lastOPTime) && !string.IsNullOrWhiteSpace(secondToLastOPTime))
                    {
                        oldStateDuration = DateTimeExtensions.CalculateTimeDifference(secondToLastOPTime, lastOPTime);
                    }

                    var stationstatechanged_json = new Dictionary<string, dynamic?>
                    {
                        ["MessageName"] = machineInfo.StationStateChanged,
                        ["Version"] = machineInfo.Version,
                        ["TimeStamp"] = Convert.ToDateTime(lastOPTime).FormatDateTimeToIso8601(8),
                        ["UniqueID"] = uniqueId,
                        ["Source"] = uniqueId,
                        ["Target"] = "ARCH",
                        ["RequestID"] = null,
                        ["MessageBody"] = new Dictionary<string, dynamic?>
                        {
                            ["$type"] = "CFX.ResourcePerformance.StationStateChanged, CFX",
                            ["OldState"] = oldState,
                            ["OldStateDuration"] = oldStateDuration,
                            ["NewState"] = newState,
                            ["RelatedFault"] = null,
                            ["Metadata"] = CreateMetadataDictionary(machineMetadata)
                        }
                    };

                    var stationstatechanged_exchangeName = $"{rabbitMQPublisher.Prefix}.{EXCHANGE_SUFFIX}";
                    var stationstatechanged_routingKey = $"{rabbitMQPublisher.Prefix}.stationstatechanged.{ROUTINGKEY_SUFFIX}";
                    var stationstatechanged_queueName = $"{rabbitMQPublisher.Prefix}.stationstatechanged.{QUEUE_SUFFIX}";
                    var stationstatechanged_message = JsonSerializer.Serialize(stationstatechanged_json, _jsonSerializerOptions);

                    await _rabbitMQService.PublishMessageAsync(stationstatechanged_exchangeName, stationstatechanged_routingKey, stationstatechanged_message);
                }

                _logger.LogInformation("机器状态信息处理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布MachineState消息时发生错误");
                return;
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                await Task.Delay(5000).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 创建元数据字典的辅助方法
        /// </summary>
        private Dictionary<string, string> CreateMetadataDictionary(MachineMetadataSetting machineMetadata)
        {
            return new Dictionary<string, string>
            {
                ["building"] = machineMetadata.Building ?? "",
                ["device"] = machineMetadata.Device ?? "",
                ["area_name"] = machineMetadata.AreaName ?? "",
                ["org"] = machineMetadata.Organization ?? "",
                ["line_name"] = machineMetadata.LineName ?? "",
                ["site_name"] = machineMetadata.SiteName ?? "",
                ["station_name"] = machineMetadata.StationName ?? "",
                ["Process_type"] = machineMetadata.ProcessType ?? "",
                ["machine_name"] = machineMetadata.MachineName ?? "",
                ["Created_by"] = machineMetadata.CreatedBy ?? "",
            };
        }

        private bool IsValidDateTime(string dateTimeString)
        {
            // 尝试解析日期时间字符串
            // 支持多种格式，包括 "yyyy/M/d H:mm", "yyyy/M/d H:m", "yyyy/M/d H:mm:ss" 等
            string[] formats = {
                "yyyy/M/d H:mm",
                "yyyy/M/d H:m",
                "yyyy/M/d H:mm:ss",
                "yyyy/M/d H:m:s",
                "yyyy/MM/dd HH:mm",
                "yyyy/MM/dd HH:mm:ss",
                "yyyy/M/dd H:mm",
                "yyyy/M/dd H:mm:ss"
            };

            DateTime result;
            return DateTime.TryParseExact(dateTimeString, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        public void Dispose()
        {
            // 释放配置监听
            foreach (var token in _optionsChangeTokens)
            {
                token?.Dispose();
            }
            _optionsChangeTokens.Clear();

            _logger.LogDebug("MachineService 资源已释放");
        }
    }
}