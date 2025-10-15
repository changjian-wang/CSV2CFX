using System.Collections.Generic;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Interfaces
{
    public interface IMachineService
    {
        /// <summary>
        /// 获取心跳消息
        /// </summary>
        Dictionary<string, dynamic?> GetHeartbeat();

        /// <summary>
        /// 获取工作流程消息列表
        /// </summary>
        Task<List<Dictionary<string, dynamic?>>> GetWorkProcessesAsync();

        /// <summary>
        /// 获取机器状态消息列表
        /// </summary>
        Task<List<Dictionary<string, dynamic?>>> GetMachineStateAsync();
    }
}