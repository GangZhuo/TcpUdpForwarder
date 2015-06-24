using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace TcpUdpForwarder.Controller
{
    public class MgmtListener
    {
        ForwarderController controller;
        Socket _socket;
        public int mgmtPort { get; private set; }
        MgmtPipe _pipe;

        public MgmtPipe Pipes
        {
            get { return _pipe; }
        }

        public MgmtListener(ForwarderController controller, int mgmtPort)
        {
            this.controller = controller;
            this.mgmtPort = mgmtPort;
            _pipe = new MgmtPipe(controller);
        }

        public void Start()
        {
            if (Utils.Utils.CheckIfTcpPortInUse(mgmtPort))
                throw new Exception("Port already in use");

            try
            {
                // Create a TCP/IP socket.
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                IPEndPoint localEndPoint = null;
                localEndPoint = new IPEndPoint(IPAddress.Loopback, mgmtPort);

                // Bind the socket to the local endpoint and listen for incoming connections.
                _socket.Bind(localEndPoint);
                _socket.Listen(1024);


                // Start an asynchronous socket to listen for connections.
                Console.WriteLine("Management listen on " + localEndPoint.ToString());
                _socket.BeginAccept(new AsyncCallback(AcceptCallback), _socket);
            }
            catch (SocketException)
            {
                _socket.Close();
                throw;
            }
        }

        public void Stop()
        {
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            Socket listener = (Socket)ar.AsyncState;
            try
            {
                Socket conn = listener.EndAccept(ar);
                if (_pipe.CreatePipe(conn))
                    return;
                // do nothing
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
            finally
            {
                try
                {
                    listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
                }
                catch (ObjectDisposedException)
                {
                    // do nothing
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }
        }

    }
}
