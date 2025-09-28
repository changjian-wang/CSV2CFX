using CSV2CFX.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSV2CFX.AppSettings
{
    public class MessageBrokerSetting
    {
        public ProtocolType ProtocolType { get; set; } = ProtocolType.AMQP;

        // MQTT 设置
        public MqttSetting? MQTT { get; set; }
    }

    public class MqttSetting
    {
        public string? BrokerHost { get; set; } = "localhost";
        public int BrokerPort { get; set; } = 1883;
        public string? ClientId { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool UseTls { get; set; } = false;
        public int KeepAliveInterval { get; set; } = 60;
        public int ConnectionTimeout { get; set; } = 30;
        public int QualityOfService { get; set; } = 1; // 0, 1, 2
    }
}
