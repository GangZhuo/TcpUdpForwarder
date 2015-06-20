using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using System.IO;

using TcpUdpForwarder.Model;
using TcpUdpForwarder.Controller;
using TcpUdpForwarder.View;

namespace TcpUdpForwarder
{
    class Program
    {
        static string _server;
        static int _serverPort;
        static int _localPort;
        static bool _quiet;

        static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                Hashtable _args = new Hashtable();
                if (!ParseArgs(args, _args))
                {
                    Usage();
                    return;
                }
                if (_args.ContainsKey("-V"))
                {
                    Version();
                    return;
                }
                if (_args.ContainsKey("-h")
                    || !_args.ContainsKey("-s")
                    || !_args.ContainsKey("-p")
                    || !_args.ContainsKey("-l")
                    || !int.TryParse((string)_args["-p"], out _serverPort)
                    || !int.TryParse((string)_args["-l"], out _localPort))
                {
                    Usage();
                    return;
                }
                _server = (string)_args["-s"];
                _quiet = _args.ContainsKey("-q");
            }
            else
            {
                _quiet = true;
            }

            Directory.SetCurrentDirectory(Application.StartupPath);

            SSForwardController controller = null;
            if(_quiet)
            {
                HideConsoleWindow();
                Logging.OpenLogFile();
                controller = new SSForwardController();
                SSForwardView viewController = new SSForwardView(controller);
            }
            else
            {
                Config config = new Config();
                config.enabled = true;
                config.index = 0;
                config.servers[0].server = _server;
                config.servers[0].serverPort = _serverPort;
                config.servers[0].localPort = _localPort;
                controller = new SSForwardController(config);
            }
            controller.Start();
            Application.ThreadException += onException;
            Application.Run();
        }

        static void onException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {

        }

        static bool ParseArgs(string[] args, Hashtable _args)
        {
            string option, value;
            if (args != null)
            {
                for (int i = 0; i < args.Length; )
                {
                    option = args[i++];
                    if (!option.StartsWith("-") || option.Length != 2)
                        return false;
                    if ("spl".IndexOf(option[1]) != -1)
                    {
                        value = args[i++];
                        if (value.StartsWith("-"))
                            return false;
                        if (_args.Contains(option))
                            return false;
                        _args.Add(option, value);
                    }
                    else if ("qhV".IndexOf(option[1]) != -1)
                    {
                        if (_args.Contains(option))
                            return false;
                        _args.Add(option, string.Empty);
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        static void Usage()
        {
            Console.WriteLine(@"Usage: SSForward [options]
Valid options are:
	-s <server_host>        hostname or ip of remote server
	-p <local_port>         port number of remote server
	-l <local_port>         port number of local server
	-q                      quiet
	-h                      print usage
	-V                      print version");
        }

        static void Version()
        {
            Console.WriteLine("SSForward " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString());
        }

        static void ShowConsoleWindow()
        {
            IntPtr handle = GetConsoleWindow();
            // Show
            ShowWindow(handle, SW_SHOW);
        }

        static void HideConsoleWindow()
        {
            IntPtr handle = GetConsoleWindow();
            // Hide
            ShowWindow(handle, SW_HIDE);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

    }
}
