using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using TcpUdpForwarder.Model;

namespace TcpUdpForwarder.Controller
{
    public class UdpForwarder
    {
        Socket _socket;
        ServerInfo _server;
        UdpPipe _pipe;

        public UdpForwarder(ServerInfo server)
        {
            _server = server;
            this._pipe = new UdpPipe(_server);
        }

        private bool CheckIfPortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] ipEndPoints = ipProperties.GetActiveUdpListeners();

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
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                IPEndPoint localEndPoint = null;
                localEndPoint = new IPEndPoint(IPAddress.Any, _server.localPort);

                // Bind the socket to the local endpoint and listen for incoming connections.
                _socket.Bind(localEndPoint);

                // Start an asynchronous socket to listen for connections.
                Console.WriteLine("UDP listen on " + localEndPoint.ToString());
                EndPoint remoteEP = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
                byte[] buf = new byte[4096];
                object[] state = new object[] {
                    _socket,
                    buf
                };
                _socket.BeginReceiveFrom(buf, 0, buf.Length, SocketFlags.None, ref remoteEP, new AsyncCallback(receiveFrom), state);
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

        private void receiveFrom(IAsyncResult ar)
        {
            object[] state = (object[])ar.AsyncState;
            Socket socket = (Socket)state[0];
            byte[] buf = (byte[])state[1];
            EndPoint remoteEP = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
            try
            {
                int bytesRead = socket.EndReceiveFrom(ar, ref remoteEP);
                if (_pipe.Handle(buf, bytesRead, socket, remoteEP))
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
                    remoteEP = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
                    socket.BeginReceiveFrom(buf, 0, buf.Length, SocketFlags.None, ref remoteEP, new AsyncCallback(receiveFrom), state);
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
