using System;

namespace TcpUdpForwarder.Model
{
    public class ServerInfo
    {
        /// <summary>
        /// forward to this server
        /// </summary>
        public string server;

        /// <summary>
        /// forward to this server port
        /// </summary>
        public int serverPort;

        /// <summary>
        /// bind on this port
        /// </summary>
        public int localPort;

        public string FriendlyName()
        {
            if (string.IsNullOrEmpty(server))
                return "New server";
            return server + ":" + serverPort + " (bind " + localPort + ")";
        }

    }
}
