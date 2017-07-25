using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using ThirdParty.Json.LitJson;

namespace Bloom.Publish
{
	/// <summary>
	/// This class broadcasts a message over the network offering to download a book to any Android that wants it.
	/// </summary>
	public class WiFiAdvertiser : IDisposable
	{
		private UdpClient _client;
		private Thread _thread;
		private IPEndPoint _endPoint;
		// The port on which we advertise.
		// ChorusHub uses 5911 to advertise. Bloom looks for a port for its server at 8089 and 10 following ports.
		// https://en.wikipedia.org/wiki/List_of_TCP_and_UDP_port_numbers shows a lot of ports in use around 8089,
		// but nothing between 5900 and 5931. Decided to use a number similar to ChorusHub.
		private const int Port = 5913; // must match port in BloomPlayer NewBookListenerService.startListenForUDPBroadcast
		private string _currentIpAddress;
		private byte[] _sendBytes; // Data we send in each advertisement packet

		public void Start()
		{
			// The doc seems to indicate that EnableBroadcast is required for doing broadcasts.
			// In practice it seems to be required on Mono but not on Windows.
			// This may be fixed in a later version of one platform or the other, but please
			// test both if tempted to remove it.
			_client = new UdpClient
			{
				EnableBroadcast = true
			};
			_endPoint = new IPEndPoint(IPAddress.Parse("255.255.255.255"), Port);
			_thread = new Thread(Work);
			_thread.Start();
		}

		public string BookTitle;
		public string BookVersion;

		private void Work()
		{
			try
			{
				while (true)
				{
					UpdateAdvertisementBasedOnCurrentIpAddress();
					_client.BeginSend(_sendBytes, _sendBytes.Length, _endPoint, SendCallback, _client);
					Thread.Sleep(1000);
				}
			}
			catch (ThreadAbortException)
			{
				//Progress.WriteVerbose("Advertiser Thread Aborting (that's normal)");
				_client.Close();
			}
			catch (Exception error)
			{
				//EventLog.WriteEntry("Application", string.Format("Error in Advertiser: {0}", error.Message), EventLogEntryType.Error);
			}
		}
		public static void SendCallback(IAsyncResult args)
		{
		}

		/// <summary>
		/// Since this migt not be a real "server", its ipaddress could be assigned dynamically,
		/// and could change each time someone "wakes up the server laptop" each morning
		/// </summary>
		private void UpdateAdvertisementBasedOnCurrentIpAddress()
		{
			if (_currentIpAddress != GetLocalIpAddress())
			{
				_currentIpAddress = GetLocalIpAddress();
				dynamic dataObj = new DynamicJson();
				dataObj.Title = BookTitle;
				dataObj.Version = BookVersion;

				_sendBytes = Encoding.UTF8.GetBytes(dataObj.ToString());
				//EventLog.WriteEntry("Application", "Serving at http://" + _currentIpAddress + ":" + ChorusHubOptions.MercurialPort, EventLogEntryType.Information);
			}
		}

		private string GetLocalIpAddress()
		{
			string localIp = null;
			var host = Dns.GetHostEntry(Dns.GetHostName());

			foreach (var ipAddress in host.AddressList.Where(ipAddress => ipAddress.AddressFamily == AddressFamily.InterNetwork))
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
