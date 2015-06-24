using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Text;

namespace TcpUdpForwarder
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            InitializeComponent();
        }

        protected override void OnBeforeInstall(IDictionary savedState)
        {
            string parameter = "--svc";
            var assemblyPath = Context.Parameters["assemblypath"];
            assemblyPath += "\" \"" + parameter;
            Context.Parameters["assemblypath"] = assemblyPath;
            base.OnBeforeInstall(savedState);
        }
    }
}
