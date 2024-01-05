using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Forms;
using Bloom.web;
using Fleck;
using SIL.Reporting;
using Logger = SIL.Reporting.Logger;

namespace Bloom.Api
{
    /// <summary>
    /// Runs a web socket server on the given port. Useful for high-frequency messages (like audio levels)
    /// and allows the backend to send messages to the client.
    ///
    /// About ports... we currently only have a single server that is used to pass any number of messages.
    /// Then on the client side, we have multiple connections to this same server; each connection is called a "socket",
    /// note that these are on the same port.
    /// For outgoing messages from c# to the client code, we have 3 levels:
    /// * clientContext: normally the screen, like "publish to BloomPub".
    /// * eventId: things like "progress message"
    /// * message: (optional) for events that need some text, it goes in here
    ///
    /// Note that there is a subclass of events for Progress boxes and dialogs, defined in IBloomWebSocketProgressEvent,
    /// which adds "progressKind".
    ///
    /// The IBloomWebSocketServer interface allows tests to use a spy to see what messages have been sent.
    /// </summary>
    public class BloomWebSocketServer : IBloomWebSocketServer, IDisposable
    {
        //Note, in normal web apps, you'd have any number of clients opening sockets to this single server. It would be 1 to n. In Bloom, where there is
        //only a single client, we think more in terms of 1 to 1.  However there's nothing preventing multiple parts of the Bloom client from opening their
        //own connection (socket).
        private WebSocketServer _server;

        // It would be nice to use something from .Net's System.Collections.Concurrent namespace, but none of the options
        // seem to fit our needs (i.e. remove a particular connection when it wants to close). To get around this, we will
        // lock the thread whenever we're changing or reading the collection.
        private List<IWebSocketConnection> _allSockets;

        // I (gjm) prefer to use a private object for locking, rather than locking on the list itself.
        // See https://stackoverflow.com/questions/38653739/locking-on-the-object-that-is-being-synchronized-or-using-a-dedicated-lock-objec
        private readonly object _connectionLock = new object();

        // The one instance which any client may use.
        // BloomWebSocketServer wants to be an application singleton, but currently,
        // it is tied to BloomServer, which has some knowledge of the current collection
        // and so is a ProjectContext singleton, and therefore BloomWebSocketServer is, too.
        // However, we only have one Project/Collection open at a time, so there is only
        // one instance of either to worry about. This variable currently gives the most
        // recent instance; if we're ever able to make it an application singleton,
        // it will be the only instance.
        // It's something of a judgment call whether to use this field or to receive
        // an IBloomWebSocketServer as a constructor argument from AutoFac. The latter is
        // helpful if the class that wants one is already created by AutoFac, especially if
        // you want the option of passing in a test stub instead. On the other hand, some
        // objects that want to send notifications are not created by AutoFac, and it
        // becomes messy passing the BloomWebSocketServer through (possibly many) layers of
        // object that don't want to know about it from some AutoFac-created object to the
        // one that needs it. It's even worse if a static method needs to do it. In such
        // cases, using this field can make things cleaner. I've made the property internal
        // so that tests can set it if necessary, but only the constructor here sets it
        // in normal operation.
        public static IBloomWebSocketServer Instance { get; internal set; }

        public BloomWebSocketServer()
        {
            Instance = this;
        }

        public void Init(string port)
        {
            FleckLog.Level = LogLevel.Warn;
            _allSockets = new List<IWebSocketConnection>();
            var websocketaddr = "ws://127.0.0.1:" + port;
            Logger.WriteMinorEvent("Attempting to open a WebSocketServer on " + websocketaddr);
            _server = new WebSocketServer(websocketaddr);
            // If we want, we can specify the subprotocols we are expecting:
            //_server.SupportedSubProtocols = new[] { "performance","pageThumbnailList", "pageThumbnailList-pageControls", "bookTeamCollectionStatus" etc etc};
            // This tells Fleck to be picky.
            // It would allow Chrome to work without any special Chrome code on the client side.
            // But it seems like a pain. Firefox is able to negotiate with Fleck just fine, and
            // we only use subprotocols for debugging, so rather than list every
            // subprotocol we use here, for now I just changed our browser-side code to not send
            // the subprotocol unless we're in Firefox.

            try
            {
                _server.Start(socket =>
                {
                    Debug.WriteLine("subprotocol " + socket.ConnectionInfo.SubProtocol);

                    socket.OnOpen = () =>
                    {
                        // our Typescript WebSocketManager sticks the name of the socket into this subProtocol parameter just for this debugging purpose
                        //Debug.WriteLine($"Opening websocket \"{socket.ConnectionInfo?.SubProtocol}\"");

                        // But that breaks Chrome
                        lock (_connectionLock)
                        {
                            _allSockets.Add(socket);
                        }
                    };
                    socket.OnClose = () =>
                    {
                        // The following is probably out of date as of Bloom 5.0, which fixed the Chrome problem.

                        // NB: In May 2019, we found that chrome could not open a socket, and we'd immediately get here
                        // and close. WebSocketManager.ts:87 WebSocket connection to 'ws://127.0.0.1:8090/' failed:
                        // Error during WebSocket handshake: Sent non-empty 'Sec-WebSocket-Protocol' header but no response
                        // was received.
                        Debug.WriteLine(
                            $"Closing websocket \"{socket.ConnectionInfo?.SubProtocol}\""
                        );
                        lock (_connectionLock)
                        {
                            _allSockets.Remove(socket);
                            socket.Close();
                        }
                    };
                    socket.OnError = (err) =>
                    {
                        var msg =
                            $"Error on websocket \"{socket.ConnectionInfo?.SubProtocol}\": {err}";
                        Debug.WriteLine(msg);
                        Logger.WriteError(err);
                    };
                });
            }
            catch (SocketException ex)
            {
                Logger.WriteEvent(
                    "Opening a WebSocketServer on " + websocketaddr + " failed.  Error = " + ex
                );
                ReportSocketExceptionAndExit(ex, _server);
            }
        }

        private const string SocketExceptionMsg =
            "Bloom cannot start properly, because something prevented it from opening port {1},{0}"
            + "which different parts of Bloom use to talk to each other.{0}"
            + "Possibly another version of Bloom is running, perhaps not very obviously.{0}"
            + "Other things that can cause this are firewalls (including Covenant Eyes) and anti-malware programs.{0}{0}"
            + "What can you do?{0}"
            + "When you click OK, Bloom will exit. Then, restart your computer.{0}"
            + "If that doesn't fix the problem, try disabling any third-party firewalls and anti-malware programs (Microsoft's are OK).{0}"
            + "If the problem keeps happening, click 'Details' and report the problem to the developers.";

        /// <summary>
        /// Internal for re-use by CommandAvailabilityPublisher
        /// </summary>
        internal static void ReportSocketExceptionAndExit(
            SocketException exception,
            WebSocketServer server
        )
        {
            ErrorReport.NotifyUserOfProblem(
                exception,
                SocketExceptionMsg,
                Environment.NewLine,
                server.Port
            );
            Application.Exit();
        }

        /// <summary>
        /// Sends a message & parameters that will get picked up a React dialog that is using the useSetupBloomDialogFromServer hook.
        /// </summary>
        public void LaunchDialog(string dialogId, dynamic dialogParameters)
        {
            SendBundle("LaunchDialog", dialogId, dialogParameters);
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
            // We're going to take this and add routing info to it, so it's no longer just the "details".
            var eventObject = eventBundle;
            eventObject.clientContext = clientContext;
            eventObject.id = eventId;

            // Note, if there is no open socket, this isn't going to do anything, and that's (currently) fine.
            IWebSocketConnection[] connectionArray;
            lock (_connectionLock)
            {
                // The ToArray() here gives us a copy so that if a socket is removed while we're doing the 'foreach' below,
                // it will be ok.
                // We actually got a user report (BL-12245) of an error where _allSockets apparently added another connection
                // in the midst of doing the copy that is part of ToArray(). That's why we've now added locks anywhere in this
                // class that changes or reads '_allSockets'.
                connectionArray = _allSockets.ToArray();
            }

            foreach (var socket in connectionArray)
            {
                // see if it's been removed
                bool socketIsPresent;
                lock (_connectionLock)
                {
                    socketIsPresent = _allSockets.Contains(socket);
                }
                if (socketIsPresent)
                {
                    try
                    {
                        if (socket != null && socket.IsAvailable)
                        {
                            // If this fails, it is likely to result in a TaskScheduler.UnobservedTaskException event
                            // and will not get caught in the catch clause below. But it does get picked up
                            // by Sentry's handler and reported. Now that we are checking for IsAvailable above,
                            // we don't expect that to happen anymore. See BL-11124.

                            // In addition now that we aren't locking this whole method, we need to catch the
                            // ConnectionNotAvailableException that we get if the socket becomes unavailable
                            // between when we check for its presence and when we try to send to it.

                            // Don't be tempted to reinstate the locking to fix this. We don't want to hold a lock
                            // on either the whole collection of sockets or a particular one while calling 'Send()'.
                            // The reason is that it's not certain that the 'Send()' will return before the javascript
                            // that receives it starts to do things. That javascript might make calls back to our server
                            // on another thread. If that results in trying to broadcast another socket message,
                            // while this thread still has the socket server locked, we'll be in a deadlock.
                            // (It's actually unlikely that the same thread that sends the message is used to handle it
                            // in Javascript, or even gets held up until the message is handled, especially in WebView2
                            // which mostly runs in another process. But let's not risk it.)
                            try
                            {
                                socket.Send(eventObject.ToString());
                            }
                            catch (ConnectionNotAvailableException)
                            {
                                if (!ApplicationUpdateSupport.IsDev)
                                {
                                    // We don't want to bother users with this, but we do want to know about it.
                                    Logger.WriteEvent(
                                        "web socket was available, until actually trying to send"
                                    );
                                    continue;
                                }
                                ReportConnectionError(socket, eventObject, eventBundle);
                            }
                        }
                        else
                        {
                            if (!ApplicationUpdateSupport.IsDev)
                            {
                                // We don't want to bother users with this, but we do want to know about it.
                                Logger.WriteEvent(
                                    "web socket is not available when trying to send"
                                );
                                continue;
                            }
                            ReportConnectionError(socket, eventObject, eventBundle);
                        }
                    }
                    catch (Exception error)
                    {
                        // See comment on Send call above. We don't expect this catch clause to fire,
                        // but I'm leaving it here in case there is another situation we don't know about.
                        NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, exception: error);
                    }
                }
            }
        }

        private void ReportConnectionError(
            IWebSocketConnection socket,
            dynamic eventObject,
            dynamic eventBundle
        )
        {
            // In mid-April 2022, we realized that we were not checking for IsAvailable and were getting
            // lots of Sentry errors on the original call: `socket?.Send(eventObject.ToString());`.
            // So we started only calling `Send` if `socket.IsAvailable` but also started sending
            // Sentry errors with more info, hoping to discern why we are getting into that state so much.
            // We did get over 200 reports for "web socket is not available when trying to send"
            // but the info did not give us any more understanding. So we decided to stop sending the Sentry
            // reports. For now, we'll bother devs if they get into this situation. But it may soon
            // be evident that we don't want to even do that. See BL-11124.
            // Note that while we don't have an explanation of why/how this happens at all for nulls or
            // so often for !IsAvailable, we are hopeful that users aren't actually losing important events.
            // Likely, the socket we want to send on is truly irrelevant by the time we get into this situation.
            // It is important to note that we are trying to send on all open sockets.
            // So hopefully the one we actually care about is being sent on successfully.
            var subProtocolDetail =
                socket?.ConnectionInfo?.SubProtocol == null
                    ? "subprotocol is null"
                    : $"subprotocol: {socket.ConnectionInfo.SubProtocol}";
            var connectionInfoDetail =
                socket?.ConnectionInfo == null
                    ? "socket.ConnectionInfo is null"
                    : subProtocolDetail;
            var socketDetail =
                socket == null
                    ? "web socket is null; "
                    : $"web socket is not available, {connectionInfoDetail}; ";
            // JT Dec 2023: getting so many of these they drive me crazy, and sometimes manange to deadlock our server.
            // I'm becoming more and more convinced that we're just including sockets on pages that we have navigated away from
            // or even opened by browser windows we have closed. I do note that they seem more common when the system has been asleep
            // while Bloom is running.
            NonFatalProblem.Report(
                ModalIf.None,
                PassiveIf.Alpha,
                "web socket is not available when trying to send",
                $"{socketDetail} bundle clientContext: {eventObject.clientContext}, eventId: {eventObject.id}, eventBundle: {eventBundle}",
                skipSentryReport: true
            );
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
            lock (_connectionLock)
            {
                if (_server != null)
                {
                    // Note that sockets remove themselves from _allSockets when they are closed.
                    foreach (var socket in _allSockets.ToArray())
                    {
                        Debug.WriteLine(
                            $"*** This socket was still open and is being closed during shutdown: \"{socket?.ConnectionInfo?.SubProtocol}\""
                        );
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
