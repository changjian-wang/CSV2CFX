using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx
{
    public class AppSettings
    {
        public MqttSettings MqttSettings { get; set; } = new();

        public RabbitMqSettings RabbitMqSettings { get; set; } = new();

        public MachineSettings MachineSettings { get; set; } = new();

        public MessageProtocol PreferredProtocol { get; set; } = MessageProtocol.MQTT;
    }

    //public enum MessageProtocol
    //{
    //    MQTT,
    //    AMQP,
    //    Both
    //}

    public enum MessageProtocol
    {
        MQTT,
        AMQP
    }
}
