using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;  // debug only, for Encoding.ASCII.GetString()
using System.Threading;
using Bloom.Publish.BloomPUB.wifi;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// Helper class to listen for a single packet from the Android. Construct an instance to start
    /// listening (on another thread); hook NewMessageReceived to receive a packet each time a client sends it.
    /// </summary>
    class BloomReaderUDPListener
    {
        // must match BloomReader.NewBookListenerService.desktopPort
        // and be different from WiFiAdvertiser.Port and port in BloomReaderPublisher.SendBookToWiFi
        private int _portToListen = 5915;
        Thread _listeningThread;
        public event EventHandler<AndroidMessageArgs> NewMessageReceived;

        // Client by which we receive replies to broadcast book advert. It listens on all network
        // interfaces on the expected listening port.
        UdpClient _clientForBookRequestReceive = null;

        private bool _listening;

        //constructor: starts listening.
        public BloomReaderUDPListener()
        {
            _listeningThread = new Thread(ListenForUDPPackages);
            _listeningThread.IsBackground = true;
            _listeningThread.Start();
            _listening = true;
        }

        /// <summary>
        /// Run on a background thread; returns only when done listening.
        /// </summary>
        public void ListenForUDPPackages()
        {
            // UdpClient needs a constructor that specifies more than just the port.
            //
            // If we specify the port only, this article describes how the system proceeds:
            // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.-ctor?view=net-9.0
            //   "This constructor creates an underlying Socket and binds it to the port number
            //    from which you intend to communicate. Use this constructor if you are only
            //    interested in setting the local port number. The underlying service provider
            //    will assign the local IP address."
            // And, similar to what has been observed in Advertiser, the "underlying service
            // provider" sometimes assigns the IP address from the wrong network interface.
            //
            // So, create the endpoint first, setting its port *and* special IP addr 'IPAddress.Any'.
            // This address causes the endpoint to listen for client activity on all network interfaces,
            // bypassing the possibility of the network stack choosing a wrong address:
            // https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.any?view=net-9.0
            // Then base the UdpClient on this endpoint.

            IPEndPoint epForBookRequestReceive = null;

            try
            {
                epForBookRequestReceive = new IPEndPoint(IPAddress.Any, _portToListen);

                if (epForBookRequestReceive == null)
                {
                    EventLog.WriteEntry("Application", "UDPListener, ERROR creating IPEndPoint");
                    return;
                }

                _clientForBookRequestReceive = new UdpClient(epForBookRequestReceive);

                if (_clientForBookRequestReceive == null)
                {
                    EventLog.WriteEntry("Application", "UDPListener, ERROR creating UdpClient");
                    return;
                }
            }
            catch (SocketException e)
            {
                //log then do nothing
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
            }

            // Listener has been created on the port that BloomReader will respond to, and
            // will monitor *all* network interfaces for UDP packets sent to that port.

            while (_listening)
            {
                try
                {
                    // Log our local address and port.
                    if (_clientForBookRequestReceive?.Client?.LocalEndPoint is IPEndPoint localEP)
                    {
                        EventLog.WriteEntry("Application", $"UDP listening will wait for packet on {localEP.Address}, port {localEP.Port}");
                    }

                    byte[] bytes = _clientForBookRequestReceive.Receive(ref epForBookRequestReceive); // waits for packet from Android.

                    // DEBUG ONLY
                    //Debug.WriteLine("UDPListener, got {0} bytes (raising \'NewMessageReceived\'):", bytes.Length);
                    //var bytesToString = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
                    //Debug.WriteLine("  " + bytesToString.Substring(0, bytes.Length));
                    // END DEBUG

                    //raise event
                    NewMessageReceived?.Invoke(this, new AndroidMessageArgs(bytes));
                }
                catch (SocketException se)
                {
                    if (!_listening || se.SocketErrorCode == SocketError.Interrupted)
                    {
                        return; // no problem, we're just closing up shop
                    }
                    throw se;
                }
            }
        }

        public void StopListener()
        {
            if (_listening)
            {
                _listening = false;
                _clientForBookRequestReceive?.Close(); // forcibly end communication
                _clientForBookRequestReceive = null;
            }

            if (_listeningThread == null)
            {
                return;
            }

            // Since we told the listener to close already this shouldn't have to do much (nor be dangerous)
            _listeningThread.Abort();
            _listeningThread.Join(2 * 1000);
            _listeningThread = null;
        }
    }
}
