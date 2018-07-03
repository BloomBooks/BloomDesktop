using System;
using System.Collections.Generic;
using Bloom.web;

namespace BloomTests.web
{
	/// <summary>
	/// WebSocketServerSpy provides a test-visible version of the messages sent to the Bloom WebSocket
	/// (e.g. by a WebSocketProgress object).
	/// See Publish.UsbPublisherTests for sample usage.
	/// </summary>
	internal class WebSocketServerSpy: IBloomWebSocketServer
	{
		private bool _isInitialized;
		private List<KeyValuePair<string, Tuple<string, string>>> _events;

		public WebSocketServerSpy()
		{
			_isInitialized = false;
		}

		public void SendString(string clientContext, string eventId, string message)
		{
			if (!_isInitialized)
				throw new ApplicationException("WebSocketServerSpy: Send() attempted when not initialized!");

			_events.Add(new KeyValuePair<string, Tuple<string, string>>(eventId, 
				new Tuple<string, string>(message, clientContext)));
		}
		public void SendBundle(string clientContext, string eventId, dynamic messageBundle)
		{
			_events.Add(new KeyValuePair<string, Tuple<string, string>>(eventId,
				new Tuple<string, string>(messageBundle.message, clientContext)));
		}
		public void Init(string dummy)
		{
			_isInitialized = true;
			_events = new List<KeyValuePair<string, Tuple<string, string>>>();
		}

		public List<KeyValuePair<string, Tuple<string, string>>> Events
		{
			get
			{
				if (!_isInitialized)
				{
					throw new ApplicationException("WebSocketServerSpy: EventDictionary access attempted when not initialized!");
				}
				return _events;
			}
		}

		public void Reset()
		{
			_isInitialized = false;
			_events.Clear();
		}

		public void Dispose()
		{
			_isInitialized = false;
			_events = null;
		}
	}
}
