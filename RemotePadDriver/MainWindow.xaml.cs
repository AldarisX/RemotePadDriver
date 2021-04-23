using Nefarius.ViGEm.Client.Targets.Xbox360;
using System;
using System.Net;
using System.Threading;
using System.Windows;

namespace RemotePadDriver
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        PadManager padManager = PadManager.GetInstance();
        NetProc netProc = NetProc.GetInstance();

        private Thread thrServer;
        private Thread thrClient;

        public MainWindow()
        {
            InitializeComponent();

            padManager.Dispatcher = Dispatcher;
            netProc.serverDelayCall += serverDelay;
            dgPadList.ItemsSource = padManager.PadList;
        }

        private void serverDelay(double delay)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                lbDelay.Content = delay + "ms";
            }));
        }

        private void feedbackRec(object sender, Xbox360FeedbackReceivedEventArgs e)
        {
            Console.WriteLine("L:" + e.LargeMotor);
            Console.WriteLine("S:" + e.SmallMotor);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            //if (tcpClient.Connected)
            //    tcpClient.Close();
            //if (clientSocket != null && clientSocket.Connected)
            //    clientSocket.Close();
            //if (serverSocket.IsBound)
            //    serverSocket.Close();
        }

        private void btnServer_Click(object sender, RoutedEventArgs e)
        {
            ThreadStart ts = new ThreadStart(server);
            thrServer = new Thread(ts);
            thrServer.Start();
        }

        private void server()
        {
            string server = null;
            string serverPort = null;
            Dispatcher.Invoke(delegate ()
            {
                server = tbListen.Text;
                serverPort = tbListenPort.Text;
            });

            netProc.StartServer(IPAddress.Parse(server), Convert.ToInt32(serverPort));
        }

        private void btnClient_Click(object sender, RoutedEventArgs e)
        {
            ThreadStart ts = new ThreadStart(clientConn);
            thrClient = new Thread(ts);
            thrClient.Start();
        }

        private void clientConn()
        {
            string server = null;
            string serverPort = null;
            Dispatcher.Invoke(delegate ()
            {
                server = tbServer.Text;
                serverPort = tbServerPort.Text;
            });
            netProc.StartClient(IPAddress.Parse(server), Convert.ToInt32(serverPort));
        }
    }
}
