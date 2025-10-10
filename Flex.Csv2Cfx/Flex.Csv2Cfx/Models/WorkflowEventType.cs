using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Models
{
    /// <summary>
    /// 工作流事件类型枚举
    /// 定义了工作流程中可能发生的各种事件状态
    /// </summary>
    public enum WorkflowEventType
    {
        /// <summary>
        /// 心跳
        /// </summary>
        [Description("心跳")]
        Heartbeat = 0,

        /// <summary>
        /// 工作完成
        /// </summary>
        [Description("工作完成")]
        WorkCompleted = 1,

        /// <summary>
        /// 工作开始
        /// </summary>
        [Description("工作开始")]
        WorkStarted = 2,

        /// <summary>
        /// 单元已处理
        /// </summary>
        [Description("单元已处理")]
        UnitsProcessed = 3,

        /// <summary>
        /// 状态改变
        /// </summary>
        [Description("状态改变")]
        ChangeState = 4,

        /// <summary>
        /// 故障发生
        /// </summary>
        [Description("故障发生")]
        FaultOccurred = 5,

        /// <summary>
        /// 故障清除
        /// </summary>
        [Description("故障清除")]
        FaultCleared = 6
    }
}
