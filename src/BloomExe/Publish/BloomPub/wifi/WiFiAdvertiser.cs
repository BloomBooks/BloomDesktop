using System;
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
        private IPEndPoint _endPoint;

        // The port on which we advertise.
        // ChorusHub uses 5911 to advertise. Bloom looks for a port for its server at 8089 and 10 following ports.
        // https://en.wikipedia.org/wiki/List_of_TCP_and_UDP_port_numbers shows a lot of ports in use around 8089,
        // but nothing between 5900 and 5931. Decided to use a number similar to ChorusHub.
        private const int Port = 5913; // must match port in BloomReader NewBookListenerService.startListenForUDPBroadcast
        private string _currentIpAddress;
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
            _client = new UdpClient { EnableBroadcast = true };
            _endPoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), Port);
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

            // WM, DEBUG ONLY: show this machine's IP addresses and network interfaces.
            showIpAddresses();
            showNetworkInterfaces();

            // Choose the IP address most likely to work.
            chooseIpAddressForAdvertising();

            Debug.WriteLine("WM, WiFiAdvertiser::Work, GetLocalIpAddress() returns " + GetLocalIpAddress());
            Debug.WriteLine("WM, WiFiAdvertiser::Work, GetIpAddressOfNetworkIface() returns " + GetIpAddressOfNetworkIface());

            //Debug.WriteLine("WM, WiFiAdvertiser::Work, begin UDP broadcast advert loop, src IP = ________"); // WM
            try
            {
                while (true)
                {
                    if (!Paused)
                    {
                        UpdateAdvertisementBasedOnCurrentIpAddress();

                        // *** TODO: Don't let C# pick the network interface to use as it sometimes chooses
                        //           the wrong one.
                        _client.BeginSend(
                            _sendBytes,
                            _sendBytes.Length,
                            _endPoint,
                            SendCallback,
                            _client
                        );
                    }
                    Debug.WriteLine("WM, WiFiAdvertiser::Work, sent UDP broadcast advert"); // WM
                    Thread.Sleep(1000);
                }
            }
            catch (ThreadAbortException)
            {
                _progress.Message(idSuffix: "Stopped", message: "Stopped Advertising.");
                _client.Close();
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

        // WM  *** DEBUG ONLY ***
        // Show all IP addresses present in the system.
        private void showIpAddresses()
        {
            String hostName = Dns.GetHostName();
            Debug.WriteLine("WM, WiFiAdvertiser::showIpAddresses for host = " + hostName);
            IPHostEntry ipEntry = Dns.GetHostEntry(hostName);
            IPAddress[] addr = ipEntry.AddressList;
            for (int i = 0; i<addr.Length; i++) {
                Debug.WriteLine("  IPaddr[{0}] = {1} ", i, addr[i].ToString());
            }
        }

        // WM  *** DEBUG ONLY ***
        // Show all network interfaces present plus some relevant attributes.
        private void showNetworkInterfaces()
        {
            Debug.WriteLine("WM, WiFiAdvertiser::showNetworkInterfaces");
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
                Debug.WriteLine("  nic[" + i + "], MAC = " + nic.GetPhysicalAddress());

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

                Debug.WriteLine("  nic[" + i + "], ipPropsV4.Index = " + ipPropsV4.Index);
                Debug.WriteLine("  nic[" + i + "], ipPropsV4       = " + ipPropsV4.ToString());
                Debug.WriteLine("  nic[" + i + "], ipProps.UnicastAddresses.FirstOrDefault = " + ipProps.UnicastAddresses.FirstOrDefault()?.Address);
                //Debug.WriteLine("  nic[" + i + "], ipProps.UnicastAddresses = " + ipProps.UnicastAddresses.ToString());
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                {
                    Debug.WriteLine("  nic[" + i + "], ipProps.addr.Address = " + addr.Address.ToString());
                }
            }
            Debug.WriteLine("  ----------");
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
            if (_currentIpAddress != GetLocalIpAddress())
            {
                _currentIpAddress = GetLocalIpAddress();
                dynamic advertisement = new DynamicJson();
                advertisement.title = BookTitle;
                advertisement.version = BookVersion;
                advertisement.language = TitleLanguage;
                advertisement.protocolVersion = WiFiPublisher.ProtocolVersion;
                advertisement.sender = System.Environment.MachineName;

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
        // multiple IP addresses (mine has 11, a mix of both IPv4 and IPv6), we must be judicious in selecting the
        // one that will actually be used by network interface. Unfortunately, GetLocalIpAddress() does not always
        // return the correct address.
        // Here is a function that does. It is based on the post at
        // https://stackoverflow.com/questions/6803073/get-local-ip-address/27376368#27376368
        private string GetIpAddressOfNetworkIface()
        {
            IPEndPoint endpoint;
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530); // Google's public DNS service
            endpoint = socket.LocalEndPoint as IPEndPoint;

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
