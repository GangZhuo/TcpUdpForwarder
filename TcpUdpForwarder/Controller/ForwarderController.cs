using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Sockets;

using TcpUdpForwarder.Model;

namespace TcpUdpForwarder.Controller
{
    public class ForwarderController
    {
        public const string Version = "1.0.0";

        Config _config;
        MgmtListener _mgmtServer;
        TcpForwarder _tcpForwarder;
        UdpForwarder _udpForwarder;
        bool _issvc = false;
        Management _mgmt;

        public event EventHandler EnableStatusChanged;
        public event EventHandler ConfigChanged;
        public event ErrorEventHandler Errored;

        public ForwarderController(bool issvc)
        {
            _issvc = issvc;
            _config = Config.Load();
        }

        public void Start()
        {
            Reload();
        }

        public void Stop()
        {
            if (_mgmtServer != null)
            {
                _mgmtServer.Stop();
                _mgmtServer = null;
            }
            if (_tcpForwarder != null)
            {
                _tcpForwarder.Stop();
                _tcpForwarder = null;
            }
            if (_udpForwarder != null)
            {
                _udpForwarder.Stop();
                _udpForwarder = null;
            }
            if (_mgmt != null)
            {
                _mgmt.OnClose -= _mgmt_OnClose;
                _mgmt.Close();
                _mgmt = null;
            }
        }

        public void Reload()
        {
            MgmtListener oldMgmt = null;
            try
            {
                _config = Config.Load();
                if (_issvc)
                {
                    if (_tcpForwarder != null)
                        _tcpForwarder.Stop();
                    if (_udpForwarder != null)
                        _udpForwarder.Stop();
                    if (_mgmtServer != null && _mgmtServer.mgmtPort != _config.mgmtPort)
                    {
                        oldMgmt = _mgmtServer;
                        _mgmtServer = null;
                    }
                    if (_mgmtServer == null)
                    {
                        _mgmtServer = new MgmtListener(this, _config.mgmtPort);
                        _mgmtServer.Start();
                    }
                    if (!_config.enabled)
                        return;
                    ServerInfo server = GetCurrentServer();
                    _tcpForwarder = new TcpForwarder(server);
                    _tcpForwarder.Start();
                    _udpForwarder = new UdpForwarder(server);
                    _udpForwarder.Start();
                }
                else
                {
                    if (_mgmt == null)
                    {
                        ReConnectMgmt();
                    }
                    else
                    {
                        _mgmt.ReloadService();
                    }
                }
            }
            catch (Exception e)
            {
                // translate Microsoft language into human language
                // i.e. An attempt was made to access a socket in a way forbidden by its access permissions => Port already in use
                if (e is SocketException)
                {
                    SocketException se = (SocketException)e;
                    if (se.SocketErrorCode == SocketError.AccessDenied)
                    {
                        e = new Exception("Port already in use", e);
                    }
                }
                Logging.LogUsefulException(e);
                ReportError(e);
            }
            finally
            {
                if (oldMgmt != null)
                {
                    oldMgmt.Stop();
                }
            }
        }

        private void _mgmt_OnClose(object sender, EventArgs e)
        {
            //ReportError(new Exception("Can't connect to service"));
            ReConnectMgmt();
        }

        void _mgmt_OnStart(object sender, EventArgs e)
        {
            ReportConfigChanged();
        }

        void ReConnectMgmt()
        {
            if (_mgmt != null)
            {
                _mgmt.OnStart -= _mgmt_OnStart;
                _mgmt.OnClose -= _mgmt_OnClose;
                _mgmt.Close();
                _mgmt = null;
            }
            _mgmt = new Management(this, _config.mgmtPort);
            _mgmt.OnStart += _mgmt_OnStart;
            _mgmt.OnClose += _mgmt_OnClose;
            _mgmt.Start();
            Console.WriteLine("Connet to 127.0.0.1:" + _config.mgmtPort);
        }

        public void ReportError(Exception e)
        {
            if (_issvc)
            {
                if (_mgmtServer != null)
                    _mgmtServer.Pipes.ReportError(e);
            }
            else if (Errored != null)
            {
                Errored(this, new ErrorEventArgs(e));
            }
        }

        public void ReportConfigChanged()
        {
            if (_issvc)
            {
                if (_mgmtServer != null)
                    _mgmtServer.Pipes.ReportConfigChanged();
            }
            else if (ConfigChanged != null)
            {
                ConfigChanged(this, new EventArgs());
            }
        }

        public void ReportEnableStatusChanged()
        {
            if (_issvc)
            {
                if (_mgmtServer != null)
                    _mgmtServer.Pipes.ReportEnableStatusChanged();
            }
            else if (EnableStatusChanged != null)
            {
                EnableStatusChanged(this, new EventArgs());
            }
        }

        public ServerInfo GetCurrentServer()
        {
            return _config.GetCurrentServer();
        }

        // always return copy
        public Config GetConfiguration()
        {
            return Config.Load();
        }

        public void SelectServerIndex(int index)
        {
            _config.index = index;
            SaveConfig(_config);
        }

        public void ToggleEnable(bool enabled)
        {
            _config.enabled = enabled;
            SaveConfig(_config);
            ReportEnableStatusChanged();
        }

        public void SaveServers(List<ServerInfo> servers)
        {
            _config.servers = servers;
            SaveConfig(_config);
        }

        protected void SaveConfig(Config newConfig)
        {
            Config.Save(newConfig);
            Reload();
        }

    }
}
