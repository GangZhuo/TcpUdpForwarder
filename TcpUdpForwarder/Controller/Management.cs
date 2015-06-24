using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;

namespace TcpUdpForwarder.Controller
{
    public class Management
    {
        ForwarderController controller;
        private int _mgmtPort;
        private Socket _remote;
        private bool _closed = false;
        public const int RecvSize = 16384;
        // remote receive buffer
        private byte[] recvBuffer = new byte[RecvSize];

        public event EventHandler OnStart;
        public event EventHandler OnClose;

        public Management(ForwarderController controller, int mgmtPort)
        {
            this.controller = controller;
            this._mgmtPort = mgmtPort;
        }

        public void Start()
        {
            try
            {
                // TODO async resolving
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Loopback, _mgmtPort);

                _remote = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
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

        private void ConnectCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }
            try
            {
                _remote.EndConnect(ar);
                _remote.BeginReceive(recvBuffer, 0, RecvSize, 0, new AsyncCallback(receiveCallback), null);
                if (OnStart != null)
                    OnStart(this, new EventArgs());
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
            }
        }

        private void receiveCallback(IAsyncResult ar)
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
                    string str = Encoding.UTF8.GetString(recvBuffer, 0, bytesRead);
                    int i = str.IndexOf(' ');
                    string key, value;
                    if (i > 0)
                    {
                        key = str.Substring(0, i).Trim();
                        value = str.Substring(i).Trim();
                    }
                    else
                    {
                        key = str.Trim();
                        value = string.Empty;
                    }
                    switch (key)
                    {
                        case "Error":
                            controller.ReportError(new Exception(value));
                            break;
                        case "ConfigChanged":
                            controller.ReportConfigChanged();
                            break;
                        case "EnableStatusChanged":
                            controller.ReportEnableStatusChanged();
                            break;
                        default:
                            Console.WriteLine(str);
                            break;
                    }
                    _remote.BeginReceive(this.recvBuffer, 0, RecvSize, 0,
                        new AsyncCallback(receiveCallback), null);
                }
                else
                {
                    this.Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
            }
        }

        public void Send(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            Send(bytes, 0, bytes.Length);
        }

        public void Send(byte[] bytes, int offset, int length)
        {
            try
            {
                _remote.BeginSend(bytes, offset, length, 0, new AsyncCallback(sendCallback), null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
            }
        }

        private void sendCallback(IAsyncResult ar)
        {
            if (_closed)
            {
                return;
            }
            try
            {
                _remote.EndSend(ar);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                this.Close();
            }
        }

        public void StartService()
        {
            Send("Start");
        }

        public void StopService()
        {
            Send("Stop");
        }

        public void ReloadService()
        {
            Send("Reload");
        }

        public bool IsClosed()
        {
            lock (this)
            {
                return _closed;
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
            if (OnClose != null)
                OnClose(this, new EventArgs());
        }
    }
}
