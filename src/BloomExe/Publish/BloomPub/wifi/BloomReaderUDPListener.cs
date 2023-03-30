using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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
			try
			{
				_listener = new UdpClient(_portToListen);
			}
			catch (SocketException e)
			{
				//log then do nothing
				Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
			}

			if (_listener != null)
			{
				IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 0);

				while (_listening)
				{
					try
					{
						byte[] bytes = _listener.Receive(ref groupEP); // waits for packet from Android.

						//raise event
						NewMessageReceived?.Invoke(this, new AndroidMessageArgs(bytes));
					}
					catch(SocketException se)
					{
						if(!_listening || se.SocketErrorCode == SocketError.Interrupted)
							return; // no problem, we're just closing up shop
						throw se;
					}
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
				return;

			// Since we told the listener to close already this shouldn't have to do much (nor be dangerous)
			_listeningThread.Abort();
			_listeningThread.Join(2 * 1000);
			_listeningThread = null;
		}
	}
}
