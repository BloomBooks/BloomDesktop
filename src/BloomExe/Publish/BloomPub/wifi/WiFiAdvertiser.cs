using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

        private UdpClient _client;
        private Thread _thread;
        private IPEndPoint _localEP;
        private IPEndPoint _remoteEP;
        private Socket _sock;
        private string _localIp = "";
        private string _remoteIp = "";  // UDP broadcast address, will NOT be 255.255.255.255
        private string _subnetMask = "";
        private string _currentIpAddress = "";
        private string _cachedIpAddress = "";

        // Lists to hold useful information pulled from all network interfaces.
        private List<string> IfTypes = new List<string>();
        private List<string> IfStatuses = new List<string>();
        private List<string> IfIpAddresses = new List<string>();
        private List<string> IfNetmasks = new List<string>();

        // The port on which we advertise.
        // ChorusHub uses 5911 to advertise. Bloom looks for a port for its server at 8089 and 10 following ports.
        // https://en.wikipedia.org/wiki/List_of_TCP_and_UDP_port_numbers shows a lot of ports in use around 8089,
        // but nothing between 5900 and 5931. Decided to use a number similar to ChorusHub.
        private const int Port = 5913; // must match port in BloomReader NewBookListenerService.startListenForUDPBroadcast
        private byte[] _sendBytes; // Data we send in each advertisement packet
        private readonly WebSocketProgress _progress;

        internal WiFiAdvertiser(WebSocketProgress progress)
        {
            _progress = progress;
        }

        public void Start()
        {
            // The doc seems to indicate that EnableBroadcast is required for doing broadcasts.
            // In practice it seems to be required on Mono but not on Windows.
            // This may be fixed in a later version of one platform or the other, but please
            // test both if tempted to remove it.
            // TODO: WM - have I accidentally removed 'EnableBroadcast'?

            _thread = new Thread(Work);
            _thread.Start();
            Debug.WriteLine("WM, WiFiAdvertiser::Start, work thread started, exiting");
        }

        public bool Paused { get; set; }

        private void Work()
        {
            _progress.Message(
                idSuffix: "beginAdvertising",
                message: "Advertising book to Bloom Readers on local network..."
            );

            // WM, DEBUG ONLY: Show this machine's IP addresses and network interfaces.
            ShowIpAddresses();
            ShowNetworkInterfaces();

            // WM, DEBUG ONLY: The existing query function, GetLocalIpAddress(), sometimes returns an
            //                 IP address that is NOT the one being used for network traffic. A new
            //                 function, GetIpAddressOfNetworkIface(), fixes this. Running Bloom on my
            //                 Lenovo laptop shows a discrepancy between the two results here.
            Debug.WriteLine("WiFiAdvertiser::GetLocalIpAddress() returns          " + GetLocalIpAddress());
            Debug.WriteLine("WiFiAdvertiser::GetIpAddressOfNetworkIface() returns " + GetIpAddressOfNetworkIface());

            // Don't let C# pick the network interface -- it sometimes chooses the wrong one.
            // Force it to use the interface corresponding to the known good local IP address,
            // using a raw socket.
            //
            // NOTE: only a multi-homed machine will have more than one network interface
            //       supporting internet connectivity. This will not be common; the typical
            //       BloomDesktop user has just one network-active interface, likely Wi-Fi
            //       but could also be Ethernet.
            //       For now we assume that the machine is single-homed. If this assumption
            //       turns out to be problematic we can revisit and augment the interface
            //       selection process.

            // Since we will use a raw socket we need to know what "local IP" address to give it.
            // We also need to find out, from the network interface, what subnet mask is in effect
            // for that address.
            _localIp = GetIpAddressOfNetworkIface();
            if (_localIp.Length == 0)
            {
                Debug.WriteLine("WM, WiFiAdvertiser::Work, ERROR: can't get local IP address, exiting");
                return;
            }
            _subnetMask = GetNetmaskOfNetworkIface();
            if (_subnetMask.Length == 0)
            {
                Debug.WriteLine("WM, WiFiAdvertiser::Work, ERROR: can't get subnet mask, exiting");
                return;
            }

            // The typical broadcast address (255.255.255.255) doesn't work with a raw socket:
            //      "System.Net.Sockets.SocketException (0x80004005): An attempt was made
            //      to access a socket in a way forbidden by its access permissions"
            // Rather, the broadcast address must be calculated from the local IP address and
            // the subnet mask.
            _remoteIp = BuildBroadcastAddress(_localIp, _subnetMask);
            if (_remoteIp.Length == 0)
            {
                Debug.WriteLine("WM, WiFiAdvertiser::Work, ERROR: can't make broadcast address, exiting");
                return;
            }

            Debug.WriteLine("WiFiAdvertiser::UDP advertising will use: _localIp    = " + _localIp);
            Debug.WriteLine("                                          _subnetMask = " + _subnetMask);
            Debug.WriteLine("                                          _remoteIp   = " + _remoteIp);

            try
            {
                Debug.WriteLine("creating _localEP with port " + Port);
                _localEP = new IPEndPoint(IPAddress.Parse(_localIp), Port);

                Debug.WriteLine("creating _remoteEP with port " + Port);
                _remoteEP = new IPEndPoint(IPAddress.Parse(_remoteIp), Port);

                Debug.WriteLine("creating UDP socket");
                _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                Debug.WriteLine("calling Bind()");
                _sock.Bind(_localEP);

                Debug.WriteLine("begin UDP broadcast advert loop from _localIp = " + _localIp);

                while (true)
                {
                    if (!Paused)
                    {
                        UpdateAdvertisementBasedOnCurrentIpAddress();

                        Debug.WriteLine("WM, WiFiAdvertiser::Work, beginning send");
                        // No need to do this on a separate thread since the only thing *this* thread does
                        // next is sleep. We are using a simple socket, not a UdpClient (more complex), so
                        // the transmission of bytes will be efficient.
                        //_client.BeginSend(
                        //    _sendBytes,
                        //    _sendBytes.Length,
                        //    LocalEP,
                        //    SendCallback,
                        //    _client
                        //);
                        _sock.SendTo(_sendBytes, 0, _sendBytes.Length, SocketFlags.None, _remoteEP);
                    }
                    Debug.WriteLine("WM, WiFiAdvertiser::Work, sent UDP broadcast advert");
                    Thread.Sleep(1000);
                }
            }
            catch (SocketException e)
            {
                Debug.WriteLine("WM, WiFiAdvertiser::Work, SocketException: " + e);
            }
        }

        // Function: for a given local IP address and subnet mask, construct the
        //           broadcast address *that stays within the subnet*.
        //           All octet values in the subnet mask are handled.
        // Algorithm:
        //    1. In the subnet mask find the 1-0 bit boundary
        //    2. For all '255' octets (they will be to the left of the boundary),
        //       copy the corresponding local IP octets into the broadcast address
        //    3. For all '0' octets (they will be to the right of the boundary),
        //       put '255' into the corresponding broadcast address octets
        //    4. For 0 < octet < 255, value of octet starts at 255 and is decremented
        //       for each 1-bit seen according to the following:
        //          scan the octet left to right (most-to-least significant)
        //          if MSb is '1' subtract 2^7; if next is '1' subtract 2^6; etc
        // Assumptions:
        //    - The local IP and mask inputs are valid and do not need to be checked
        //
        private string BuildBroadcastAddress(string ipIn, string maskIn)
        {
            Debug.WriteLine("WM, WiFiAdvertiser::BuildBroadcastAddress, begin");

            // Isolate the octets for both local IP and subnet mask.
            string[] octetsIp = ipIn.Split('.');
            string[] octetsMask = maskIn.Split('.');

            // Find indexes (left-to-right) where mask octets begin.
            //   - most significant octet begins at index 0 (duh)
            //   - the other 3 begin after a '.' (dot) which we find here
            List<int> indexes = new List<int>();
            for (int i = 0; i < maskIn.Length; i++)
            {
                if (maskIn[i] == '.')
                {
                    indexes.Add(i);
                }
            }

            // WM, DEBUG ONLY, BEGIN - dump the arrays
            for (int j = 0; j < 4; j++)
            {
                Debug.WriteLine("  octetsIp[{0}] = {1}", j, octetsIp[j]);
            }
            for (int j = 0; j < 4; j++)
            {
                Debug.WriteLine("  octetsMask[{0}] = {1}", j, octetsMask[j]);
            }
            for (int j = 0; j < 3; j++)
            {
                Debug.WriteLine("  mask: indexes[{0}] = {1}", j, indexes[j]);
            }
            // WM, DEBUG ONLY, END

            // Step 1: find the 1-0 bit boundary in the mask.
            int indexFirstZero = maskIn.IndexOf('0');
            Debug.WriteLine("  indexFirstZero = " + indexFirstZero);

            // Steps 2-4: examine the 4 octets in turn, starting with the most
            // significant one. Based on their value and their position relative to the
            // 1-0 bit boundary, build up the broadcast address string.
            string bcastAddress = "";
            for (int i = 0; i < 4; i++)
            {
                if (octetsMask[i] == "255")
                {
                    bcastAddress += octetsIp[i]; // Step 2
                    Debug.WriteLine("  255: bcastAddress = " + bcastAddress);
                }
                else if (octetsMask[i] == "0")   // Step 3
                {
                    bcastAddress += "255";
                    Debug.WriteLine("    0: bcastAddress = " + bcastAddress);
                }
                else                             // Step 4
                {
                    int octetVal = 255;  // starting value

                    // Convert the mask octet to a numeric so each bit can be assessed.
                    byte maskVal = System.Convert.ToByte(octetsMask[i]);
                    Debug.WriteLine("  maskVal = " + maskVal);

                    for (int j = 0; j < 8; j++)
                    {
                        Debug.WriteLine("  j = {0}, maskVal & (1 << (7-j)) = {1}", j, maskVal & (1 << (7 - j)));
                        int comp = maskVal & (1 << (7 - j));
                        if (comp == (1 << (7 - j)))
                        {
                            octetVal -= (1 << (7 - j));
                            Debug.WriteLine("  octetVal = " + octetVal);
                        }
                        else
                        {
                            Debug.WriteLine("  no change, octetVal = " + octetVal);
                        }
                    }
                    Debug.WriteLine("  final octetVal = " + octetVal);
                    bcastAddress += octetVal.ToString();
                    Debug.WriteLine("  mix: bcastAddress = " + bcastAddress);
                }
                if (i < 3)   // no dot after the final byte
                {
                    bcastAddress += ".";
                }
            }
            Debug.WriteLine("WM, WiFiAdvertiser::BuildBroadcastAddress, done");
            return bcastAddress;
        }

        // WM  *** DEBUG ONLY ***
        // Show all IP addresses of a host computer.
        //
        private void ShowIpAddresses()
        {
            String hostName = Dns.GetHostName();
            Debug.WriteLine("WM, WiFiAdvertiser::ShowIpAddresses, host = " + hostName + ":");
            IPHostEntry ipEntry = Dns.GetHostEntry(hostName);
            IPAddress[] addr = ipEntry.AddressList;

            for (int i = 0; i < addr.Length; i++)
            {
                Debug.WriteLine("  IPaddr[{0}] = {1} ", i, addr[i].ToString());
            }
        }

        // WM  *** DEBUG ONLY ***
        // Show all network interfaces present with many of their attributes.
        //
        private void ShowNetworkInterfaces()
        {
            Debug.WriteLine("WM, WiFiAdvertiser::ShowNetworkInterfaces:");
            int i = -1;

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                Debug.WriteLine("  ----------");
                i++;  // doing it here lets an increment still follow a 'continue'
                Debug.WriteLine("  nic[" + i + "].Name = " + nic.Name);
                Debug.WriteLine("  nic[" + i + "].Id   = " + nic.Id);
                Debug.WriteLine("  nic[" + i + "].Description = " + nic.Description);
                Debug.WriteLine("  nic[" + i + "].NetworkInterfaceType = " + nic.NetworkInterfaceType);
                Debug.WriteLine("  nic[" + i + "].OperationalStatus = " + nic.OperationalStatus);
                Debug.WriteLine("  nic[" + i + "].Speed = " + nic.Speed);

                if (!nic.Supports(NetworkInterfaceComponent.IPv6))
                {
                    Debug.WriteLine("    does not support IPv6");
                }
                if (!nic.Supports(NetworkInterfaceComponent.IPv4))
                {
                    Debug.WriteLine("    does not support IPv4");
                    continue;
                }

                IPInterfaceProperties ipProps = nic.GetIPProperties();
                if (ipProps == null)
                {
                    Debug.WriteLine("    IP information not available");
                    continue;
                }
                IPv4InterfaceProperties ipPropsV4 = ipProps.GetIPv4Properties();
                if (ipPropsV4 == null)
                {
                    Debug.WriteLine("    IPv4 information not available");
                    continue;
                }

                // Show the index of which IP address, if any, for this interface is IPv4.
                Debug.WriteLine("  nic[" + i + "], ipPropsV4.Index = " + ipPropsV4.Index);

                // Show all IP addresses held by this network interface. Also show all
                // IPv4 netmasks. Note that IPv4 netmasks contain all zeroes for IPv6
                // addresses (which we don't use).
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                {
                    Debug.WriteLine("  nic[" + i + "], ipProps.addr.Address  = " + addr.Address.ToString());
                    Debug.WriteLine("  nic[" + i + "], ipProps.addr.IPv4Mask = " + addr.IPv4Mask);
                }
            }
        }

        // Gather info on all network interfaces of this machine.
        // We need this data to determine which interface is the one currently supporting
        // connectivity with the outside world -- we will use the same one for advertising.
        // For each interface we need its:
        //   - IP address: will be part of the book advert
        //   - netmask: will drive calculation of the UDP broadcast address to send to
        //   - type: might prove useful, not sure yet
        //   - current status: might prove useful, not sure yet
        //
        private void GetNetworkInterfaceInfo(List<string> ifTypes, List<string> ifStatuses,
                                             List<string> ifIpAddresses, List<string> ifNetmasks)
        {
            Debug.WriteLine("WM, WiFiAdvertiser::GetNetworkInterfaceInfo, begin");

            int i = 0;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!nic.Supports(NetworkInterfaceComponent.IPv4))
                {
                    Debug.WriteLine("    i = " + i + ", does not support IPv4");
                    continue;
                }

                ifTypes.Add(nic.NetworkInterfaceType.ToString());  // TODO, REMOVE here and in comment
                ifStatuses.Add(nic.OperationalStatus.ToString());  // TODO, REMOVE here and in comment

                IPInterfaceProperties ipProps = nic.GetIPProperties();
                if (ipProps == null)
                {
                    Debug.WriteLine("    i = " + i + ", IP information not available");
                    continue;
                }
                IPv4InterfaceProperties ipPropsV4 = ipProps.GetIPv4Properties();
                if (ipPropsV4 == null)
                {
                    Debug.WriteLine("    i = " + i + ", IPv4 information not available");
                    continue;
                }

                // This network interface might hold IPv4 addresses, IPv6 addresses, or both.
                // My laptop's Ethernet interface (machine has 11 interfaces altogether) has
                // four IPv6 addresses and one IPv4 address.
                int j = 0;
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                {
                    ifIpAddresses.Add(addr.Address.ToString());
                    ifNetmasks.Add(addr.IPv4Mask.ToString());
                    j++;
                }
                i++;
            }

            // WM, DEBUG ONLY, BEGIN - dump the lists
            for (int k = 0; k < ifTypes.Count; k++)
            {
                Debug.WriteLine("  ifTypes[" + k + "] = " + ifTypes[k]);
            }
            for (int k = 0; k < ifStatuses.Count; k++)
            {
                Debug.WriteLine("  ifStatuses[" + k + "] = " + ifStatuses[k]);
            }
            for (int k = 0; k < ifIpAddresses.Count; k++)
            {
                Debug.WriteLine("  ifIpAddresses[" + k + "] = " + ifIpAddresses[k]);
            }
            for (int k = 0; k < ifNetmasks.Count; k++)
            {
                Debug.WriteLine("  ifNetmasks[" + k + "] = " + ifNetmasks[k]);
            }
            // WM, DEBUG ONLY, END
        }

        // Gather info on all network interfaces. Then, find which interface the
        // local IP address came from and grab the netmask from that same interface.
        //   1. Get the network interface info, write it into the class-scoped lists.
        //   2. Find the local IP's index in the IP address list. If we can't find
        //      it, return an empty string.
        //   3. In the netmask list, get the netmask at the same index depth as the
        //      local IP, and return it.
        //
        private string GetNetmaskOfNetworkIface()
        {
            // Step 1
            GetNetworkInterfaceInfo(IfTypes, IfStatuses, IfIpAddresses, IfNetmasks);

            // Step 2
            int ipInUseIdx = -1;
            for (int i = 0; i < IfIpAddresses.Count; i++)
            {
                if (_localIp.Equals(IfIpAddresses[i].ToString()))
                {
                    Debug.WriteLine("WM, WiFiAdvertiser::GetNetmaskOfNetworkIface, ip found: IfIpAddresses[" + i + "]");
                    ipInUseIdx = i;
                    break;
                }
            }
            if (ipInUseIdx == -1)
            {
                Debug.WriteLine("WM, WiFiAdvertiser::GetNetmaskOfNetworkIface, oh no! ip not found, bail and regroup");
                return "";
            }

            // Step 3
            return IfNetmasks[ipInUseIdx];
        }

        public static void SendCallback(IAsyncResult args) { }

        /// <summary>
        /// Since this is typically not a real "server", its ipaddress could be assigned dynamically,
        /// and could change each time someone wakes it up.
        /// </summary>
        // WM: true, the IP address is assigned dynamically but that happens at power-up. It's not
        // going to change between a pair of the UDP broadcast adverts that go out once per second.
        //
        private void UpdateAdvertisementBasedOnCurrentIpAddress()
        {
            _currentIpAddress = GetIpAddressOfNetworkIface();

            if (_cachedIpAddress != _currentIpAddress)
            {
                _cachedIpAddress = _currentIpAddress;
                dynamic advertisement = new DynamicJson();
                advertisement.title = BookTitle;
                advertisement.version = BookVersion;
                advertisement.language = TitleLanguage;
                advertisement.protocolVersion = WiFiPublisher.ProtocolVersion;
                advertisement.sender = System.Environment.MachineName;
                advertisement.senderIP = _currentIpAddress; // will be needed when advert is also displayed as QR code

                _sendBytes = Encoding.UTF8.GetBytes(advertisement.ToString());
                //EventLog.WriteEntry("Application", "Serving at http://" + _currentIpAddress + ":" + ChorusHubOptions.MercurialPort, EventLogEntryType.Information);
            }
        }

        /// <summary>
        /// The intent here is to get an IP address by which this computer can be found on the local subnet.
        /// This is ambiguous if the computer has more than one IP address (typically for an Ethernet and WiFi adapter).
        /// Early experiments indicate that things work whichever one is used, assuming the networks are connected.
        /// Eventually we may want to prefer WiFi if available (see code in HearThis), or even broadcast on all of them.
        /// </summary>
        /// <returns></returns>
        private string GetLocalIpAddress()
        {
            string localIp = null;
            var host = Dns.GetHostEntry(Dns.GetHostName());

            foreach (
                var ipAddress in host.AddressList.Where(
                    ipAddress => ipAddress.AddressFamily == AddressFamily.InterNetwork
                )
            )
            {
                if (localIp != null)
                {
                    if (host.AddressList.Length > 1)
                    {
                        //EventLog.WriteEntry("Application", "Warning: this machine has more than one IP address", EventLogEntryType.Warning);
                    }
                }
                localIp = ipAddress.ToString();
            }
            return localIp ?? "Could not determine IP Address!";
        }

        // We need the IP address of *this* (the local) machine. Since a machine running BloomDesktop
        // can have multiple IP addresses (mine has 11, a mix of both IPv4 and IPv6), we must carefully
        // select the one that will actually be used by a network interface. Unfortunately, returning
        // the first non-blank address found of type 'AddressFamily.InterNetwork' is sometimes NOT correct.
        //
        // This alternative function is based on the post at
        // https://stackoverflow.com/questions/6803073/get-local-ip-address/27376368#27376368
        //
        private string GetIpAddressOfNetworkIface()
        {
            IPEndPoint endpoint;
            Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            sock.Connect("8.8.8.8", 65530); // Google's public DNS service
            endpoint = sock.LocalEndPoint as IPEndPoint;

            return endpoint.Address.ToString();
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
