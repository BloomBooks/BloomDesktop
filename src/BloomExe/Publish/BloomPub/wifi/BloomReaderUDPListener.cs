using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
//using System.Text;  // debug only, for Encoding.ASCII.GetString()
using System.Threading;
using System.Threading.Tasks;
using Bloom.Publish.BloomPUB.wifi;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// Helper class to listen for a single packet from the Android. Construct an instance to start
    /// listening (on another thread); hook NewMessageReceived to receive a packet each time a client sends it.
    /// </summary>
    class BloomReaderUDPListener : IDisposable
    {
        // must match BloomReader.NewBookListenerService.desktopPort
        // and be different from WiFiAdvertiser.Port and port in BloomReaderPublisher.SendBookToWiFi
        private int _portToListen = 5915;
        public event EventHandler<AndroidMessageArgs> NewMessageReceived;
        private CancellationTokenSource _cts;

        // Client by which we receive replies to broadcast book advert. It listens on all network
        // interfaces on the expected listening port.
        UdpClient _clientForBookRequestReceive = null;

        //constructor: starts listening.
        public BloomReaderUDPListener()
        {
            _cts = new CancellationTokenSource();
            Debug.WriteLine("UDPListener, got _cts, calling Run()"); // WM, TEMPORARY
            Task.Run(() => ListenAsync(_cts.Token));
        }

        /// <summary>
        /// Listens as an async task for UDP packets from the Android Bloom Reader.
        /// </summary>
        /// <remarks>
        /// This was suggested by github copilot as a way to do async UDP listening with cancellation support.
        /// </remarks>
        private async Task ListenAsync(CancellationToken ct)
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
            catch (Exception e)
            {
                EventLog.WriteEntry("Application", "UDPListener, ERROR creating UdpClient: " + e);
                Debug.WriteLine("UDPListener, EXCEPTION for UdpClient: " + e); // WM, TEMPORARY
                return;
            }

            using (_clientForBookRequestReceive)
            {
                Debug.WriteLine("UDPListener, entering loop"); // WM, TEMPORARY
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        // UdpClient.ReceiveAsync does not accept a CancellationToken directly.
                        // Workaround: Use Task.WhenAny to support cancellation.
                        var receiveTask = _clientForBookRequestReceive.ReceiveAsync();
                        var completedTask = await Task.WhenAny(
                            receiveTask,
                            Task.Delay(Timeout.Infinite, ct)
                        );
                        Debug.WriteLine("UDPListener, got something"); // WM, TEMPORARY
                        if (completedTask == receiveTask)
                        {
                            var result = receiveTask.Result;
                            Debug.WriteLine("UDPListener, got advert"); // WM, TEMPORARY
                            NewMessageReceived?.Invoke(this, new AndroidMessageArgs(result.Buffer));
                        }
                        else
                        {
                            // Cancellation requested
                            Debug.WriteLine("UDPListener, got cancel request"); // WM, TEMPORARY
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("UDPListener, got OperationCanceledException"); // WM, TEMPORARY
                        break;
                    }
                    catch (SocketException se) when (!ct.IsCancellationRequested)
                    {
                        Debug.WriteLine("UDPListener, got SocketException, rethrowing"); // WM, TEMPORARY
                        throw;
                    }
                }
            }
        }

        public void StopListener()
        {
            Debug.WriteLine("UDPListener, StopListener() starting"); // WM, TEMPORARY
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            Debug.WriteLine("UDPListener, StopListener() done"); // WM, TEMPORARY
        }

        public void Dispose()
        {
            Debug.WriteLine("UDPListener, Dispose() starting"); // WM, TEMPORARY
            _clientForBookRequestReceive.Dispose();
            StopListener();
            Debug.WriteLine("UDPListener, Dispose() done"); // WM, TEMPORARY
        }
    }
}
