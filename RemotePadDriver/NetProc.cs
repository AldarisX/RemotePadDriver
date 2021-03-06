using AesEverywhere;
using Google.Protobuf;
using System;
using System.Collections;
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
        private string id = Guid.NewGuid().ToString();
        private int ReceiveBufferSize = 1024;
        private static NetProc netProc;
        private PadManager padManager = PadManager.GetInstance();

        private System.Timers.Timer hbTimer = new System.Timers.Timer();

        private TcpListener tcpListener;
        private TcpClient tcpClient;
        private Hashtable tempClientMap = new Hashtable();

        public delegate void ServerDelay(double delay);
        public ServerDelay serverDelayCall;

        private ManualResetEvent allDone = new ManualResetEvent(false);
        private Mutex tempSocketLock = new Mutex();

        private long lastHBTime = 0;

        public NetProc()
        {
            //ThreadPool.SetMaxThreads(16, 8);
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
                _ = AsyncSend(tcpClient, protoData);
                AsyncRecive(tcpClient, stream);
            }, null);
        }

        public void StartServer(IPAddress ipa, int port)
        {
            if (tcpListener != null)
                return;
            tcpListener = new TcpListener(ipa, port);
            tcpListener.Server.ReceiveBufferSize = ReceiveBufferSize;
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
                    tempClientMap.Add(client, Util.GetTime());
                    NetworkStream stream = client.GetStream();
                    AsyncRecive(client, stream);
                }, null);

                allDone.WaitOne();
            }
        }

        private async void AsyncRecive(TcpClient client, NetworkStream stream)
        {
            try
            {
                while (client.Connected)
                {
                    byte[] dataLen = new byte[4];
                    await stream.ReadAsync(dataLen, 0, 4);
                    int len = BitConverter.ToInt32(dataLen, 0);
                    if (len <= 0)
                    {
                        continue;
                    }
                    byte[] data = new byte[len];
                    await stream.ReadAsync(data, 0, len);
                    procProtoData(client, data);
                }
            }
            catch(Exception e)
            {
                Console.WriteLine(e.StackTrace);
                Console.WriteLine(e.Message);
                if (e is SocketException)
                {
                    var se = (SocketException)e;
                    Console.WriteLine("socket exception {0}", se.ErrorCode);
                }
                if (client != tcpClient)
                {
                    padManager.Remove(client);
                    MessageBox.Show("与服务器连接断开");
                }
            }
        }

        private async Task AsyncSend(TcpClient client, Data protoData)
        {
            if (client == null)
                client = tcpClient;
            if (!client.Connected)
                return;
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
                                    tempSocketLock.WaitOne();
                                    tempClientMap.Remove(client);
                                    tempSocketLock.ReleaseMutex();
                                    _ = AsyncSend(client, protoData);
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
                        double delay = Util.Delay(lastHBTime, protoData.Ping.Time);
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
                //心跳时间超过10s
                if (Util.IsTimeout(time, padObj.LastHB))
                {
                    if (padObj.TcpClient != null && padObj.TcpClient != tcpClient)
                    {
                        if (padObj.TcpClient.Connected)
                            Debug.WriteLine("HB timeout {0}", padObj.TcpClient.Client.RemoteEndPoint.ToString());
                    }
                    else
                    {
                        //要求服务端踢手柄端
                        _ = RemoveAsync(padObj.TcpClient);
                    }
                    padManager.Remove(padObj);
                    i--;
                    continue;
                }
                if (padObj.TcpClient != null)
                {
                    _ = AsyncSend(padObj.TcpClient, protoData);
                }
                else
                {
                    _ = AsyncSend(tcpClient, protoData);
                }
            }
            if (tcpClient != null && tcpClient.Connected)
            {
                if (time - lastHBTime > 10 * 100000)
                {
                    MessageBox.Show("与服务器连接超时");
                    tcpClient.Close();
                    return;
                }
                _ = AsyncSend(tcpClient, protoData);
            }

            tempSocketLock.WaitOne();
            //TODO remove tempTcpClient
            tempSocketLock.ReleaseMutex();
        }

        public async Task RemoveAsync(TcpClient client)
        {
            PadObj padObj = padManager.Contains(client);
            if (padObj == null)
                return;
            Data protoData = new Data()
            {
                Cmd = CmdType.TDisconnect,
                MsgType = MsgType.Driver,
                Disconnect = new Disconnect()
                {
                    Id = padObj.Id,
                },
            };
            await AsyncSend(padObj.TcpClient, protoData);
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
