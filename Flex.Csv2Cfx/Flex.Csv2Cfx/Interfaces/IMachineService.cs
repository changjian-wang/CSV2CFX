using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Interfaces
{
    public interface IMachineService
    {
        Dictionary<string, dynamic?> GetHeartbeat();

        Task<List<Dictionary<string, dynamic?>>> GetWorkProcessesAsync();

        Task<List<Dictionary<string, dynamic?>>> GetMachineStateAsync();
    }
}
