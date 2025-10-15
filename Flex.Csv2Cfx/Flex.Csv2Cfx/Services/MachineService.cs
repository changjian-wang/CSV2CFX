using Flex.Csv2Cfx.Extensions;
using Flex.Csv2Cfx.Interfaces;
using Flex.Csv2Cfx.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

// 使用别名解决冲突
using SystemIO = System.IO;

namespace Flex.Csv2Cfx.Services
{
    public class MachineService : IMachineService
    {
        private readonly IConfigurationService _configuration;
        private readonly ILogger _logger;
        private readonly AppSettings _settings;

        public MachineService(IConfigurationService configuration, ILogger<MachineService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _settings = _configuration.GetSettings();
        }

        /// <summary>
        /// Heartbeat消息 - 保持原有格式
        /// </summary>
        public Dictionary<string, dynamic?> GetHeartbeat()
        {
            var body = new Dictionary<string, dynamic?>
            {
                ["$type"] = $"{_settings.MachineSettings.Cfx.Heartbeat}, CFX",
                ["CFXHandle"] = _settings.MachineSettings.Cfx.UniqueId,
                ["HeartbeatFrequency"] = _settings.MachineSettings.Cfx.HeartbeatFrequency,
                ["ActiveFaults"] = 0,
                ["ActiveRecipes"] = Array.Empty<object>(),
                ["Metadata"] = CreateMetadataDictionary(_settings)
            };

            var json = new Dictionary<string, dynamic?>
            {
                ["MessageName"] = _settings.MachineSettings.Cfx.Heartbeat,
                ["Version"] = _settings.MachineSettings.Cfx.Version,
                ["TimeStamp"] = DateTime.UtcNow.FormatDateTimeToIso8601(0),
                ["UniqueID"] = _settings.MachineSettings.Cfx.UniqueId,
                ["Source"] = _settings.MachineSettings.Cfx.UniqueId,
                ["Target"] = null,
                ["RequestID"] = Guid.NewGuid().ToString(),
                ["MessageBody"] = body
            };

            return json;
        }

        /// <summary>
        /// WorkProcess消息 - 保持原有格式
        /// </summary>
        public async Task<List<Dictionary<string, dynamic?>>> GetWorkProcessesAsync()
        {
            var workProcesses = new List<Dictionary<string, dynamic?>>();
            var filePath = _settings.MachineSettings.Csv.ProductionInformationFilePath ?? "";
            var copyFilePath = $"{filePath}.backup.csv";

            if (!SystemIO.File.Exists(filePath) && !SystemIO.File.Exists(copyFilePath))
            {
                _logger.LogDebug("生产信息文件不存在: {FilePath}", filePath);
                return new List<Dictionary<string, dynamic?>>();
            }

            if (!SystemIO.File.Exists(copyFilePath))
            {
                SystemIO.File.Copy(filePath, copyFilePath, true);
                SystemIO.File.Delete(filePath);
                _logger.LogDebug("已创建备份文件: {CopyFilePath}", copyFilePath);
            }

            filePath = copyFilePath;
            var lines = await SystemIO.File.ReadAllLinesAsync(filePath);

            try
            {
                _logger.LogInformation("开始处理生产信息文件，共 {LineCount} 行数据", lines.Length - 1);

                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = line.Split(',');
                    if (columns.Length < 7) continue;
                    if (columns.All(c => string.IsNullOrWhiteSpace(c))) continue;

                    Production production = new Production
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
                    var uniqueId = _settings.MachineSettings.Cfx.UniqueId;

                    // workstarted
                    workProcesses.Add(GetWorkStarted(_settings, production, transactionID, uniqueId));

                    // unitsprocessed
                    workProcesses.Add(await GetUnitsProcessedAsync(_settings, production, transactionID, uniqueId).ConfigureAwait(false));

                    // workcompleted
                    workProcesses.Add(GetWorkCompleted(_settings, production, transactionID, uniqueId));
                }

                _logger.LogInformation("生产信息文件处理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成消息时发生错误");
                return new List<Dictionary<string, dynamic?>>();
            }
            finally
            {
                if (SystemIO.File.Exists(filePath))
                {
                    SystemIO.File.Delete(filePath);
                    _logger.LogDebug($"已删除备份文件: {filePath}");
                }
                await Task.Delay(5000).ConfigureAwait(false);
            }

            return workProcesses;
        }

        /// <summary>
        /// MachineState
        /// </summary>
        public async Task<List<Dictionary<string, dynamic?>>> GetMachineStateAsync()
        {
            var machineStates = new List<Dictionary<string, dynamic?>>();
            var filePath = _settings.MachineSettings.Csv.MachineStatusInformationFilePath ?? "";
            var copyFilePath = $"{filePath}.backup.csv";

            if (!SystemIO.File.Exists(filePath) && !SystemIO.File.Exists(copyFilePath))
            {
                _logger.LogDebug("机器状态信息文件不存在: {FilePath}", filePath);
                return new List<Dictionary<string, dynamic?>>();
            }

            if (!SystemIO.File.Exists(copyFilePath))
            {
                SystemIO.File.Copy(filePath, copyFilePath, true);
                SystemIO.File.Delete(filePath);
            }

            filePath = copyFilePath;

            var lines = await SystemIO.File.ReadAllLinesAsync(filePath);
            var list = new List<MachineStatus>();

            try
            {
                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var columns = line.Split(',');
                    if (columns.Length < 4) continue;
                    if (columns.All(c => string.IsNullOrWhiteSpace(c))) continue;

                    list.Add(new MachineStatus
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
                    return new List<Dictionary<string, dynamic?>>();
                }

                // faultoccurred
                var lastErrorIndex = list.FindLastIndex(s => s.Status == (int)MAPBasicStatusCode.Error);
                if (lastErrorIndex == -1)
                {
                    _logger.LogDebug("未找到错误状态记录");
                    return new List<Dictionary<string, dynamic?>>();
                }

                var lastError = list[lastErrorIndex];
                var guid = Guid.NewGuid().ToString();
                var uniqueId = _settings.MachineSettings.Cfx.UniqueId;

                var faultOccurredJson = new Dictionary<string, dynamic?>
                {
                    ["MessageName"] = _settings.MachineSettings.Cfx.FaultOccurred,
                    ["Version"] = _settings.MachineSettings.Cfx.Version,
                    ["TimeStamp"] = Convert.ToDateTime(lastError.OPTime).FormatDateTimeToIso8601(8),
                    ["UniqueID"] = uniqueId,
                    ["Source"] = uniqueId,
                    ["Target"] = null,
                    ["RequestID"] = null,
                    ["MessageBody"] = new Dictionary<string, dynamic?>
                    {
                        ["$type"] = $"{_settings.MachineSettings.Cfx.FaultOccurred}, CFX",
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
                        ["Metadata"] = CreateMetadataDictionary(_settings)
                    }
                };

                machineStates.Add(faultOccurredJson);

                // faultcleared
                if (list.Count - 1 == lastErrorIndex)
                {
                    _logger.LogDebug("没有后续的故障清除记录");
                    return machineStates;
                }

                var lastClearErrorOPTime = list[lastErrorIndex + 1].OPTime;
                var faultClearedJson = new Dictionary<string, dynamic?>
                {
                    ["MessageName"] = _settings.MachineSettings.Cfx.FaultCleared,
                    ["Version"] = _settings.MachineSettings.Cfx.Version,
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
                        ["Metadata"] = CreateMetadataDictionary(_settings)
                    }
                };

                machineStates.Add(faultClearedJson);

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
                        ["MessageName"] = _settings.MachineSettings.Cfx.StationStateChanged,
                        ["Version"] = _settings.MachineSettings.Cfx.Version,
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
                            ["Metadata"] = CreateMetadataDictionary(_settings)
                        }
                    };

                    machineStates.Add(stationstatechanged_json);
                }

                _logger.LogInformation("机器状态信息处理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布MachineState消息时发生错误");
                return new List<Dictionary<string, dynamic?>>();
            }
            finally
            {
                if (SystemIO.File.Exists(filePath))
                {
                    SystemIO.File.Delete(filePath);
                }
                await Task.Delay(5000).ConfigureAwait(false);
            }

            return machineStates;
        }

        // 辅助方法保持不变...
        private Dictionary<string, dynamic?> GetWorkStarted(AppSettings settings, Production production, string transactionID, string uniqueId)
        {
            var body = new Dictionary<string, dynamic?>
            {
                ["$type"] = $"{settings.MachineSettings.Cfx.WorkStarted}, CFX",
                ["PrimaryIdentifier"] = production.SN,
                ["HermesIdentifier"] = null,
                ["TransactionID"] = transactionID,
                ["Line"] = 1,
                ["UnitCount"] = null,
                ["Units"] = Array.Empty<object>(),
                ["Metadata"] = CreateMetadataDictionary(settings)
            };

            var json = new Dictionary<string, dynamic?>
            {
                ["MessageName"] = settings.MachineSettings.Cfx.WorkStarted,
                ["Version"] = settings.MachineSettings.Cfx.Version,
                ["TimeStamp"] = Convert.ToDateTime(production.StartTime).FormatDateTimeToIso8601(8),
                ["UniqueID"] = uniqueId,
                ["Source"] = uniqueId,
                ["Target"] = null,
                ["RequestID"] = Guid.NewGuid().ToString(),
                ["MessageBody"] = body
            };

            return json;
        }

        private async Task<Dictionary<string, dynamic?>> GetUnitsProcessedAsync(AppSettings settings, Production production, string transactionID, string uniqueId)
        {
            var directoryPath = settings.MachineSettings.Csv.ProcessDataFilesFilePath ?? "";
            if (!SystemIO.Directory.Exists(directoryPath))
            {
                _logger.LogWarning("过程数据文件夹不存在: {DirectoryPath}", directoryPath);
                return new Dictionary<string, dynamic?>();
            }

            var files = SystemIO.Directory.GetFiles(directoryPath, "*.csv");
            var filePath = files.Where(s => SystemIO.Path.GetFileNameWithoutExtension(s).StartsWith(production.SN ?? "")).FirstOrDefault() ?? "";

            var copyFilePath = $"{filePath}.backup.csv";

            if (!SystemIO.File.Exists(filePath) && !SystemIO.File.Exists(copyFilePath))
            {
                _logger.LogDebug("未找到序列号 {SN} 对应的过程数据文件", production.SN);
                return new Dictionary<string, dynamic?>();
            }

            if (!SystemIO.File.Exists(copyFilePath))
            {
                SystemIO.File.Copy(filePath, copyFilePath, true);
                SystemIO.File.Delete(filePath);
            }

            filePath = copyFilePath;

            try
            {
                var lines = await SystemIO.File.ReadAllLinesAsync(filePath, encoding: System.Text.Encoding.UTF8);
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
                    ["RecipeName"] = null,
                    ["CommonProcessData"] = new Dictionary<string, dynamic>
                    {
                        ["$type"] = "CFX.Structures.ProccessData, CFX",
                        ["PersonalizedUnits"] = personalizedUnits
                    },
                    ["Metadata"] = CreateMetadataDictionary(settings),
                    ["UnitProcessData"] = Array.Empty<object>()
                };

                var json = new Dictionary<string, dynamic?>
                {
                    ["MessageName"] = settings.MachineSettings.Cfx.UnitsProcessed ?? "",
                    ["Version"] = settings.MachineSettings.Cfx.Version ?? "",
                    ["TimeStamp"] = Convert.ToDateTime(production.EndTime).FormatDateTimeToIso8601(8),
                    ["UniqueID"] = settings.MachineSettings.Cfx.UniqueId ?? "",
                    ["Source"] = settings.MachineSettings.Cfx.UniqueId ?? "",
                    ["Target"] = null,
                    ["RequestID"] = null,
                    ["MessageBody"] = body
                };

                return json;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发布UnitsProcessed消息时发生错误，SN: {SN}", production.SN);
                return new Dictionary<string, dynamic?>();
            }
            finally
            {
                if (SystemIO.File.Exists(filePath))
                {
                    SystemIO.File.Delete(filePath);
                }
                await Task.Delay(5000).ConfigureAwait(false);
            }
        }

        private Dictionary<string, dynamic?> GetWorkCompleted(AppSettings settings, Production production, string transactionID, string uniqueId)
        {
            var body = new Dictionary<string, dynamic?>
            {
                ["$type"] = $"{settings.MachineSettings.Cfx.WorkCompleted}, CFX",
                ["PrimaryIdentifier"] = production.SN,
                ["HermesIdentifier"] = null,
                ["TransactionID"] = transactionID,
                ["Result"] = production.Result,
                ["UnitCount"] = null,
                ["Units"] = Array.Empty<object>(),
                ["PerformanceImpacts"] = Array.Empty<object>(),
                ["Metadata"] = CreateMetadataDictionary(settings)
            };

            var json = new Dictionary<string, dynamic?>
            {
                ["MessageName"] = settings.MachineSettings.Cfx.WorkCompleted ?? "",
                ["Version"] = settings.MachineSettings.Cfx.Version ?? "",
                ["TimeStamp"] = Convert.ToDateTime(production.EndTime).FormatDateTimeToIso8601(8),
                ["UniqueID"] = settings.MachineSettings.Cfx.UniqueId ?? "",
                ["Source"] = settings.MachineSettings.Cfx.UniqueId ?? "",
                ["Target"] = null,
                ["RequestID"] = null,
                ["MessageBody"] = body
            };

            return json;
        }

        /// <summary>
        /// 创建元数据字典的辅助方法
        /// </summary>
        private Dictionary<string, string> CreateMetadataDictionary(AppSettings settings)
        {
            return new Dictionary<string, string>
            {
                ["building"] = settings.MachineSettings.Metadata.Building ?? "",
                ["device"] = settings.MachineSettings.Metadata.Device ?? "",
                ["area_name"] = settings.MachineSettings.Metadata.AreaName ?? "",
                ["org"] = settings.MachineSettings.Metadata.Organization ?? "",
                ["line_name"] = settings.MachineSettings.Metadata.LineName ?? "",
                ["site_name"] = settings.MachineSettings.Metadata.SiteName ?? "",
                ["station_name"] = settings.MachineSettings.Metadata.StationName ?? "",
                ["Process_type"] = settings.MachineSettings.Metadata.ProcessType ?? "",
                ["machine_name"] = settings.MachineSettings.Metadata.MachineName ?? "",
                ["Created_by"] = settings.MachineSettings.Metadata.CreatedBy ?? "",
            };
        }

        private bool IsValidDateTime(string dateTimeString)
        {
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
    }
}