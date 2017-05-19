using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Forms;
using Fleck;
using SIL.Reporting;

namespace Bloom.Api
{
	/// <summary>
	///
	///
	/// this is from spike, which worked, but we aren't using (yet).  When searching
	/// for other parts of the spike, do a text search, as they may be commented out
	///
	///
	/// Commands have a status that needs to be communicated to the html client.
	/// This class does that by wiring up to events on commands and sending
	/// updated status on commands over a websocket.
	/// </summary>
	class CommandAvailabilityPublisher : IDisposable
	{
		private readonly DuplicatePageCommand _duplicatePageCommand;

		//WebSocketServer comes from the fleck nuget library
		private WebSocketServer _server;

		private List<IWebSocketConnection> _allSockets;

		public CommandAvailabilityPublisher(IEnumerable<ICommand> commands )
		{
			foreach (var command in commands)
			{
				command.EnabledChanged += command_EnabledChanged;
			}

			FleckLog.Level = LogLevel.Warn;
			_allSockets = new List<IWebSocketConnection>();
			_server = new WebSocketServer("ws://127.0.0.1:8189");
			try
			{
				_server.Start(socket =>
				{
					socket.OnOpen = () =>
					{
						Debug.WriteLine("Backend received an request to open a CommandAvailabilityPublisher socket");
						_allSockets.Add(socket);
					};
					socket.OnClose = () =>
					{
						Debug.WriteLine("Backend received an request to close  CommandAvailabilityPublisher socket");
						_allSockets.Remove(socket);
					};
				});
			}
			catch (SocketException ex)
			{
				BloomWebSocketServer.ReportSocketExceptionAndExit(ex, _server);
			}
		}

		void command_EnabledChanged(object sender, EventArgs e)
		{
			//TODO: What's here is just a proof of concept. We may want to send this in a different format
			//once we start using it.
			var cmd = (Command) sender;
			var message = string.Format("{{\"{0}\": {{\"enabled\": \"{1}\"}}}}", cmd.Name, cmd.Enabled.ToString());
			foreach(var socket in _allSockets)
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
