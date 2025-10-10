using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Models
{
    public class MachineStatus
    {
        public string? OPTime { get; set; }

        public int? Status { get; set; }

        public int? ErrorID { get; set; }

        public string? ErrorMsg { get; set; }
    }
}
