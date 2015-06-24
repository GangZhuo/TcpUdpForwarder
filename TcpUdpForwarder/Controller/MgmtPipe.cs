using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

using TcpUdpForwarder.Model;

namespace TcpUdpForwarder.Controller
{
    public class MgmtPipe
    {
        ForwarderController controller;
        List<Handler> handlers = new List<Handler>();

        public MgmtPipe(ForwarderController controller)
        {
            this.controller = controller;
        }

        public bool CreatePipe(Socket socket)
        {
            Handler handler = new Handler(controller);
            handler.OnClose += handler_OnClose;
            handler.Start(socket);
            return true;
        }

        public void ReportError(Exception e)
        {
            lock (handlers)
            {
                foreach(Handler h in handlers)
                {
                    h.Send("Error " + e.Message);
                }
            }
        }

        public void ReportConfigChanged()
        {
            lock (handlers)
            {
                foreach (Handler h in handlers)
                {
                    h.Send("ConfigChanged");
                }
            }
        }

        public void ReportEnableStatusChanged()
        {
            lock (handlers)
            {
                foreach (Handler h in handlers)
                {
                    h.Send("EnableStatusChanged");
                }
            }
        }

        private void handler_OnClose(object sender, EventArgs e)
        {
            lock (handlers)
            {
                handlers.Remove((Handler)sender);
            }
        }

        private void UpdateHandlerList(Handler handler)
        {
            lock (handlers)
            {
                handlers.Insert(0, handler);
            }
        }

        class Handler
        {
            ForwarderController controller;
            private Socket _local;
            private bool _closed = false;
            public const int RecvSize = 16384;
            // receive buffer
            private byte[] recvBuffer = new byte[RecvSize];

            public event EventHandler OnClose;

            public Handler(ForwarderController controller)
            {
                this.controller = controller;
            }

            public void Start(Socket socket)
            {
                this._local = socket;
                StartPipe();
            }

            private void StartPipe()
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    _local.BeginReceive(recvBuffer, 0, RecvSize, 0, new AsyncCallback(receiveCallback), null);
                    Send(Logging.LogFile);
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
                    int bytesRead = _local.EndReceive(ar);

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
                            case "Start":
                                controller.Start();
                                break;
                            case "Stop":
                                controller.Stop();
                                break;
                            case "Reload":
                                controller.Reload();
                                break;
                        }
                        _local.BeginReceive(this.recvBuffer, 0, RecvSize, 0,
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
                    _local.BeginSend(bytes, offset, length, 0, new AsyncCallback(sendCallback), null);
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
                    _local.EndSend(ar);
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
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
                if (OnClose != null)
                    OnClose(this, new EventArgs());
            }
        }
    }
}
