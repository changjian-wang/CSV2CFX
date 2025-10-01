using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flex.Csv2Cfx.Models
{
    public class Message : INotifyPropertyChanged
    {
        private string _protocol = string.Empty;
        private string _topic = string.Empty;
        private string _content = string.Empty;
        private DateTime _timestamp = DateTime.Now;
        private string _status = string.Empty;
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Protocol
        {
            get => _protocol;
            set
            {
                if (_protocol != value)
                {
                    _protocol = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Protocol)));
                }
            }
        }

        public string Topic
        {
            get => _topic;
            set
            {
                _topic = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Topic)));
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                _timestamp = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Timestamp)));
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }
}
