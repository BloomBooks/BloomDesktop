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
        UdpClient _listener = null;
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
            // UdpClient needs a constructor that specifies not just the port but also the
            // IP address.
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
            // So, create the endpoint first setting its port *and* IP address. Then pass that
            // endpoint into the UdpClient constructor.

            IPEndPoint groupEP = null;

            try
            {
                groupEP = new IPEndPoint(IPAddress.Any, _portToListen);

                if (groupEP == null)
                {
                    Debug.WriteLine("UDPListener, ERROR creating IPEndPoint, bail");
                    return;
                }

                _listener = new UdpClient(groupEP);

                if (_listener == null)
                {
                    Debug.WriteLine("UDPListener, ERROR creating UdpClient, bail");
                    return;
                }
            }
            catch (SocketException e)
            {
                //log then do nothing
                Debug.WriteLine("UDPListener, SocketException-1 = " + e);
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
            }

            // Local endpoint has been created on the port that BloomReader will respond to.
            // And the endpoint's address 'IPAddress.Any' means that *all* network interfaces
            // on this machine will be monitored for UDP packets sent to the designated port.

            while (_listening)
            {
                try
                {
                    // Log our local address and port.
                    if (_listener?.Client?.LocalEndPoint is IPEndPoint localEP)
                    {
                        Debug.WriteLine("UDP listening will wait for packet on {0}, port {1}", localEP.Address, localEP.Port);
                    }

                    byte[] bytes = _listener.Receive(ref groupEP); // waits for packet from Android.

                    // DEBUG ONLY
                    Debug.WriteLine("WM, UDPListener, got {0} bytes (raising \'NewMessageReceived\'):", bytes.Length);
                    var bytesToString = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
                    Debug.WriteLine("  " + bytesToString.Substring(0, bytes.Length));
                    // END DEBUG

                    //raise event
                    NewMessageReceived?.Invoke(this, new AndroidMessageArgs(bytes));
                }
                catch (SocketException se)
                {
                    Debug.WriteLine("UDPListener, SocketException-2 = " + se);
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
                _listener?.Close(); // forcibly end communication
                _listener = null;
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
