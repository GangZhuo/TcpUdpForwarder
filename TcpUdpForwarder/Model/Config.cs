using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.Text;

namespace TcpUdpForwarder.Model
{
    public class Config
    {
        private static string CONFIG_FILE = "config.xml";

        public bool isDefault;
        public bool enabled;
        public List<ServerInfo> servers;
        public int index;
        public int mgmtPort;

        public Config()
        {
            isDefault = true;
            enabled = false;
            servers = new List<ServerInfo>();
            servers.Add(GetDefaultServer());
            index = 0;
            mgmtPort = 4434;
        }

        public ServerInfo GetCurrentServer()
        {
            if (servers != null && index >= 0 && index < servers.Count)
                return servers[index];
            return null;
        }

        public static Config Load()
        {
            Config config = new Config();

            if (File.Exists(CONFIG_FILE))
            {
                config.isDefault = false;
                XmlDocument xmldoc = new XmlDocument();
                xmldoc.Load(CONFIG_FILE);
                XmlNode n;

                n = xmldoc.SelectSingleNode("/config/enabled");
                if (n != null) config.enabled = Convert.ToBoolean(n.Attributes["value"].Value);

                n = xmldoc.SelectSingleNode("/config/index");
                if (n != null) config.index = Convert.ToInt32(n.Attributes["value"].Value);

                n = xmldoc.SelectSingleNode("/config/mgmt_port");
                if (n != null) config.mgmtPort = Convert.ToInt32(n.Attributes["value"].Value);

                config.servers.Clear();
                XmlNodeList ns = xmldoc.SelectNodes("/config/server");
                foreach (XmlNode sn in ns)
                {
                    ServerInfo s = GetDefaultServer();
                    XmlAttribute attr;

                    attr = sn.Attributes["server"];
                    if (attr !=null) s.server = attr.Value;

                    attr = sn.Attributes["server_port"];
                    if (attr != null) s.serverPort = Convert.ToInt32(attr.Value);

                    attr = sn.Attributes["bind_port"];
                    if (attr != null) s.localPort = Convert.ToInt32(attr.Value);

                    config.servers.Add(s);
                }
            }
            return config;
        }

        public static void Save(Config config)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = Encoding.UTF8;
            settings.Indent = true;
            using (FileStream fs = new FileStream(CONFIG_FILE, FileMode.Create, FileAccess.Write))
            {
                using (XmlWriter writer = XmlWriter.Create(fs, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("config");

                    #region

                    writer.WriteStartElement("enabled");
                    writer.WriteStartAttribute("value");
                    writer.WriteString(config.enabled.ToString().ToLower());
                    writer.WriteEndAttribute();
                    writer.WriteEndElement();

                    writer.WriteStartElement("index");
                    writer.WriteStartAttribute("value");
                    writer.WriteString(config.index.ToString());
                    writer.WriteEndAttribute();
                    writer.WriteEndElement();

                    writer.WriteStartElement("mgmt_port");
                    writer.WriteStartAttribute("value");
                    writer.WriteString(config.mgmtPort.ToString());
                    writer.WriteEndAttribute();
                    writer.WriteEndElement();

                    foreach (ServerInfo s in config.servers)
                    {
                        writer.WriteStartElement("server");
                        writer.WriteStartAttribute("server");
                        writer.WriteString(s.server);
                        writer.WriteEndAttribute();
                        writer.WriteStartAttribute("server_port");
                        writer.WriteString(s.serverPort.ToString());
                        writer.WriteEndAttribute();
                        writer.WriteStartAttribute("bind_port");
                        writer.WriteString(s.localPort.ToString());
                        writer.WriteEndAttribute();
                        writer.WriteEndElement();
                    }

                    #endregion

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            config.isDefault = false;
        }

        public static ServerInfo GetDefaultServer()
        {
            return new ServerInfo()
            {
                server = "104.224.129.11",
                serverPort = 443,
                localPort = 443,
            };
        }

        public static void CheckServer(ServerInfo server)
        {
            CheckPort(server.serverPort);
            CheckPort(server.localPort);
            CheckServer(server.server);
        }

        public static void CheckPort(int port)
        {
            if (port <= 0 || port > 65535)
            {
                throw new ArgumentException("Port out of range");
            }
        }

        private static void CheckServer(string server)
        {
            if (string.IsNullOrEmpty(server))
            {
                throw new ArgumentException("Server IP can not be blank");
            }
        }

    }
}
