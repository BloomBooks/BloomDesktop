using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Forms;
using Bloom;
using Bloom.Api;
using Bloom.web;
using Fleck;
using SIL.Reporting;

namespace Bloom.Api
{
	/// <summary>
	/// Runs a web socket server on the given port. Useful for high-frequency messages (like audio levels) and allows the backend to send messages to the client.
	///
	/// About ports... we currently only have a single server that is used to pass any number of messages. 
	/// Then on the client side, we have multiple connections to this same server; each connection is called a "socket",
	/// note that these are on the same port.
	/// For outgoing messages from c# to the client code, we have 3 levels:
	/// * clientContext: normally the screen, like "publish to android".
	/// * eventId: things like "progress message"
	/// * message: (optional) for events that need some text, it goes in here
	/// * cssStyleRule: (optional) for events that need some css styles. (JH isn't so happy about this one, seems rather special case)
	///
	/// The IBloomWebSocketServer interface allows tests to use a spy to see what messages have been sent.
	/// </summary>
	public class BloomWebSocketServer : IBloomWebSocketServer, IDisposable
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
						// our Typescript WebSocketManager sticks the name of the socket into this subProtocol parameter just for this debugging purpose
						Debug.WriteLine($"Opening websocket \"{socket.ConnectionInfo?.SubProtocol}\"");
						_allSockets.Add(socket);
					};
					socket.OnClose = () =>
					{
						Debug.WriteLine($"Closing websocket \"{socket.ConnectionInfo?.SubProtocol}\"");
						_allSockets.Remove(socket);
						socket.Close();
					};
				});
			}
			catch (SocketException ex)
			{
				Logger.WriteEvent("Opening a WebSocketServer on " + websocketaddr + " failed.  Error = " + ex);
				ReportSocketExceptionAndExit(ex, _server);
			}
		}

		private const string SocketExceptionMsg =
			"Bloom cannot start properly, because something prevented it from opening port {1},{0}" +
			"which different parts of Bloom use to talk to each other.{0}" +
			"Possibly another version of Bloom is running, perhaps not very obviously.{0}" +
			"Other things that can cause this are firewalls (including Covenant Eyes) and anti-malware programs.{0}{0}" +
			"What can you do?{0}" +
			"When you click OK, Bloom will exit. Then, restart your computer.{0}" +
			"If that doesn't fix the problem, try disabling any third-party firewalls and anti-malware programs (Microsoft's are OK).{0}" +
			"If the problem keeps happening, click 'Details' and report the problem to the developers.";

		/// <summary>
		/// Internal for re-use by CommandAvailabilityPublisher
		/// </summary>
		internal static void ReportSocketExceptionAndExit(SocketException exception, WebSocketServer server)
		{
			ErrorReport.NotifyUserOfProblem(exception, SocketExceptionMsg, Environment.NewLine, server.Port);
			Application.Exit();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="clientContext">This serves a couple of purposes. First, it is used to filter out messages
		/// to those aimed at a particular client context. For example, two screens, both containing progress boxes,
		/// could be on screen at the same time. The ClientContext would tell us which one is supposed to be 
		/// printing out messages coming with the "progress" eventId. </param>
		/// <param name="eventId"></param>
		/// <param name="eventBundle"></param>
		public void SendBundle(string clientContext, string eventId, dynamic eventBundle)
		{
			// We're going to take this and add routing info to it, so it's
			// no longer just the "details".
			var eventObject = eventBundle;
			eventObject.clientContext = clientContext;
			eventObject.id = eventId;

			//note, if there is no open socket, this isn't going to do anything, and
			//that's (currently) fine.
			lock (this)
			{
				// the ToArray() here gives us a copy so that if a socket
				// is removed while we're doing this, it will be ok
				foreach (var socket in _allSockets.ToArray())
				{
					// see if it's been removed
					if (_allSockets.Contains(socket))
					{
						// it could *still* be closed by the time we execute this,
						// I don't know if Sending on a closed socket would throw, so we'll catch it in any case
						try
						{
							socket?.Send(eventObject.ToString());
						}
						catch (Exception error)
						{
							NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, exception: error);
						}
					}
				}
			}
		}

		// we have 3 places in javascript land that are not yet using our WebSocketManager; they
		// don't know about "clientContext"s yet.
		public void SendLegacy(string eventId, string message)
		{
			SendString("legacy", eventId, message);
		}

		public void SendString(string clientContext, string eventId, string message)
		{
			dynamic eventBundle = new DynamicJson();
			eventBundle.message = message;
			SendBundle(clientContext, eventId, eventBundle);
		}
		public void SendEvent(string clientContext, string eventId)
		{
			dynamic eventBundle = new DynamicJson(); // nothing to put in it
			SendBundle(clientContext, eventId, eventBundle);
		}

		public void Dispose()
		{
			lock(this)
			{
				if(_server != null)
				{
					// Note that sockets remove themselves from _allSockets when they are closed.
					foreach(var socket in _allSockets.ToArray())
					{
						Debug.WriteLine($"*** This socket was still open and is being closed during shutdown: \"{socket?.ConnectionInfo?.SubProtocol}\"");
						socket?.Close();
					}
					_allSockets.Clear();
					_server.Dispose();
					_server = null;
				}
			}
		}
	}
}
