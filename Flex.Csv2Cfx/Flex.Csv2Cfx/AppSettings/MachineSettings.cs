using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx
{
    public class MachineSettings
    {
        public MachineSettingsMetadata Metadata { get; set; } = new();

        public MachineSettingsCfx Cfx { get; set; } = new();

        public MachineSettingsCsv Csv { get; set; } = new();
    }

    public class MachineSettingsMetadata
    {
        public string Building { get; set; } = "";

        public string Device { get; set; } = "";

        public string AreaName { get; set; } = "";

        public string Organization { get; set; } = "";

        public string LineName { get; set; } = "";

        public string SiteName { get; set; } = "";

        public string StationName { get; set; } = "";

        public string ProcessType { get; set; } = "";

        public string MachineName { get; set; } = "";

        public string CreatedBy { get; set; } = "";
    }

    public class MachineSettingsCfx
    {
        public string Heartbeat { get; set; } = "";

        public string WorkStarted { get; set; } = "";

        public string WorkCompleted { get; set; } = "";

        public string UnitsProcessed { get; set; } = "";

        public string StationStateChanged { get; set; } = "";

        public string FaultOccurred { get; set; } = "";

        public string FaultCleared { get; set; } = "";

        public string UniqueId { get; set; } = "";

        public string Version { get; set; } = "";

        public int HeartbeatFrequency { get; set; } = 5;
    }

    public class MachineSettingsCsv
    {
        public string? ProductionInformationFilePath { get; set; }

        public string? MachineStatusInformationFilePath { get; set; }

        public string? ProcessDataFilesFilePath { get; set; }
    }
}
