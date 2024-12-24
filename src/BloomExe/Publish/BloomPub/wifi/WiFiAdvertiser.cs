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
        private string _remoteIp = "";  // UDP broadcast address
        private string _subnetMask = "";
        private string _currentIpAddress = "";
        private string _cachedIpAddress = "";

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

            _thread = new Thread(Work);
            _thread.Start();
            Debug.WriteLine("WM, WiFiAdvertiser::Start, work thread started, exiting"); // WM, temporary
        }

        public bool Paused { get; set; }

        private void Work()
        {
            _progress.Message(
                idSuffix: "beginAdvertising",
                message: "Advertising book to Bloom Readers on local network..."
            );

            // WM, DEBUG ONLY: show this machine's IP addresses and network interfaces.
            showIpAddresses();
            showNetworkInterfaces();

            // WM, DEBUG ONLY: show this machine's IP address that is currently usable for traffic.
            //                 Compare what is returned by the existing query function, GetLocalIpAddress(),
            //                 with the new one, GetIpAddressOfNetworkIface().
            Debug.WriteLine("WM, WiFiAdvertiser::Work, GetLocalIpAddress() returns " + GetLocalIpAddress());
            Debug.WriteLine("WM, WiFiAdvertiser::Work, GetIpAddressOfNetworkIface() returns " + GetIpAddressOfNetworkIface());

            // Don't let C# pick the network interface -- it sometimes chooses the wrong one.
            // Force it to use the interface corresponding to the known good local IP address,
            // using a raw socket.
            // First choice is Wi-Fi, followed by Ethernet.
            //
            // The following call determines the local IP address to use and copies it into
            // _localIp. It also gets the subnet mask of the network interface that owns that
            // local IP address and copies it into _subnetMask.
            chooseIpAddressForAdvertising();

            // Until chooseIpAddressForAdvertising() is done, hardcode both.
            _localIp = "192.168.1.17";
            _subnetMask = "255.255.255.0";

            // _remoteIp = "255.255.255.255";
            // The typical "broadcast address" above doesn't work with a raw socket:
            //      "System.Net.Sockets.SocketException (0x80004005): An attempt was made
            //      to access a socket in a way forbidden by its access permissions"
            // Rather, the broadcast address must be calculated per the current subnet mask:
            _remoteIp = buildBroadcastAddress(_localIp, _subnetMask);

            if (_localIp.Length > 0)
            {
                try
                {
                    Debug.WriteLine("_localIp = " + _localIp);
                    Debug.WriteLine("_remoteIp = " + _remoteIp);

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
                            //_client.BeginSend(
                            //    _sendBytes,
                            //    _sendBytes.Length,
                            //    LocalEP,
                            //    SendCallback,
                            //    _client
                            //);
                            _sock.SendTo(_sendBytes, 0, _sendBytes.Length, SocketFlags.None, _remoteEP);
                        }
                        Debug.WriteLine("WM, WiFiAdvertiser::Work, sent UDP broadcast advert"); // WM
                        Thread.Sleep(1000);
                    }
                }
                catch (SocketException e)
                {
                    Debug.WriteLine("WM, WiFiAdvertiser::Work, SocketException: " + e);
                }
            }
            else
            {
                Debug.WriteLine("WM, WiFiAdvertiser::Work, didn't get a local IP addr");
            }
        }

        // Function: for a given local IP address and subnet mask, construct the
        //           broadcast address that stays within the subnet.
        //           All octet values in the subnet mask are handled.
        // Algorithm:
        //    1. In the subnet mask find the 1-0 bit boundary
        //    2. For all '255' octets (they *will* be to the left of the boundary),
        //       copy the corresponding local IP octets into the broadcast address
        //    3. For all '0' octets (they *will* be to the right of the boundary),
        //       put '255' into the corresponding broadcast address octets
        //    4. For 0 < octet < 255, value of octet starts at 255 and is decremented
        //       for each 1-bit seen according to the following:
        //          scan the octet left to right (most-to-least significant)
        //          if MSb is '1' subtract 2^7; if next is '1' subtract 2^6; etc
        // Assumptions:
        //    - The local IP and mask inputs are valid and do not need to be checked

        private string buildBroadcastAddress(string localIp, string mask)
        {
            Debug.WriteLine("WM, WiFiAdvertiser::Work, buildBroadcastAddress per localIp = {0}, mask = {1}", localIp, mask);

            // Isolate the octets for both local IP and subnet mask.
            string[] octetsIp = localIp.Split('.');
            string[] octetsMask = mask.Split('.');

            // Find indexes (left-to-right) where mask octets begin.
            //   - most significant octet begins at index 0 (duh)
            //   - the other 3 begin after a '.' (dot) which we find here
            List<int> indexes = new List<int>();
            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] == '.')
                {
                    indexes.Add(i);
                }
            }

            for (int j = 0; j < 4; j++)  // tmp debug only
            {
                Debug.WriteLine("  octetsIp[{0}] = {1}", j, octetsIp[j]);
            }
            for (int j = 0; j < 4; j++)  // tmp debug only
            {
                Debug.WriteLine("  octetsMask[{0}] = {1}", j, octetsMask[j]);
            }
            for (int j = 0; j < 3; j++)  // tmp debug only
            {
                Debug.WriteLine("  mask: indexes[{0}] = {1}", j, indexes[j]);
            }

            // Step 1: find the 1-0 bit boundary in the mask.
            int indexFirstZero = mask.IndexOf('0');
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
                    Debug.WriteLine("  255: bcastAddress = "+ bcastAddress);
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
            return bcastAddress;
        }

        // WM  *** DEBUG ONLY ***
        // Show all IP addresses of a host computer.
        private void showIpAddresses()
        {
            String hostName = Dns.GetHostName();
            Debug.WriteLine("WM, WiFiAdvertiser::showIpAddresses, host = " + hostName + ":");
            IPHostEntry ipEntry = Dns.GetHostEntry(hostName);
            IPAddress[] addr = ipEntry.AddressList;

            for (int i = 0; i < addr.Length; i++) {
                Debug.WriteLine("  IPaddr[{0}] = {1} ", i, addr[i].ToString());
            }
        }

        // WM  *** DEBUG ONLY ***
        // Show all network interfaces present with many of their attributes.
        private void showNetworkInterfaces()
        {
            Debug.WriteLine("WM, WiFiAdvertiser::showNetworkInterfaces:");
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
                    Debug.WriteLine("    does not support IPv6"); // on my machine only Wi-Fi has this
                }
                if (!nic.Supports(NetworkInterfaceComponent.IPv4))
                {
                    Debug.WriteLine("    does not support IPv4");
                    continue;
                }

                IPInterfaceProperties ipProps = nic.GetIPProperties();
                IPv4InterfaceProperties ipPropsV4 = ipProps.GetIPv4Properties();
                if (ipPropsV4 == null)
                {
                    Debug.WriteLine("    IPv4 information not available");
                    continue;
                }

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

        private void chooseIpAddressForAdvertising()
        {
            Debug.WriteLine("WM, WiFiAdvertiser::chooseIpAddressForAdvertising, do nothing yet");
            // TODO: Is it possible that none of this interface stuff is needed? Can I just take what
            // my improved IP-address-in-use function returns to force the socket connection?
            // If the socket DOES require an interface ID or something then it should be simple to just
            // search the interface for the IP address returned by my IP-address-in-use function.
        }

        public static void SendCallback(IAsyncResult args) { }

        /// <summary>
        /// Since this is typically not a real "server", its ipaddress could be assigned dynamically,
        /// and could change each time someone wakes it up.
        /// </summary>
        private void UpdateAdvertisementBasedOnCurrentIpAddress()
        {
            // WM *** Yes, the IP address is assigned dynamically but that happens at power-up. It's not
            // going to change between a pair of the UDP broadcast adverts that go out once per second,
            // right?

            _currentIpAddress = GetIpAddressOfNetworkIface();
            //_currentIpAddress = LocalIp;
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

        // We need the IP address of *this* (the local) machine. Since a machine running BloomDesktop can have
        // multiple IP addresses (mine has 11, a mix of both IPv4 and IPv6), we must be judicious in selecting
        // the one that will actually be used by a network interface. Unfortunately, returning the first non-blank
        // address found of type 'AddressFamily.InterNetwork' is sometimes NOT correct.
        //
        // This alternative function is based on the post at
        // https://stackoverflow.com/questions/6803073/get-local-ip-address/27376368#27376368

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
