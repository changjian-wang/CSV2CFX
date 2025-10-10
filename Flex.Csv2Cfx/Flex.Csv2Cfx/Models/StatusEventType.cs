using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Models
{
    public class StatusEventType
    {
        public static int GetCfxCode(MAPBasicStatusCode code)
        {
            switch (code)
            {
                case MAPBasicStatusCode.Ready:
                    return (int)CFXCode.Ready;

                case MAPBasicStatusCode.Running:
                    return (int)CFXCode.Running;

                case MAPBasicStatusCode.Error:
                    return (int)CFXCode.Error;

                case MAPBasicStatusCode.Idle:
                    return (int)CFXCode.Idle4_0;

                case MAPBasicStatusCode.Pause:
                    return (int)CFXCode.Pause;

                case MAPBasicStatusCode.Manual:
                    return (int)CFXCode.Manual;

                case MAPBasicStatusCode.TestRunning:
                    return (int)CFXCode.TestRunning;

                default:
                    return -1;
            }
        }
    }

    public enum MAPBasicStatusCode
    {
        Ready = 1,
        Running = 2,
        Error = 3,
        Idle = 4,
        Pause = 5,
        Manual = 6,
        TestRunning = 7
    }

    public enum MAPStatusCode
    {
        Ready = 10,
        Running = 20,
        Error = 30,
        Idle4_0 = 40,
        Idle4_1 = 41,
        Idle4_2 = 42,
        Idle4_3 = 43,
        Pause = 50,
        Manual = 60,
        TestRunning = 70
    }

    public enum CFXCode
    {
        [Description("设备准备OK")]
        Ready = 2000,

        [Description("设备正常生产中")]
        Running = 1000,

        [Description("设备发生异常中")]
        Error = 5000,

        [Description("设备运行状态中，但在一定时间内未流入产品")]
        Idle4_0 = 2200,

        [Description("设备运行状态中，但在一定时间内未流入产品")]
        Idle4_1 = 2200,

        [Description("设备运行状态中，但在一定时间内未流入产品")]
        Idle4_2 = 2201,

        [Description("设备运行状态中，但在一定时间内未流入产品")]
        Idle4_3 = 5500,

        [Description("设备暂停中")]
        Pause = 2100,

        [Description("设备手动状态（无报警）")]
        Manual = 2300,

        [Description("设备空跑测试运行中")]
        TestRunning = 4600
    }
}
