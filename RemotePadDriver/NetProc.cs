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

namespace RemotePadDriver
{
    class NetProc
    {
        private string id = System.Guid.NewGuid().ToString();
        private int ReceiveBufferSize = 512;
        private static NetProc netProc;
        private PadManager padManager = PadManager.GetInstance();

        private System.Timers.Timer hbTimer = new System.Timers.Timer();

        private Socket serverSocket;
        private List<Socket> tempSocketList = new List<Socket>();
        private Socket clientSocket;

        public delegate void ServerDelay(double delay);
        public ServerDelay serverDelayCall;

        public ManualResetEvent allDone = new ManualResetEvent(false);
        private Mutex sLock = new Mutex();
        //private Mutex hbLock = new Mutex();
        private long lastHBTime = 0;

        public NetProc() {
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
            if (clientSocket != null && clientSocket.Connected)
                return;
            clientSocket = new Socket(ipa.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.ReceiveBufferSize = ReceiveBufferSize;
            clientSocket.NoDelay = true;
            clientSocket.BeginConnect(new IPEndPoint(ipa, port), connResult =>
            {
                clientSocket.EndConnect(connResult);

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
                AsyncSend(clientSocket, protoData);
                AsyncRecive(clientSocket);
            }, null);
        }

        public void StartServer(IPAddress ipa, int port)
        {
            if (serverSocket != null && serverSocket.Connected)
                return;
            serverSocket = new Socket(ipa.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.ReceiveBufferSize = ReceiveBufferSize;
            serverSocket.NoDelay = true;
            serverSocket.Bind(new IPEndPoint(ipa, port));  //绑定IP地址：端口  
            serverSocket.Listen(10);//设定最多个排队连接请求

            while (serverSocket.IsBound)
            {
                allDone.Reset();

                serverSocket.BeginAccept(result =>
                {
                    allDone.Set();
                    Socket socket = serverSocket.EndAccept(result);
                    socket.NoDelay = true;
                    tempSocketList.Add(socket);
                    AsyncRecive(socket);
                }, null);

                allDone.WaitOne();
            }
        }

        //private async void AsyncRecive(Socket socket)
        //{
        //    await Task.Run(new Action(() =>
        //    {
        //        try
        //        {
        //            if (!socket.Connected)
        //                return;
        //            byte[] dataLen = new byte[4];
        //            socket.Receive(dataLen);
        //            int len = BitConverter.ToInt32(dataLen, 0);
        //            if (len <= 0)
        //            {
        //                AsyncRecive(socket);
        //                return;
        //            }
        //            byte[] data = new byte[len];
        //            socket.Receive(data);
        //            Data protoData = Data.Parser.ParseFrom(data);
        //            Debug.WriteLine("REC: LEN({0})", len);
        //            Debug.WriteLine(protoData);
        //            procProtoData(socket, protoData);
        //            AsyncRecive(socket);
        //        }
        //        catch (InvalidProtocolBufferException e)
        //        {
        //            Console.WriteLine(e);
        //            return;
        //        }
        //        catch (SocketException e)
        //        {
        //            Debug.WriteLine("socket exception {0}", e.ErrorCode);
        //            //return;
        //        }
        //    }));
        //}

        private async void AsyncRecive(Socket socket)
        {
            await Task.Run(() =>
            {
                if (!socket.Connected)
                    return;
                try
                {
                    byte[] dataLen = new byte[4];
                    socket.BeginReceive(dataLen, 0, dataLen.Length, SocketFlags.None, lenResult =>
                    {
                        try
                        {
                            if (!socket.Connected)
                                return;
                            byte[] dataLenD = (byte[])lenResult.AsyncState;
                            socket.EndReceive(lenResult);
                            int len = BitConverter.ToInt32(dataLenD, 0);
                            if (len <= 0)
                            {
                                AsyncRecive(socket);
                                return;
                            }
                            byte[] data = new byte[len];
                            socket.Receive(data);
                            try
                            {
                                Debug.WriteLine("REC: LEN({0})", len);

                                AsyncRecive(socket);
                                procProtoData(socket, data);
                            }
                            catch (SocketException e)
                            {
                                Debug.WriteLine("socket exception {0}", e.ErrorCode);
                                return;
                            }
                            //socket.BeginReceive(data, 0, data.Length, SocketFlags.None, dataResult =>
                            //{
                            //    try
                            //    {
                            //        byte[] dataD = (byte[])dataResult.AsyncState;
                            //        socket.EndReceive(dataResult);
                            //        Data protoData = Data.Parser.ParseFrom(dataD);
                            //        Debug.WriteLine("REC: LEN({0})", len);
                            //        Debug.WriteLine(protoData);
                            //        AsyncRecive(socket);
                            //        procProtoData(socket, protoData);
                            //    }
                            //    catch (SocketException e)
                            //    {
                            //        Debug.WriteLine("socket exception {0}", e.ErrorCode);
                            //        return;
                            //    }
                            //    catch (InvalidProtocolBufferException e)
                            //    {
                            //        Console.WriteLine(e);
                            //        return;
                            //    }
                            //}, data);
                        }
                        catch (SocketException e)
                        {
                            Debug.WriteLine("socket exception {0}", e.ErrorCode);
                            return;
                        }
                    }, dataLen);
                }
                catch (SocketException e)
                {
                    Debug.WriteLine("socket exception {0}", e.ErrorCode);
                    Console.WriteLine("procData");
                    Console.WriteLine(e.Message);
                    AsyncRecive(socket);
                }
            });
        }

        private async Task<byte[]> AsyncRecive(Socket socket, int len, int offset, byte[] data)
        {
            return await Task.Run(async () =>
            {
                int recLen = socket.Receive(data, offset, len, SocketFlags.None);
                if (recLen < len)
                {
                    _ = await AsyncRecive(socket, len - recLen, recLen - 1, data);
                }
                return data;
            });
        }

        private void AsyncSend(Socket socket, Data protoData)
        {
            //await Task.Run(new Action(() =>
            //{
                //sLock.WaitOne();
                protoData.Id = id;
                byte[] data = protoData.ToByteArray();
                byte[] len = BitConverter.GetBytes(data.Length);
                using (var stream = new MemoryStream())
                {
                    stream.Write(len, 0, len.Length);
                    stream.Write(data, 0, data.Length);
                    data = stream.ToArray();
                }
                try
                {
                    //socket.Send(data);
                    //Debug.WriteLine("SEND: LEN({0})", data.Length);
                    //Debug.WriteLine(protoData);

                    socket.BeginSend(data, 0, data.Length, SocketFlags.None, lenResult =>
                    {
                        //socket.Send(data);
                        socket.EndSend(lenResult);
                        Debug.WriteLine("SEND: LEN({0})", data.Length);
                        Debug.WriteLine(protoData);
                        //sLock.ReleaseMutex();
                    }, null);
                }
                catch (SocketException e)
                {
                    //sLock.ReleaseMutex();
                }
                finally
                {
                    //sLock.ReleaseMutex();
                }
            //}));
            
        }

        private async void procProtoData(Socket socket, byte[] data)
        {
            await Task.Run(() =>
            {
                Data protoData = Data.Parser.ParseFrom(data);
                Debug.WriteLine(protoData);

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
                                    padManager.Add(protoData.Id, socket);
                                    protoData.MsgType = MsgType.Driver;
                                    AsyncSend(socket, protoData);
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
                    if (padObj.Socket != null && padObj.Socket != clientSocket)
                    {
                        if (padObj.Socket.Connected)
                            Debug.WriteLine("HB timeout {0}", padObj.Socket.RemoteEndPoint.ToString());
                    }
                    else
                    {
                        //TODO 要求服务端踢手柄端
                    }
                    padManager.Remove(padObj);
                    i--;
                    continue;
                }
                if (padObj.Socket != null)
                {
                    AsyncSend(padObj.Socket, protoData);
                }
                else
                {
                    AsyncSend(clientSocket, protoData);
                }
            }
            if (clientSocket != null && clientSocket.Connected)
            {
                if (time - lastHBTime > 10 * 100000)
                {
                    Debug.WriteLine("Server HB timeout");
                    clientSocket.Close();
                    return;
                }
                //if (serverDelayCall != null)
                //    serverDelayCall((lastHBTime - protoData.Ping.Time) / 10D);
                AsyncSend(clientSocket, protoData);
            }
        }
    }
}
