using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Sockets;

using TcpUdpForwarder.Model;

namespace TcpUdpForwarder.Controller
{
    public class SSForwardController
    {
        public const string Version = "1.0.0";

        Config _config;
        TcpForwarder _tcpForwarder;
        UdpForwarder _udpForwarder;

        public event EventHandler EnableStatusChanged;
        public event EventHandler ConfigChanged;
        public event ErrorEventHandler Errored;

        public SSForwardController()
        {
            _config = Config.Load();
        }

        public SSForwardController(Config config)
        {
            _config = config;
        }

        public void Start()
        {
            Reload();
        }

        public void Stop()
        {
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
        }

        protected void Reload()
        {
            _config = Config.Load();
            if (_tcpForwarder != null)
                _tcpForwarder.Stop();
            if (_udpForwarder != null)
                _udpForwarder.Stop();
            if (!_config.enabled)
            {
                ReportConfigChanged();
                return;
            }
            try
            {
                ServerInfo server = GetCurrentServer();
                _tcpForwarder = new TcpForwarder(server);
                _tcpForwarder.Start();
                _udpForwarder = new UdpForwarder(server);
                _udpForwarder.Start();
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
            ReportConfigChanged();
        }

        protected void ReportError(Exception e)
        {
            if (Errored != null)
            {
                Errored(this, new ErrorEventArgs(e));
            }
        }

        protected void ReportConfigChanged()
        {
            if (ConfigChanged != null)
            {
                ConfigChanged(this, new EventArgs());
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
            if (EnableStatusChanged != null)
            {
                EnableStatusChanged(this, new EventArgs());
            }
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
