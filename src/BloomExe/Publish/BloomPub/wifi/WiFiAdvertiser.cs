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

        private Thread _thread;
        private IPEndPoint _localEP;
        private IPEndPoint _remoteEP;
        private Socket _sock;
        private string _localIp = "";
        private string _remoteIp = "";  // UDP broadcast address, will *not* be 255.255.255.255
        private string _subnetMask = "";
        private string _currentIpAddress = "";
        private string _cachedIpAddress = "";

        // Lists to hold useful information pulled from all network interfaces.
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

            // Don't let C# pick the network interface -- it sometimes chooses the wrong one.
            // Force it to use the interface corresponding to the known good local IP address.
            // Do this by using a Socket instead of a UdpClient.
            //
            //   NOTE: only a multi-homed machine will have more than one network interface
            //         supporting internet connectivity. This will not be common -- the typical
            //         BloomDesktop user is on Windows and has just one active network interface,
            //         likely Wi-Fi but could also be Ethernet.
            // ASSUME: the machine is single-homed.

            // For a raw socket we need to know what "local IP" address to give it. Find out
            // what IP address *currently* supports internet traffic, and use that address.
            _localIp = GetIpAddressOfNetworkIface();
            if (_localIp.Length == 0)
            {
                Debug.WriteLine("WiFiAdvertiser, ERROR: can't get local IP address, exiting");
                return;
            }

            // The local IP address is associated with one of the network interfaces. From that
            // same interface get the associated subnet mask (this will be needed to generate the
            // appropriate broadcast address for UDP advertising).
            _subnetMask = GetNetmaskOfNetworkIface();
            if (_subnetMask.Length == 0)
            {
                Debug.WriteLine("WiFiAdvertiser, ERROR: can't get subnet mask, exiting");
                return;
            }

            // Using 255.255.255.255 as UDP broadcast address doesn't work for a couple reasons:
            //   - A raw socket, which now replaces UdpClient, raises an exception:
            //     "System.Net.Sockets.SocketException (0x80004005): An attempt was made
            //     to access a socket in a way forbidden by its access permissions"
            //   - The broadcast address must be computed from sender's local IP address and
            //     subnet mask
            _remoteIp = BuildBroadcastAddress(_localIp, _subnetMask);
            if (_remoteIp.Length == 0)
            {
                Debug.WriteLine("WiFiAdvertiser, ERROR: can't make broadcast address, exiting");
                return;
            }

            Debug.WriteLine("UDP advertising will have: source addr    = " + _localIp);
            Debug.WriteLine("                           broadcast addr = " + _remoteIp);
            Debug.WriteLine("                           subnet mask    = " + _subnetMask);

            try
            {
                _localEP = new IPEndPoint(IPAddress.Parse(_localIp), Port);

                _remoteEP = new IPEndPoint(IPAddress.Parse(_remoteIp), Port);

                _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                _sock.Bind(_localEP);

                // Socket is ready. Begin advertising once per second, indefinitely.
                while (true)
                {
                    if (!Paused)
                    {
                        UpdateAdvertisementBasedOnCurrentIpAddress();

                        // No need to transmit on a separate thread; this thread spends most of its time
                        // sleeping. This is a simple socket and we are sending only a few hundred bytes.
                        _sock.SendTo(_sendBytes, 0, _sendBytes.Length, SocketFlags.None, _remoteEP);
                    }
                    Thread.Sleep(1000);
                }
            }
            catch (SocketException e)
            {
                Debug.WriteLine("WiFiAdvertiser::Work, SocketException: " + e);
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

        // Function: for a given local IP address and subnet mask, construct the
        //           appropriate broadcast address.
        //
        // Philosophy: the subnet mask indicates how to handle the IP address bits --
        //   - '1' mask bits indicate the "network address" portion of the IP address.
        //     The broadcast address aims at the same network so just copy the IP address
        //     bits into the same positions of the broadcast address. 'XORing' these '1'
        //     mask bits with '1' flips them to '0'. Then 'ORing' those now-'0' bits with
        //     the IP bits keeps the IP bits unchanged.
        //   - '0' mask bits indicate the "host ID" portion of the IP address. We must fill
        //     this portion of the broadcast address with '1's so all hosts on the network
        //     will see the transmission. The same bit operations work here too: 'XORing'
        //     the '0' mask bits with '1' flips them to '1', which when 'ORed' with anything
        //     gives '1', which is what we want for the host bits.
        //
        // Algorithm:
        //   1. Convert IP address and corresponding subnet mask to byte arrays
        //   2. Create a byte array to hold broadcast address result
        //   3. For each IP address octet and corresponding mask octet, starting with most significant
        //         Compute broadcast address octet per "Philosophy" above
        //      EndFor
        //   4. Convert broadcast address byte array to IP address string and return it
        //
        private string BuildBroadcastAddress(string ipIn, string maskIn)
        {
            try
            {
                // Step 1
                IPAddress ipAddress = IPAddress.Parse(ipIn);
                IPAddress subnetMask = IPAddress.Parse(maskIn);
                byte[] ipBytes = ipAddress.GetAddressBytes();
                byte[] subnetBytes = subnetMask.GetAddressBytes();

                if ((ipBytes.Length != 4) || (subnetBytes.Length != 4))
                {
                    Debug.WriteLine("Error, broadcast address: address and/or subnet mask length is not 4.");
                    return "";
                }

                // Step 2
                byte[] bcastBytes = new byte[ipBytes.Length];

                // Step 3
                for (int i = 0; i < ipBytes.Length; i++)
                {
                    bcastBytes[i] = (byte)(ipBytes[i] | (subnetBytes[i] ^ 255));
                }

                // Step 4
                return new IPAddress(bcastBytes).ToString();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error, broadcast address: exception ", e);
                return "";
            }
        }

        // Helper function to gather info on all network interfaces of this machine.
        //
        // nic.NetworkInterfaceType and nic.OperationalStatus are also interesting (and
        // the debug version of this file retrieves and prints them), but they are not
        // needed -- GetIpAddressOfNetworkIface() tells us which interface to use.
        //
        private void GetNetworkInterfaceInfo(List<string> ifIpAddresses, List<string> ifNetmasks)
        {
            int i = 0;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!nic.Supports(NetworkInterfaceComponent.IPv4))
                {
                    Debug.WriteLine("NOTE: nic[" + i + "], IPv4 not supported");
                    continue;
                }

                IPInterfaceProperties ipProps = nic.GetIPProperties();
                if (ipProps == null)
                {
                    Debug.WriteLine("NOTE: nic[" + i + "], IP information not available");
                    continue;
                }
                IPv4InterfaceProperties ipPropsV4 = ipProps.GetIPv4Properties();
                if (ipPropsV4 == null)
                {
                    Debug.WriteLine("NOTE: nic[" + i + "], IPv4 information not available");
                    continue;
                }

                // This network interface might hold IPv4 addresses, IPv6 addresses, or both.
                // My laptop's Ethernet interface (machine has 11 interfaces altogether) has
                // four IPv6 addresses and one IPv4 address.
                // Grab and store each IP address and the associated netmask.
                int j = 0;
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                {
                    ifIpAddresses.Add(addr.Address.ToString());
                    ifNetmasks.Add(addr.IPv4Mask.ToString());
                    j++;
                }
                i++;
            }
        }

        // Find and return the subnet mask in effect for the network interface that
        // will be used for UDP advertising.
        //
        // Gather info on all network interfaces. We already have the working IP address
        // from GetIpAddressOfNetworkIface(). Find which interface that working address
        // comes from and grab the netmask from that same interface.
        //   1. Get the network interface info, write it into the class-scoped lists.
        //   2. Find the local IP's index in the IP address list. If we can't find
        //      it, return an empty string. Something went really wrong.
        //   3. In the netmask list, get the netmask at the same index depth as the
        //      local IP, and return it.
        //
        private string GetNetmaskOfNetworkIface()
        {
            // Step 1
            GetNetworkInterfaceInfo(IfIpAddresses, IfNetmasks);

            // Step 2
            int ipInUseIdx = -1;
            for (int i = 0; i < IfIpAddresses.Count; i++)
            {
                if (_localIp.Equals(IfIpAddresses[i].ToString()))
                {
                    ipInUseIdx = i;
                    break;
                }
            }
            if (ipInUseIdx == -1)
            {
                Debug.WriteLine("WiFiAdvertiser, oh no! ip not found, bail and regroup");
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

        // We need *the* IP address of *this* (the local) machine. Since a machine running BloomDesktop
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
