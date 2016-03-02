using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using Fleck;
using SIL.Reporting;

namespace Bloom.web
{
	class BloomWebSocketServer : IDisposable
	{
		private WebSocketServer _server;
		private List<IWebSocketConnection> _allSockets;

		public BloomWebSocketServer()
		{
			FleckLog.Level = LogLevel.Warn;
			_allSockets = new List<IWebSocketConnection>();
			_server = new WebSocketServer("ws://127.0.0.1:8189");
			
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
				ErrorReport.NotifyUserOfProblem(ex, "Bloom cannot start properly (cannot set up some internal communications){0}{0}" +
					"What caused this?{0}" +
					"Possibly another version of Bloom is running, perhaps not very obviously.{0}{0}" +
					"What can you do?{0}" +
					"Click OK, then exit Bloom and restart your computer.{0}" +
					"If the problem keeps happening, click 'Details' and report the problem to the developers.", Environment.NewLine);
			}
		}

		public void Send(string message)
		{
			//note, if there is no open socket, this isn't going to do anything, and
			//that's (currently) fine.
			foreach (var socket in _allSockets)
			{
				socket.Send(message);
			}
		}

		public void Dispose()
		{
			if (_server != null)
			{
				_server.Dispose();
				_server = null;
			}
		}
	}
}
