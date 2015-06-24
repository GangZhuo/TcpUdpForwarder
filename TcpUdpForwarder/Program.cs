using System;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using System.ServiceProcess;

using TcpUdpForwarder.Model;
using TcpUdpForwarder.Controller;
using TcpUdpForwarder.View;

namespace TcpUdpForwarder
{
    class Program
    {
        static bool is_svc = false;

        static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                foreach (string s in args)
                {
                    if (s == "--svc")
                    {
                        is_svc = true;
                        break;
                    }
                }
            }
            //Directory.SetCurrentDirectory(Application.StartupPath);
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

            Logging.OpenLogFile(is_svc);
            ForwarderController controller = null;
            if (is_svc)
            {
                ServiceBase[] servicesToRun;
                servicesToRun = new ServiceBase[] 
                { 
                    new Services.TcpUdpForwarder() 
                };
                ServiceBase.Run(servicesToRun);
            }
            else
            {
                controller = new ForwarderController(is_svc);
                SSForwardView viewController = new SSForwardView(controller);
                controller.Start();
                Application.Run();
            }
        }
    }
}
