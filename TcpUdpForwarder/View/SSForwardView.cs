using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

using TcpUdpForwarder.Model;
using TcpUdpForwarder.Controller;
using TcpUdpForwarder.Properties;

namespace TcpUdpForwarder.View
{
    public class SSForwardView
    {
        private SSForwardController _controller;
        private NotifyIcon _notifyIcon;
        private ContextMenu _contextMenu;
        private MenuItem _enableItem;
        private MenuItem _serversItem;
        private MenuItem _serversGroupSeperatorItem;
        private MenuItem _editServersItem;
        private ConfigForm _configForm;
        private bool _isFirstRun;

        public SSForwardView(SSForwardController controller)
        {
            _controller = controller;
            LoadMenu();

            _controller.EnableStatusChanged += _controller_EnableStatusChanged;
            _controller.ConfigChanged += _controller_ConfigChanged;
            _controller.Errored += _controller_Errored;
            
            _notifyIcon = new NotifyIcon();
            UpdateTrayIcon();
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenu = _contextMenu;

            LoadCurrentConfiguration();
            if (controller.GetConfiguration().isDefault)
            {
                _isFirstRun = true;
                ShowConfigForm();
            }
        }

        private void _controller_EnableStatusChanged(object sender, EventArgs e)
        {
            _enableItem.Checked = _controller.GetConfiguration().enabled;
        }

        private void _controller_ConfigChanged(object sender, EventArgs e)
        {
            LoadCurrentConfiguration();
            UpdateTrayIcon();
        }

        private void _controller_Errored(object sender, System.IO.ErrorEventArgs e)
        {
            MessageBox.Show(e.GetException().ToString(), String.Format("TCP(UDP) forwarder Error: {0}", e.GetException().Message));
        }

        private void ShowFirstTimeBalloon()
        {
            if (_isFirstRun)
            {
                _notifyIcon.BalloonTipTitle = "TCP(UDP) forwarder is here";
                _notifyIcon.BalloonTipText = "You can turn on/off in the context menu";
                _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                _notifyIcon.ShowBalloonTip(0);
                _isFirstRun = false;
            }
        }

        private void LoadCurrentConfiguration()
        {
            Config config = _controller.GetConfiguration();
            UpdateServersMenu();
            _enableItem.Checked = config.enabled;
        }

        private void UpdateServersMenu()
        {
            var items = _serversItem.MenuItems;
            while (items[0] != _serversGroupSeperatorItem)
            {
                items.RemoveAt(0);
            }

            Config configuration = _controller.GetConfiguration();
            for (int i = 0; i < configuration.servers.Count; i++)
            {
                ServerInfo server = configuration.servers[i];
                MenuItem item = new MenuItem(server.FriendlyName());
                item.Tag = i;
                item.Click += AServerItem_Click;
                items.Add(i, item);
            }

            if (configuration.index >= 0 && configuration.index < configuration.servers.Count)
            {
                items[configuration.index].Checked = true;
            }
        }

        private void ShowConfigForm()
        {
            if (_configForm != null)
            {
                _configForm.Activate();
            }
            else
            {
                _configForm = new ConfigForm(_controller);
                _configForm.Show();
                _configForm.FormClosed += _configForm_FormClosed;
            }
        }

        private void _configForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            _configForm = null;
            ShowFirstTimeBalloon();
        }

        private void UpdateTrayIcon()
        {
            int dpi;
            Graphics graphics = Graphics.FromHwnd(IntPtr.Zero);
            dpi = (int)graphics.DpiX;
            graphics.Dispose();
            Bitmap icon = null;
            if (dpi < 97)
            {
                // dpi = 96;
                icon = Resources.f16;
            }
            else if (dpi < 121)
            {
                // dpi = 120;
                icon = Resources.f20;
            }
            else
            {
                icon = Resources.f24;
            }
            Config config = _controller.GetConfiguration();
            bool enabled = config.enabled;
            if (!enabled)
            {
                Bitmap iconCopy = new Bitmap(icon);
                for (int x = 0; x < iconCopy.Width; x++)
                {
                    for (int y = 0; y < iconCopy.Height; y++)
                    {
                        Color color = icon.GetPixel(x, y);
                        iconCopy.SetPixel(x, y, Color.FromArgb((byte)(color.A / 1.25), color.R, color.G, color.B));
                    }
                }
                icon = iconCopy;
            }
            _notifyIcon.Icon = Icon.FromHandle(icon.GetHicon());

            // we want to show more details but notify icon title is limited to 63 characters
            string text = "TCP(UDP) forwarder" + " " + SSForwardController.Version
                + "\n" + config.GetCurrentServer().FriendlyName();
            _notifyIcon.Text = text.Substring(0, Math.Min(63, text.Length));
        }

        private void LoadMenu()
        {
            _contextMenu = new ContextMenu(new MenuItem[] {
                _enableItem = new MenuItem("Enable", new EventHandler(this.EnableItem_Click)),
                new MenuItem("-"),
                _serversItem = new MenuItem("Servers", new MenuItem[] {
                    this._serversGroupSeperatorItem = new MenuItem("-"),
                    this._editServersItem = new MenuItem("Edit Servers...", new EventHandler(this.EditServersItem_Click)),
                }),
                new MenuItem("-"),
                new MenuItem("Show Logs...", new EventHandler(this.ShowLogItem_Click)),
                new MenuItem("About...", new EventHandler(this.AboutItem_Click)),
                new MenuItem("-"),
                new MenuItem("Quit", new EventHandler(this.Quit_Click))
            });
        }

        private void EnableItem_Click(object sender, EventArgs e)
        {
            _controller.ToggleEnable(!_enableItem.Checked);
        }

        private void AServerItem_Click(object sender, EventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            _controller.SelectServerIndex((int)item.Tag);
        }

        private void EditServersItem_Click(object sender, EventArgs e)
        {
            ShowConfigForm();
        }

        private void ShowLogItem_Click(object sender, EventArgs e)
        {
            string argument = Logging.LogFile;
            System.Diagnostics.Process.Start("notepad.exe", argument);
        }

        private void AboutItem_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("https://github.com/GangZhuo/TcpUdpForwarder");
            }
            catch { }
        }

        private void Quit_Click(object sender, EventArgs e)
        {
            _controller.Stop();
            _notifyIcon.Visible = false;
            Application.Exit();
        }

    }
}
