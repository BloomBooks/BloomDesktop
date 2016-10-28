using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using Fleck;
using SIL.Reporting;

namespace Bloom.Api
{
	/// <summary>
	/// Runs a websocket on the given port. Useful for high-frequency messages (like audio levels) and allows the backend to send messages to the client.
	/// 
	/// About ports... we could have a single server that is used to pass any number of messages. We could have a single connection on the client
	/// with a means to distribute messages around the client depending on, say, a message identifier. Or we could have multiple connections (sockets) from
	/// various parts in the client, each just filtering out the message stream to the ones they are interested in.
	/// 
	/// Alternatively, you could have multiple instances of this class, each with its own "port" parameter, and intended for use by a single, simple end point.
	/// That is the case as this is introduced in Bloom 3.6, for getting the peak level of the audio coming from a microphone.
	/// </summary>
	public class BloomWebSocketServer : IDisposable
	{
		//Note, in normal web apps, you'd have any number of clients opening sockets to this single server. It would be 1 to n. In Bloom, where there is
		//only a single client, we think more in terms of 1 to 1.  However there's nothing preventing multiple parts of the Bloom client from opening their
		//own connection (socket).
		private WebSocketServer _server;
		private List<IWebSocketConnection> _allSockets;

		public void Init(string port)
		{
			FleckLog.Level = LogLevel.Warn;
			_allSockets = new List<IWebSocketConnection>();
			var websocketaddr = "ws://127.0.0.1:" + port;
			Logger.WriteMinorEvent("Attempting to open a WebSocketServer on " + websocketaddr);
			_server = new WebSocketServer(websocketaddr);
			
			try
			{
				_server.Start(socket =>
				{
					socket.OnOpen = () =>
					{
						Debug.WriteLine("Backend received an request to open a BloomWebSocketServer socket");
						_allSockets.Add(socket);
					};
					socket.OnClose = () =>
					{
						Debug.WriteLine("Backend received an request to close  BloomWebSocketServer socket");
						_allSockets.Remove(socket);
					};
				});
			}
			catch (SocketException ex)
			{
				Logger.WriteEvent("Opening a WebSocketServer on " + websocketaddr + " failed.  Error = " + ex);
				ErrorReport.NotifyUserOfProblem(ex, "Bloom cannot start properly (cannot set up some internal communications){0}{0}" +
					"What caused this?{0}" +
					"Possibly another version of Bloom is running, perhaps not very obviously.{0}{0}" +
					"What can you do?{0}" +
					"Click OK, then exit Bloom and restart your computer.{0}" +
					"If the problem keeps happening, click 'Details' and report the problem to the developers.", Environment.NewLine);
			}
		}

		public void Send(string eventId, string eventData)
		{
			dynamic e = new DynamicJson();
			e.id = eventId;
			e.payload = eventData;

			//note, if there is no open socket, this isn't going to do anything, and
			//that's (currently) fine.
			lock(this)
			{
				foreach (var socket in _allSockets)
				{
					socket.Send(e.ToString());
				}
			}
		}

		public void Dispose()
		{
			lock(this)
			{
				if(_server != null)
				{
					foreach(var socket in _allSockets)
					{
						socket.Close();
					}
					_allSockets.Clear();
					_server.Dispose();
					_server = null;
				}
			}
		}
	}
}
