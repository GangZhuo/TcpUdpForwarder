using System;
using System.Net;
using System.Net.Sockets;

using TcpUdpForwarder.Model;

namespace TcpUdpForwarder.Controller
{
    public class TcpPipe
    {
        ServerInfo _server;

        public TcpPipe(ServerInfo server)
        {
            this._server = server;
        }

        public bool Handle(Socket socket)
        {
            new Handler(_server).Start(socket);
            return true;
        }

        class Handler
        {
            ServerInfo _server;

            private Socket _local;
            private Socket _remote;
            private bool _closed = false;
            private bool _localShutdown = false;
            private bool _remoteShutdown = false;
            public const int RecvSize = 16384;
            // remote receive buffer
            private byte[] remoteRecvBuffer = new byte[RecvSize];
            // connection receive buffer
            private byte[] connetionRecvBuffer = new byte[RecvSize];

            public Handler(ServerInfo server)
            {
                this._server = server;
            }

            public void Start(Socket socket)
            {
                this._local = socket;
                IPAddress ipAddress = null;
                bool parsed = IPAddress.TryParse(_server.server, out ipAddress);
                if (!parsed)
                    ipAddress = LookupIp(_server.server);
                if (ipAddress == null)
                    throw new Exception("Wrong target server");
                try
                {
                    // TODO async resolving
                    IPEndPoint remoteEP = new IPEndPoint(ipAddress, _server.serverPort);

                    _remote = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    _remote.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                    // Connect to the remote endpoint.
                    _remote.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private IPAddress LookupIp(string hostNameOrAddress)
            {
                IPAddress ipAddress = null;
                IPHostEntry hostinfo = Dns.GetHostEntry(hostNameOrAddress);
                IPAddress[] aryIP = hostinfo.AddressList;
                if (aryIP != null)
                {
                    foreach (IPAddress ip in aryIP)
                    {
                        if (ip.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ipAddress = ip;
                            break;
                        }
                    }
                }
                return ipAddress;
            }

            private void ConnectCallback(IAsyncResult ar)
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    _remote.EndConnect(ar);
                    StartPipe();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private void StartPipe()
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    _remote.BeginReceive(remoteRecvBuffer, 0, RecvSize, 0, new AsyncCallback(PipeRemoteReceiveCallback), null);
                    _local.BeginReceive(connetionRecvBuffer, 0, RecvSize, 0, new AsyncCallback(PipeConnectionReceiveCallback), null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private void PipeRemoteReceiveCallback(IAsyncResult ar)
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    int bytesRead = _remote.EndReceive(ar);

                    if (bytesRead > 0)
                    {
                        _local.BeginSend(remoteRecvBuffer, 0, bytesRead, 0, new AsyncCallback(PipeConnectionSendCallback), null);
                    }
                    else
                    {
                        //Console.WriteLine("bytesRead: " + bytesRead.ToString());
                        _local.Shutdown(SocketShutdown.Send);
                        _localShutdown = true;
                        CheckClose();
                    }
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private void PipeConnectionReceiveCallback(IAsyncResult ar)
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    int bytesRead = _local.EndReceive(ar);

                    if (bytesRead > 0)
                    {
                        _remote.BeginSend(connetionRecvBuffer, 0, bytesRead, 0, new AsyncCallback(PipeRemoteSendCallback), null);
                    }
                    else
                    {
                        _remote.Shutdown(SocketShutdown.Send);
                        _remoteShutdown = true;
                        CheckClose();
                    }
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private void PipeRemoteSendCallback(IAsyncResult ar)
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    _remote.EndSend(ar);
                    _local.BeginReceive(this.connetionRecvBuffer, 0, RecvSize, 0,
                        new AsyncCallback(PipeConnectionReceiveCallback), null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private void PipeConnectionSendCallback(IAsyncResult ar)
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    _local.EndSend(ar);
                    _remote.BeginReceive(this.remoteRecvBuffer, 0, RecvSize, 0,
                        new AsyncCallback(PipeRemoteReceiveCallback), null);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private void CheckClose()
            {
                if (_localShutdown && _remoteShutdown)
                {
                    this.Close();
                }
            }

            public void Close()
            {
                lock (this)
                {
                    if (_closed)
                    {
                        return;
                    }
                    _closed = true;
                }
                if (_local != null)
                {
                    try
                    {
                        _local.Shutdown(SocketShutdown.Both);
                        _local.Close();
                    }
                    catch (Exception e)
                    {
                        Logging.LogUsefulException(e);
                    }
                }
                if (_remote != null)
                {
                    try
                    {
                        _remote.Shutdown(SocketShutdown.Both);
                        _remote.Close();
                    }
                    catch (SocketException e)
                    {
                        Logging.LogUsefulException(e);
                    }
                }
            }
        }
    }
}
