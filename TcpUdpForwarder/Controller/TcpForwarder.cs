using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using TcpUdpForwarder.Model;

namespace TcpUdpForwarder.Controller
{
    public class TcpForwarder
    {
        Socket _socket;
        TcpPipe _pipe;
        ServerInfo _server;

        public TcpForwarder(ServerInfo server)
        {
            _server = server;
            this._pipe = new TcpPipe(_server);
        }

        private bool CheckIfPortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] ipEndPoints = ipProperties.GetActiveTcpListeners();

            foreach (IPEndPoint endPoint in ipEndPoints)
            {
                if (endPoint.Port == port)
                {
                    return true;
                }
            }
            return false;
        }

        public void Start()
        {
            if (CheckIfPortInUse(_server.localPort))
                throw new Exception("Port already in use");

            try
            {
                // Create a TCP/IP socket.
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                IPEndPoint localEndPoint = null;
                localEndPoint = new IPEndPoint(IPAddress.Any, _server.localPort);

                // Bind the socket to the local endpoint and listen for incoming connections.
                _socket.Bind(localEndPoint);
                _socket.Listen(1024);


                // Start an asynchronous socket to listen for connections.
                Console.WriteLine("TCP listen on " + localEndPoint.ToString());
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
                if (_pipe.Handle(conn))
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
                    listener.BeginAccept( new AsyncCallback(AcceptCallback), listener);
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
