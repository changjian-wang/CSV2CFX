using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx
{
    public class MqttSettings
    {
        public string Server { get; set; } = "localhost";
        public int Port { get; set; } = 1883;
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string ClientIdPrefix { get; set; } = "";
    }
}
