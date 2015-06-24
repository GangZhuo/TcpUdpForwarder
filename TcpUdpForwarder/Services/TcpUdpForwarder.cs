using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;

using TcpUdpForwarder.Controller;

namespace TcpUdpForwarder.Services
{
    partial class TcpUdpForwarder : ServiceBase
    {
        ForwarderController controller;

        public TcpUdpForwarder()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            controller = new ForwarderController(true);
            controller.Start();
        }

        protected override void OnStop()
        {
            controller.Stop();
            controller = null;
        }
    }
}
