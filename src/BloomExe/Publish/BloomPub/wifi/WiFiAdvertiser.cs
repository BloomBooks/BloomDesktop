using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Bloom.Api;
using Bloom.web;
using SIL.Progress;

namespace Bloom.Publish.BloomPub.wifi
{
    /// <summary>
    /// This class broadcasts a message over the network offering to supply a book to any Android that wants it.
    /// </summary>
    public class WiFiAdvertiser : IDisposable
    {
        // The information we will advertise.
        public string BookTitle;
        private string _bookVersion;

        public string BookVersion
        {
            get { return _bookVersion; }
            set
            {
                _bookVersion = value;
                // In case this gets modified after we start advertising, we need to recompute the advertisement
                // next time we send it. Clearing this makes sure it happens.
                _currentIpAddress = "";
            }
        }
        public string TitleLanguage;

        // Client by which we send out broadcast book adverts. It uses our local IP address (attached
        // to the correct network interface) and the expected port.
        private UdpClient _clientForBookAdvertSend;

        private Thread _thread;
        private IPEndPoint _bcastDestinationEP;
        private IPEndPoint _bcastSourceEP;

        // Our "local IP address" of the network interface used for Bloom network traffic:
        //   - it is the target address for incoming book requests from Androids
        //   - it is also used to derive the destination address for outgoing book advert broadcasts
        private string _ipForReceivingReplies = "";

        // IP address to which outgoing book advert broadcasts are sent, a "directed broadcast address":
        private string _ipForSendingBroadcast = "";

        private string _subnetMask = "";
        private string _currentIpAddress = "";
        private string _previousIpAddress = "";

        // Layout of a row in the IPv4 routing table.
        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_IPFORWARDROW
        {
            public uint dwForwardDest;
            public uint dwForwardMask;
            public uint dwForwardPolicy;
            public uint dwForwardNextHop;
            public int dwForwardIfIndex;
            public int dwForwardType;
            public int dwForwardProto;
            public int dwForwardAge;
            public int dwForwardNextHopAS;
            public int dwForwardMetric1;  // the "interface metric" we need
            public int dwForwardMetric2;
            public int dwForwardMetric3;
            public int dwForwardMetric4;
            public int dwForwardMetric5;
        }

        // Hold a copy of the IPv4 routing table, which we will examine
        // to find which row/route has the lowest "interface metric".
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_IPFORWARDTABLE
        {
            private int dwNumEntries;
            private MIB_IPFORWARDROW table;
        }

        // We use an unmanaged function in the C/C++ DLL "iphlpapi.dll".
        //   - "true": calling this function *can* set an error code,
        //     which will be retrieveable via Marshal.GetLastWin32Error()
        [DllImport("iphlpapi.dll", SetLastError = true)]
        static extern int GetIpForwardTable(IntPtr pIpForwardTable, ref int pdwSize, bool bOrder);

        // Hold relevant network interface attributes.
        private class InterfaceInfo
        {
            public string IpAddr      { get; set; }
            public string Description { get; set; }
            public string NetMask     { get; set; }
            public int Metric         { get; set; }
        }

        // The port on which we advertise.
        // ChorusHub uses 5911 to advertise. Bloom looks for a port for its server at 8089 and 10 following ports.
        // https://en.wikipedia.org/wiki/List_of_TCP_and_UDP_port_numbers shows a lot of ports in use around 8089,
        // but nothing between 5900 and 5931. Decided to use a number similar to ChorusHub.
        private const int _portForBroadcast = 5913; // must match port in BloomReader NewBookListenerService.startListenForUDPBroadcast
        private byte[] _sendBytes; // Data we send in each advertisement packet
        private readonly WebSocketProgress _progress;

        internal WiFiAdvertiser(WebSocketProgress progress)
        {
            _progress = progress;
        }

        public void Start()
        {
            _thread = new Thread(Work);
            _thread.Start();
        }

        public bool Paused { get; set; }

        private void Work()
        {
            _progress.Message(
                idSuffix: "beginAdvertising",
                message: "Advertising book to Bloom Readers on local network..."
            );

            try
            {
                // Advertise indefinitely.
                while (true)
                {
                    if (!Paused)
                    {
                        // Determine remote (broadcast) and local IP addresses. This includes
                        // instantiating the UdpClient, which must be released after it does
                        // the broadcast.
                        // This all takes about 250 millisec on a 1.9 GHz Core i7 Win11 laptop.
                        // It does slow the advertising loop by a quarter second, but the user
                        // experience won't suffer. Even slower machines taking 1000 millisec
                        // or more to do this will still have adverts going out every 2-3 secs.
                        // I don't think that would be a big problem.
                        GetCurrentIpAddresses();

                        UpdateAdvertisementBasedOnCurrentIpAddress();

                        Debug.WriteLine("WM, Work, BeginSend-start"); // TEMPORARY
                        _clientForBookAdvertSend.BeginSend(
                            _sendBytes,
                            _sendBytes.Length,
                            _bcastDestinationEP,
                            SendCallback,
                            _clientForBookAdvertSend
                        );
                        Debug.WriteLine("WM, Work, BeginSend-end"); // TEMPORARY

                        // Release network resources used by the broadcast.
                        _clientForBookAdvertSend.Close();
                        Debug.WriteLine("WM, Work, UdpClient released"); // TEMPORARY
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (SocketException e)
            {
                Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
                EventLog.WriteEntry("Application", "WiFiAdvertiser::Work, SocketException: " + e);
                // Don't know what _progress.Message() is desired here, add as appropriate.
            }
            catch (ThreadAbortException)
            {
                _progress.Message(idSuffix: "Stopped", message: "Stopped Advertising.");
                _clientForBookAdvertSend.Close();
            }
            catch (Exception error)
            {
                // not worth localizing
                _progress.MessageWithoutLocalizing(
                    $"Error in Advertiser: {error.Message}",
                    ProgressKind.Error
                );
            }
        }

        public static void SendCallback(IAsyncResult args) { }

        private void GetCurrentIpAddresses()
        {
            // We must ensure that the local IP address we advertise by the UDP broadcast is the
            // same address from which the network stack makes the broadcast. Gleaning the local
            // IP address from a UdpClient usually does yield the one that is being used, but
            // unfortunately it can be different on some machines. When that happens the remote
            // Android gets the wrong address from the advert, and Desktop won't get the Android
            // book request.
            //
            // To mitigate, instantiate the UdpClient with the IP address of the network interface
            // the network stack will use: the interface having the lowest "interface metric."
            //
            // The PC on which this runs likely has both WiFi and Ethernet. Either can work,
            // but preference is given to WiFi. The reason: although this PC can likely go either
            // way, the Android device only has WiFi. For the book transfer to work both Desktop
            // and Reader must be on the same subnet. If Desktop is using Ethernet it may not be
            // on the same subnet as Reader, especially on larger networks. The chances that both
            // PC and Android are on the same subnet are greatest if both are using WiFi.

            // Examine the network interfaces and determine which will be used for network traffic.
            Debug.WriteLine("WM, GetCurrentIpAddresses, getting src and dest IPs"); // TEMPORARY
            InterfaceInfo ifcResult = GetInterfaceStackWillUse();

            if (ifcResult == null)
            {
                EventLog.WriteEntry("Application", "WiFiAdvertiser, ERROR getting local IP");
                return;
            }

            _ipForReceivingReplies = ifcResult.IpAddr;
            _subnetMask = ifcResult.NetMask;
            string ifaceDesc = ifcResult.Description;

            // Now that we know the IP address and subnet mask in effect, calculate
            // the "directed" broadcast address to use.
            _ipForSendingBroadcast = GetDirectedBroadcastAddress(_ipForReceivingReplies, _subnetMask);
            if (_ipForSendingBroadcast.Length == 0)
            {
                EventLog.WriteEntry("Application", "WiFiAdvertiser, ERROR getting broadcast address");
                return;
            }
            Debug.WriteLine("WM, GetCurrentIpAddresses, got src and dest IPs"); // TEMPORARY

            try
            {
                Debug.WriteLine("WM, GetCurrentIpAddresses, try-begin"); // TEMPORARY
                // Instantiate source endpoint using the local IP address we just got,
                // then use that to create the UdpClient for broadcasting adverts.
                _bcastSourceEP = new IPEndPoint(IPAddress.Parse(_ipForReceivingReplies), _portForBroadcast);
                if (_bcastSourceEP == null)
                {
                    EventLog.WriteEntry("Application", "WiFiAdvertiser, ERROR creating local IPEndPoint");
                    return;
                }

                _clientForBookAdvertSend = new UdpClient(_bcastSourceEP);
                if (_clientForBookAdvertSend == null)
                {
                    EventLog.WriteEntry("Application", "WiFiAdvertiser, ERROR creating UdpClient");
                    return;
                }

                // The doc seems to indicate that EnableBroadcast is required for doing broadcasts.
                // In practice it seems to be required on Mono but not on Windows.
                // This may be fixed in a later version of one platform or the other, but please
                // test both if tempted to remove it.
                _clientForBookAdvertSend.EnableBroadcast = true;

                // Set up destination endpoint.
                _bcastDestinationEP = new IPEndPoint(IPAddress.Parse(_ipForSendingBroadcast), _portForBroadcast);

                // Log key data for tech support.
                EventLog.WriteEntry("Application", $"UDP advertising will use: localIp = {_ipForReceivingReplies}:{_bcastSourceEP.Port} ({ifaceDesc})");
                EventLog.WriteEntry("Application", $"                          subnetMask = {_subnetMask}");
                EventLog.WriteEntry("Application", $"                          remoteIp = {_bcastDestinationEP.Address}:{_bcastDestinationEP.Port}");
                Debug.WriteLine($"      _ipForReceivingReplies = {_ipForReceivingReplies}"); // TEMPORARY
                Debug.WriteLine($"      _ipForSendingBroadcast = {_ipForSendingBroadcast}"); // TEMPORARY
                Debug.WriteLine("WM, GetCurrentIpAddresses, try-end"); // TEMPORARY
            }
            catch (Exception e)
            {
                Debug.WriteLine("WM, GetCurrentIpAddresses, Exception: " + e); // TEMPORARY
                EventLog.WriteEntry("Application", "WiFiAdvertiser::GetCurrentIpAddresses, Exception: " + e);
            }
        }

        /// <summary>
        /// Since this is typically not a real "server", its ipaddress could be assigned dynamically,
        /// and could change each time someone wakes it up.
        /// </summary>
        private void UpdateAdvertisementBasedOnCurrentIpAddress()
        {
            Debug.WriteLine("WM, UABOCIA, begin"); // TEMPORARY
            _currentIpAddress = _ipForReceivingReplies;

            if (_previousIpAddress != _currentIpAddress)
            {
                _previousIpAddress = _currentIpAddress;
                dynamic advertisement = new DynamicJson();
                advertisement.title = BookTitle;
                advertisement.version = BookVersion;
                advertisement.language = TitleLanguage;
                advertisement.protocolVersion = WiFiPublisher.ProtocolVersion;
                advertisement.sender = System.Environment.MachineName;

                // If we do eventually add capability to display the advert as a QR code,
                // the local IP address will need to be included. Might as well add it now.
                advertisement.senderIP = _currentIpAddress;

                _sendBytes = Encoding.UTF8.GetBytes(advertisement.ToString());
                //EventLog.WriteEntry("Application", "Serving at http://" + _currentIpAddress + ":" + ChorusHubOptions.MercurialPort, EventLogEntryType.Information);
            }
            Debug.WriteLine("WM, UABOCIA, done"); // TEMPORARY
        }

        // Survey the network interfaces and determine which one, if any, the network
        // stack will use for network traffic.
        //   - During the assessment the current leading WiFi candidate will be held in
        //     'IfaceWifi', and similarly the current best candidate for Ethernet will
        //     be in 'IfaceEthernet'.
        //   - After assessment inform calling code of the winner by returning an enum
        //     indicating which of the candidate structs to draw from: WiFi, Ethernet,
        //     or neither.
        //
        private InterfaceInfo GetInterfaceStackWillUse()
        {
            InterfaceInfo IfaceWifi = new();
            InterfaceInfo IfaceEthernet = new();
            int currentIfaceMetric;

            // Initialize result structs metric field to the highest possible value
            // so the first interface metric value seen will always replace it.
            IfaceWifi.Metric = int.MaxValue;
            IfaceEthernet.Metric = int.MaxValue;

            // Retrieve all network interfaces that are *active*.
            var allOperationalNetworks = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up).ToArray();

            if (!allOperationalNetworks.Any())
            {
                EventLog.WriteEntry("Application", "WiFiAdvertiser, NO network interfaces are operational");
                return null;
            }

            // Get key attributes of active network interfaces.
            foreach (NetworkInterface ni in allOperationalNetworks)
            {
                // If we can't get IP or IPv4 properties for this interface, skip it.
                var ipProps = ni.GetIPProperties();
                var ipv4Props = ipProps?.GetIPv4Properties();
                if (ipv4Props == null)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation ip in ipProps.UnicastAddresses)
                {
                    // We don't consider IPv6 so filter for IPv4 ('InterNetwork')...
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        // ...And of these we care only about WiFi and Ethernet.
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        {
                            currentIfaceMetric = GetMetricForInterface(ipv4Props.Index);

                            // Save this interface if its metric is lowest we've seen so far.
                            if (currentIfaceMetric < IfaceWifi.Metric)
                            {
                                IfaceWifi.IpAddr = ip.Address.ToString();
                                IfaceWifi.NetMask = ip.IPv4Mask.ToString();
                                IfaceWifi.Description = ni.Description;
                                IfaceWifi.Metric = currentIfaceMetric;
                            }
                        }
                        else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                        {
                            currentIfaceMetric = GetMetricForInterface(ipv4Props.Index);

                            // Save this interface if its metric is lowest we've seen so far.
                            if (currentIfaceMetric < IfaceEthernet.Metric)
                            {
                                IfaceEthernet.IpAddr = ip.Address.ToString();
                                IfaceEthernet.NetMask = ip.IPv4Mask.ToString();
                                IfaceEthernet.Description = ni.Description;
                                IfaceEthernet.Metric = currentIfaceMetric;
                            }
                        }
                    }
                }
            }

            // Active network interfaces have all been assessed.
            //   - The WiFi interface having the lowest metric has been saved in the
            //     WiFi result struct. Note: if no active WiFi interface was seen then
            //     the result struct's metric field will still have its initial value.
            //   - Likewise for Ethernet.
            // Now choose the winner, if there is one:
            //   - If we saw an active WiFi interface, return that
            //   - Else if we saw an active Ethernet interface, return that
            //   - Else there is no winner so return none
            if (IfaceWifi.Metric < int.MaxValue)
            {
                return IfaceWifi;
            }
            if (IfaceEthernet.Metric < int.MaxValue)
            {
                return IfaceEthernet;
            }

            EventLog.WriteEntry("Application", "WiFiAdvertiser, NO suitable network interface found");
            return null;
        }

        // Get a key piece of info ("metric") from the specified network interface.
        // https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getipforwardtable
        //
        // Retrieving the metric is not as simple as grabbing one of the fields in
        // the network interface. The metric resides in the network stack routing
        // table. One of the interface fields ("Index") is also in the routing table
        // and is how we correlate the two.
        //   - Calling code (walking the interface collection) passes in the index
        //     of the interface whose "best" metric it wants.
        //   - This function walks the routing table looking for all rows (each of
        //     which is a route) containing that index. It notes the metric in each
        //     route and returns the lowest among all routes/rows for the interface.
        //
        int GetMetricForInterface(int interfaceIndex)
        {
            // Initialize to "worst" possible metric (Win10 Pro: 2^31 - 1).
            // It can only get better from there!
            int bestMetric = int.MaxValue;

            // Preliminary: call with a null buffer ('size') to learn how large a
            // buffer is needed to hold a copy of the routing table.
            int size = 0;
            GetIpForwardTable(IntPtr.Zero, ref size, false);

            IntPtr tableBuf;

            try
            {
                // 'size' now shows how large a buffer is needed, so allocate it.
                tableBuf = Marshal.AllocHGlobal(size);
            }
            catch (OutOfMemoryException e)
            {
                EventLog.WriteEntry("Application", "GetMetricForInterface, ERROR creating buffer: " + e);
                return bestMetric;
            }

            try
            {
                // Copy the routing table into buffer for examination.
                int error = GetIpForwardTable(tableBuf, ref size, false);
                if (error != 0)
                {
                    // Something went wrong so bail.
                    // It is tempting to add a dealloc call here, but don't. The
                    // dealloc in the 'finally' block *will* be done (I checked).
                    EventLog.WriteEntry("Application", $"GetMetricForInterface, ERROR {error} getting table, returning {bestMetric}");
                    return bestMetric;
                }

                // Get number of routing table entries.
                int numEntries = Marshal.ReadInt32(tableBuf);

                // Advance pointer past the integer to point at 1st row.
                IntPtr rowPtr = IntPtr.Add(tableBuf, 4);

                // Walk the routing table looking for rows involving the the network
                // interface passed in. For each such row/route, check the metric.
                // If it is lower than the lowest we've yet seen, save it to become
                // the new benchmark.
                for (int i = 0; i < numEntries; i++)
                {
                    MIB_IPFORWARDROW row = Marshal.PtrToStructure<MIB_IPFORWARDROW>(rowPtr);
                    if (row.dwForwardIfIndex == interfaceIndex)
                    {
                        bestMetric = Math.Min(bestMetric, row.dwForwardMetric1);
                    }
                    rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf<MIB_IPFORWARDROW>());
                }
            }
            catch (Exception e)
            {
                if (e is AccessViolationException || e is MissingMethodException)
                {
                    EventLog.WriteEntry("Application", "GetMetricForInterface, ERROR walking table: " + e);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tableBuf);
            }

            return bestMetric;
        }

        // Construct a network interface's directed broadcast address.
        //
        // This is different from 255.255.255.255, the "limited broadcast address" which
        // applies only to 0.0.0.0, the "zero network" beyond which broadcasts will never
        // be propagated (https://en.wikipedia.org/wiki/Broadcast_address).
        // But a directed broadcast CAN be forwarded to other subnets, if the network's
        // routers are configured to allow it. They often aren't but at least the potential
        // is there. For a thorough explanation see:
        // https://www.practicalnetworking.net/stand-alone/local-broadcast-vs-directed-broadcast/
        //
        // So, to maximize the chances that a Reader will hear book adverts we use the
        // directed broadcast address. It is generated from an interface's IP address and
        // subnet mask, both of which are passed in to this function.
        //
        // Bit-wise rationale:
        //       The subnet mask indicates how to handle the IP address bits.
        //     - '1' mask bits: the "network address" portion of the IP address.
        //       The broadcast address aims at the same network so just copy the IP
        //       address bits into the same positions of the broadcast address.
        //       'ORing' IP bits with 1-mask-bits-inverted-to-0 keeps them all
        //       unchanged.
        //     - '0' mask bits: the "host ID" portion of the IP address. We want to
        //       fill this portion of the broadcast address with '1's so all hosts on
        //       the subnet will see the transmission.
        //       'ORing' IP bits with 0-mask-bits-inverted-to-1 makes them all 1.
        //
        // Function operation:
        //     convert IP address and subnet mask strings to byte arrays
        //     create byte array to hold broadcast address result
        //     FOR each IP address octet and corresponding subnet mask octet, starting with most significant
        //         compute broadcast address octet per "Bit-wise rationale" above
        //     END
        //     convert broadcast address byte array to IP address string and return it
        //
        // Note: the local IP and mask inputs are not explicitly checked. Any issues they
        // may have will become apparent by the catch block firing. 
        //
        private string GetDirectedBroadcastAddress(string ipIn, string maskIn)
        {
            try
            {
                IPAddress ipAddress = IPAddress.Parse(ipIn);
                IPAddress subnetMask = IPAddress.Parse(maskIn);
                byte[] ipBytes = ipAddress.GetAddressBytes();
                byte[] maskBytes = subnetMask.GetAddressBytes();

                if (ipBytes.Length != maskBytes.Length)
                {
                    EventLog.WriteEntry("Application", $"BroadcastAddress, ERROR length mismatch, IP vs mask: {ipBytes.Length}, {maskBytes.Length}");
                    return "";
                }

                byte[] bcastBytes = new byte[ipBytes.Length];
                for (int i = 0; i < ipBytes.Length; i++)
                {
                    bcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                }

                return new IPAddress(bcastBytes).ToString();
            }
            catch (Exception e)
            {
                EventLog.WriteEntry("Application", "BroadcastAddress, Exception: " + e);
                return "";
            }
        }

        public void Stop()
        {
            if (_thread == null)
                return;

            //EventLog.WriteEntry("Application", "Advertiser Stopping...", EventLogEntryType.Information);
            _thread.Abort();
            _thread.Join(2 * 1000);
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
