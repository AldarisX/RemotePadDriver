using AesEverywhere;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;

namespace RemotePadDriver
{
    class NetProc
    {
        private string id = System.Guid.NewGuid().ToString();
        private int ReceiveBufferSize = 1024;
        private static NetProc netProc;
        private PadManager padManager = PadManager.GetInstance();

        private System.Timers.Timer hbTimer = new System.Timers.Timer();

        private TcpListener tcpListener;
        private TcpClient tcpClient;
        private List<TcpClient> tempSocketList = new List<TcpClient>();

        public delegate void ServerDelay(double delay);
        public ServerDelay serverDelayCall;

        public ManualResetEvent allDone = new ManualResetEvent(false);
        private Mutex sLock = new Mutex();
        //private Mutex hbLock = new Mutex();
        private long lastHBTime = 0;

        public NetProc()
        {
            ThreadPool.SetMaxThreads(16, 8);
        }

        public static NetProc GetInstance()
        {
            if (netProc == null)
            {
                netProc = new NetProc();

                netProc.hbTimer.Interval = 1000;
                netProc.hbTimer.Elapsed += netProc.procHB;
                netProc.hbTimer.AutoReset = true;
                netProc.hbTimer.SynchronizingObject = null;
                netProc.hbTimer.Start();
            }
            return netProc;
        }

        public void StartClient(IPAddress ipa, int port)
        {
            if (tcpClient != null && tcpClient.Connected)
                return;
            tcpClient = new TcpClient();
            tcpClient.ReceiveBufferSize = ReceiveBufferSize;
            tcpClient.NoDelay = true;
            tcpClient.BeginConnect(ipa, port, result =>
            {
                try
                {
                    tcpClient.EndConnect(result);
                }
                catch (SocketException e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.ErrorCode);
                    MessageBox.Show("连接服务器失败");
                    return;
                }
                NetworkStream stream = tcpClient.GetStream();
                AES256 aes = new AES256();
                //发送hello
                Data protoData = new Data()
                {
                    Cmd = CmdType.THello,
                    MsgType = MsgType.Driver,
                    Hello = new Hello
                    {
                        Group = "test",
                        ServerMsg = aes.Encrypt("hello", "654321"),
                    },
                };
                lastHBTime = Util.GetTime();
                AsyncSend(tcpClient, protoData);
                AsyncRecive(tcpClient, stream);
            }, null);
        }

        public void StartServer(IPAddress ipa, int port)
        {
            if (tcpListener != null)
                return;
            tcpListener = new TcpListener(ipa, port);
            tcpListener.Server.ReceiveBufferSize = ReceiveBufferSize;
            //serverSocket.NoDelay = true;
            //serverSocket.Bind(new IPEndPoint(ipa, port));  //绑定IP地址：端口  
            //serverSocket.Listen(10);//设定最多个排队连接请求
            tcpListener.Start();

            while (tcpListener.Server.IsBound)
            {
                allDone.Reset();
                tcpListener.BeginAcceptTcpClient(result =>
                {
                    allDone.Set();
                    TcpClient client = tcpListener.EndAcceptTcpClient(result);
                    client.NoDelay = true;
                    client.ReceiveBufferSize = ReceiveBufferSize;
                    tempSocketList.Add(client);
                    NetworkStream stream = client.GetStream();
                    AsyncRecive(client, stream);
                }, null);

                allDone.WaitOne();
            }
        }

        private async void AsyncRecive(TcpClient client, NetworkStream stream)
        {
            if (!client.Connected)
                return;
            try
            {
                byte[] dataLen = new byte[4];
                await stream.ReadAsync(dataLen, 0, 4);
                int len = BitConverter.ToInt32(dataLen, 0);
                if (len <= 0)
                {
                    AsyncRecive(client, stream);
                    return;
                }
                byte[] data = new byte[len];
                await stream.ReadAsync(data, 0, len);
                AsyncRecive(client, stream);
                procProtoData(client, data);
            }
            catch (IOException e)
            {
                Console.WriteLine(e.Message);
                MessageBox.Show("与服务器连接断开");
                AsyncRecive(client, stream);
            }
            catch (SocketException e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine("socket exception {0}", e.ErrorCode);
                MessageBox.Show("与服务器连接断开");
                AsyncRecive(client, stream);
            }
        }

        private async void AsyncSend(TcpClient client, Data protoData)
        {
            NetworkStream stream = client.GetStream();
            protoData.Id = id;
            byte[] data = protoData.ToByteArray();
            byte[] len = BitConverter.GetBytes(data.Length);
            await stream.WriteAsync(len, 0, len.Length);
            await stream.WriteAsync(data, 0, data.Length);
            stream.Flush();
        }

        private async void procProtoData(TcpClient client, byte[] data)
        {
            await Task.Run(() =>
            {
                Data protoData = null;
                try
                {
                    protoData = Data.Parser.ParseFrom(data);
                }
                catch (InvalidProtocolBufferException e)
                {
                    Console.WriteLine(e.Message);
                    return;
                }

                //Debug.WriteLine(protoData);

                switch (protoData.Cmd)
                {
                    case CmdType.THello:
                        switch (protoData.MsgType)
                        {
                            case MsgType.Pad:
                                try
                                {
                                    string msg = protoData.Hello.Msg;
                                    AES256 aes = new AES256();
                                    aes.ByteOrder = !protoData.Hello.Order;
                                    if (aes.Decrypt(msg, "123456") != "hello")
                                    {
                                        Console.WriteLine("unkonw client wrong text");
                                        break;
                                    }
                                    padManager.Add(protoData.Id, client);
                                    protoData.MsgType = MsgType.Driver;
                                    AsyncSend(client, protoData);
                                }
                                catch (CryptographicException e)
                                {
                                    Console.WriteLine(protoData);
                                    Console.WriteLine("unkonw client");
                                }
                                break;
                        }
                        break;
                    case CmdType.TPing:
                        if (protoData.MsgType == MsgType.Server && protoData.Ping == null)
                            break;
                        lastHBTime = Util.GetTime();
                        double delay = (lastHBTime - protoData.Ping.Time) / 100D;
                        //Debug.WriteLine("client delay " + delay + "ms");
                        if (protoData.MsgType == MsgType.Server)
                        {
                            if (serverDelayCall != null)
                                serverDelayCall(delay);
                        }

                        padManager.UpdateHB(protoData.Id, lastHBTime, delay);
                        break;
                    case CmdType.TPadType:
                        switch (protoData.PadType)
                        {
                            case PadType.Xbox360:
                                padManager.SwitchType(protoData.Id, PadType.Xbox360);
                                break;
                            case PadType.Ds4:
                                padManager.SwitchType(protoData.Id, PadType.Ds4);
                                break;
                        }
                        if (protoData.MsgType == MsgType.Server)
                        {
                            padManager.FromServer(protoData.Id);
                        }
                        break;
                    case CmdType.TPadData:
                        padManager.ProcBtn(protoData);
                        break;
                }
            });
        }

        private void procHB(object sender, ElapsedEventArgs e)
        {
            long time = Util.GetTime();
            Data protoData = new Data()
            {
                Cmd = CmdType.TPing,
                MsgType = MsgType.Driver,
                Ping = new Ping
                {
                    Time = time,
                },
            };
            for (int i = 0; i < padManager.PadList.Count; i++)
            {
                PadObj padObj = padManager.PadList[i];
                if (time - padObj.LastHB > 10 * 100000)
                {
                    if (padObj.TcpClient != null && padObj.TcpClient != tcpClient)
                    {
                        if (padObj.TcpClient.Connected)
                            Debug.WriteLine("HB timeout {0}", padObj.TcpClient.Client.RemoteEndPoint.ToString());
                    }
                    else
                    {
                        //TODO 要求服务端踢手柄端
                    }
                    padManager.Remove(padObj);
                    i--;
                    continue;
                }
                if (padObj.TcpClient != null)
                {
                    AsyncSend(padObj.TcpClient, protoData);
                }
                else
                {
                    AsyncSend(tcpClient, protoData);
                }
            }
            if (tcpClient != null && tcpClient.Connected)
            {
                if (time - lastHBTime > 10 * 100000)
                {
                    Debug.WriteLine("Server HB timeout");
                    tcpClient.Close();
                    return;
                }
                //if (serverDelayCall != null)
                //    serverDelayCall((lastHBTime - protoData.Ping.Time) / 10D);
                AsyncSend(tcpClient, protoData);
            }
        }

        public void Shutdown()
        {
            if (tcpClient != null && tcpClient.Connected)
                tcpClient.Close();
            if (tcpListener != null && tcpListener.Pending())
                tcpListener.Stop();
        }
    }
}
