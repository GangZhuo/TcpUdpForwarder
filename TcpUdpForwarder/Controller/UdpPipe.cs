﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

using TcpUdpForwarder.Model;

namespace TcpUdpForwarder.Controller
{
    public class UdpPipe
    {
        ServerInfo _server;
        EndPoint _serverEP;

        Hashtable _handlers = Hashtable.Synchronized(new Hashtable());

        System.Timers.Timer _timer = new System.Timers.Timer(10000);

        public UdpPipe(ServerInfo server)
        {
            _server = server;
            IPAddress ipAddress = null;
            bool parsed = IPAddress.TryParse(_server.server, out ipAddress);
            if (!parsed)
                ipAddress = LookupIp(_server.server);
            if (ipAddress == null)
                throw new Exception("Wrong target server");
            _serverEP = new IPEndPoint(ipAddress, _server.serverPort);
            _timer.AutoReset = true;
            _timer.Enabled = true;
            _timer.Elapsed += _timer_Elapsed;
            _timer.Start();
        }

        ~UdpPipe()
        {
            _timer.Stop();
            _handlers.Clear();
        }

        public bool Handle(byte[] firstPacket, int length, Socket fromSocket, EndPoint fromEP)
        {
            Handler handler = getHandler(fromEP, fromSocket);
            handler.Handle(firstPacket, length);
            return true;
        }

        Handler getHandler(EndPoint fromEP, Socket fromSocket)
        {
            string key = fromEP.ToString();
            lock(this)
            {
                if (_handlers.ContainsKey(key))
                    return (Handler)_handlers[key];
                Handler handler = new Handler();
                _handlers.Add(key, handler);
                handler._local = fromSocket;
                handler._localEP = fromEP;
                handler._remoteEP = _serverEP;
                handler.OnClose += handler_OnClose;
                handler.Start();
                return handler;
            }
        }

        private void handler_OnClose(object sender, EventArgs e)
        {
            Handler handler = (Handler)sender;
            string key = handler._localEP.ToString();
            lock (this)
            {
                if (_handlers.ContainsKey(key))
                {
#if DEBUG
                    try { Console.WriteLine("remove udp pipe"); }
                    catch { }
#endif
                    _handlers.Remove(key);
                }
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

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                _timer.Stop();
                List<Handler> keys = new List<Handler>(_handlers.Count);
                lock (this)
                {
                    foreach (string key in _handlers.Keys)
                    {
                        Handler handler = (Handler)_handlers[key];
                        if (handler.IsExpire())
                        {
                            keys.Add(handler);
                        }
                    }
                    foreach (Handler handler in keys)
                    {
#if DEBUG
                        try { Console.WriteLine("remove expire: " + handler._remote.LocalEndPoint.ToString()); }
                        catch { }
#endif
                        string key = handler._localEP.ToString();
                        handler.Close(false);
                        _handlers.Remove(key);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.LogUsefulException(ex);
            }
            finally
            {
                _timer.Start();
            }
        }

        class Handler
        {
            public event EventHandler OnClose;

            private DateTime _expires;

            public Socket _local;
            public EndPoint _localEP;
            public EndPoint _remoteEP;
            public Socket _remote;

            private bool _closed = false;
            public const int RecvSize = 16384;
            // remote receive buffer
            private byte[] remoteRecvBuffer = new byte[RecvSize];

            private List<byte[]> _packages = new List<byte[]>(1024);
            private bool _connected = false;
            private bool _sending = false;

            public bool IsExpire()
            {
                lock(this)
                {
                    return _expires <= DateTime.Now;
                }
            }

            public void Start()
            {
                try
                {
                    _remote = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    _remote.SetSocketOption(SocketOptionLevel.Udp, SocketOptionName.NoDelay, true);
                    _remote.BeginConnect(_remoteEP, new AsyncCallback(RemoteConnectCallback), null);
                    Delay();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            public void Handle(byte[] buffer, int length)
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    if (length > 0)
                    {
                        lock (_packages)
                        {
                            byte[] bytes = new byte[length];
                            Buffer.BlockCopy(buffer, 0, bytes, 0, length);
                            _packages.Add(bytes);
                        }
                        StartSend();
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

            private void RemoteConnectCallback(IAsyncResult ar)
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    _remote.EndConnect(ar);
                    lock (_packages)
                        _connected = true;
                    StartPipe();
                    StartSend();
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
                    EndPoint remoteEP = (EndPoint)(new IPEndPoint(((IPEndPoint)_remoteEP).Address, ((IPEndPoint)_remoteEP).Port));
                    _remote.BeginReceiveFrom(this.remoteRecvBuffer, 0, RecvSize, 0, ref remoteEP,
                        new AsyncCallback(PipeRemoteReceiveCallback), null);
                    StartSend();
                    Delay();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private void StartSend()
            {
                if (_closed)
                {
                    return;
                }
                try
                {
                    lock (_packages)
                    {
                        if (_sending || !_connected)
                            return;
                        if (_packages.Count > 0)
                        {
                            _sending = true;
                            byte[] bytes = _packages[0];
                            _packages.RemoveAt(0);
                            _remote.BeginSendTo(bytes, 0, bytes.Length, 0, _remoteEP, new AsyncCallback(PipeRemoteSendCallback), null);
                        }
                        else
                        {
                            _sending = false;
                        }
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
                    _remote.EndSendTo(ar);
                    lock (_packages)
                        _sending = false;
                    StartSend();
                    Delay();
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
                        _local.BeginSendTo(remoteRecvBuffer, 0, bytesRead, 0, _localEP, new AsyncCallback(PipeConnectionSendCallback), null);
                    }
                    else
                    {
                        this.Close();
                    }
                    Delay();
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
                    _local.EndSendTo(ar);
                    EndPoint remoteEP = (EndPoint)(new IPEndPoint(((IPEndPoint)_remoteEP).Address, ((IPEndPoint)_remoteEP).Port));
                    _remote.BeginReceiveFrom(this.remoteRecvBuffer, 0, RecvSize, 0, ref remoteEP,
                        new AsyncCallback(PipeRemoteReceiveCallback), null);
                    Delay();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                    this.Close();
                }
            }

            private void Delay()
            {
                lock (this)
                {
                    _expires = DateTime.Now.AddMinutes(1);
                }
            }

            public void Close(bool reportClose = true)
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
                if (reportClose && OnClose != null)
                {
                    OnClose(this, new EventArgs());
                }
            }
        }
    }
}
