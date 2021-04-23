using Nefarius.ViGEm.Client;
using System.ComponentModel;
using System.Net.Sockets;

namespace RemotePadDriver
{
    class PadObj : INotifyPropertyChanged
    {
        private string id;
        private string type;
        private long lastHB;
        private string delay;
        private IVirtualGamepad pad;
        private bool ready = false;
        private TcpClient tcpClient;

        public string Id { get => id; set => id = value; }
        public string Type
        {
            get => type; set
            {
                if(type != value)
                {
                    type = value;
                    OnPropertyChange("type");
                }
                
            }
        }
        public long LastHB { get => lastHB; set => lastHB = value; }
        public IVirtualGamepad Pad { get => pad; set => pad = value; }
        public TcpClient TcpClient { get => tcpClient; set => tcpClient = value; }
        public string Delay { get => delay; set
            {
                if (delay != value)
                {
                    delay = value;
                    OnPropertyChange("delay");
                }

            }
        }

        public bool Ready { get => ready; set => ready = value; }

        public event PropertyChangedEventHandler PropertyChanged;

        internal void OnPropertyChange(string property)
        {
            if (PropertyChanged != null)
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
