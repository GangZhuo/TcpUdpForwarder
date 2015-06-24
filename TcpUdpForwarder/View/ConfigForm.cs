using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using Microsoft.Win32;

using TcpUdpForwarder.Model;
using TcpUdpForwarder.Controller;
using TcpUdpForwarder.Properties;

namespace TcpUdpForwarder.View
{
    public partial class ConfigForm : Form
    {
        private ForwarderController controller;

        // this is a copy of configuration that we are working on
        private Config _modifiedConfiguration;
        private int _oldSelectedIndex = -1;

        public ConfigForm(ForwarderController controller)
        {
            this.Font = System.Drawing.SystemFonts.MessageBoxFont;
            InitializeComponent();

            // a dirty hack
            this.ServersListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.PerformLayout();

            this.Icon = Icon.FromHandle(Resources.f128.GetHicon());

            this.controller = controller;
            controller.ConfigChanged += controller_ConfigChanged;

            LoadCurrentConfiguration();
        }

        private void controller_ConfigChanged(object sender, EventArgs e)
        {
            LoadCurrentConfiguration();
        }
        
        private void ShowWindow()
        {
            this.Opacity = 1;
            this.Show();
            IPTextBox.Focus();
        }

        private bool SaveOldSelectedServer()
        {
            try
            {
                if (_oldSelectedIndex == -1 || _oldSelectedIndex >= _modifiedConfiguration.servers.Count)
                {
                    return true;
                }
                ServerInfo server = new ServerInfo
                {
                    server = IPTextBox.Text,
                    serverPort = int.Parse(ServerPortTextBox.Text),
                    localPort = int.Parse(BindPortTextBox.Text),
                };
                Config.CheckServer(server);
                _modifiedConfiguration.servers[_oldSelectedIndex] = server;
                
                return true;
            }
            catch (FormatException)
            {
                MessageBox.Show("Illegal port number format");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            return false;
        }

        private void LoadSelectedServer()
        {
            if (ServersListBox.SelectedIndex >= 0 && ServersListBox.SelectedIndex < _modifiedConfiguration.servers.Count)
            {
                ServerInfo server = _modifiedConfiguration.servers[ServersListBox.SelectedIndex];

                IPTextBox.Text = server.server;
                ServerPortTextBox.Text = server.serverPort.ToString();
                BindPortTextBox.Text = server.localPort.ToString();
                ServerGroupBox.Visible = true;
            }
            else
            {
                ServerGroupBox.Visible = false;
            }
        }

        private void LoadConfiguration(Config configuration)
        {
            ServersListBox.Items.Clear();
            foreach (ServerInfo server in _modifiedConfiguration.servers)
            {
                ServersListBox.Items.Add(server.FriendlyName());
            }
        }

        private void LoadCurrentConfiguration()
        {
            _modifiedConfiguration = controller.GetConfiguration();
            LoadConfiguration(_modifiedConfiguration);
            _oldSelectedIndex = _modifiedConfiguration.index;
            ServersListBox.SelectedIndex = _modifiedConfiguration.index;
            LoadSelectedServer();
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {

        }

        private void ServersListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_oldSelectedIndex == ServersListBox.SelectedIndex)
            {
                // we are moving back to oldSelectedIndex or doing a force move
                return;
            }
            if (!SaveOldSelectedServer())
            {
                // why this won't cause stack overflow?
                ServersListBox.SelectedIndex = _oldSelectedIndex;
                return;
            }
            LoadSelectedServer();
            _oldSelectedIndex = ServersListBox.SelectedIndex;
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            if (!SaveOldSelectedServer())
            {
                return;
            }
            ServerInfo server = Config.GetDefaultServer();
            _modifiedConfiguration.servers.Add(server);
            LoadConfiguration(_modifiedConfiguration);
            ServersListBox.SelectedIndex = _modifiedConfiguration.servers.Count - 1;
            _oldSelectedIndex = ServersListBox.SelectedIndex;
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            _oldSelectedIndex = ServersListBox.SelectedIndex;
            if (_oldSelectedIndex >= 0 && _oldSelectedIndex < _modifiedConfiguration.servers.Count)
            {
                _modifiedConfiguration.servers.RemoveAt(_oldSelectedIndex);
            }
            if (_oldSelectedIndex >= _modifiedConfiguration.servers.Count)
            {
                // can be -1
                _oldSelectedIndex = _modifiedConfiguration.servers.Count - 1;
            }
            ServersListBox.SelectedIndex = _oldSelectedIndex;
            LoadConfiguration(_modifiedConfiguration);
            ServersListBox.SelectedIndex = _oldSelectedIndex;
            LoadSelectedServer();
        }

        private void OKButton_Click(object sender, EventArgs e)
        {
            if (!SaveOldSelectedServer())
            {
                return;
            }
            if (_modifiedConfiguration.servers.Count == 0)
            {
                MessageBox.Show("Please add at least one server");
                return;
            }
            controller.SaveServers(_modifiedConfiguration.servers);
            this.Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void ConfigForm_Shown(object sender, EventArgs e)
        {
            IPTextBox.Focus();
        }

        private void ConfigForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            controller.ConfigChanged -= controller_ConfigChanged;
        }

    }
}
