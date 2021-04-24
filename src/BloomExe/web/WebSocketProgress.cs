using System;
using System.Diagnostics;
using Bloom.Api;
using L10NSharp;

namespace Bloom.web
{
	public enum MessageKind
	{
		Error, Warning, Instruction, Note, Progress
	};


	public interface IWebSocketProgress
	{
		void MessageWithoutLocalizing(string message, MessageKind kind=MessageKind.Progress);
		void Message(string idSuffix, string comment, string message, MessageKind messageKind = MessageKind.Progress, bool useL10nIdPrefix =true);
		void Message(string idSuffix, string message, MessageKind kind=MessageKind.Progress, bool useL10nIdPrefix = true);
		void MessageWithParams(string idSuffix, string comment, string message, MessageKind kind, params object[] parameters);
	}

	/// <summary>
	/// Sends localized messages to a websocket, intended for html display
	/// </summary>
	public class WebSocketProgress : IWebSocketProgress
	{


		private readonly IBloomWebSocketServer _bloomWebSocketServer;
		private readonly string _clientContext;
		private string _l10IdPrefix;

		private string GetL10nId(string idSuffix, bool useL10nIdPrefix)
		{
			if (useL10nIdPrefix)
				return _l10IdPrefix + idSuffix;
			else
				return idSuffix;
		}

		/// <summary>
		/// Get a new WebSocketProgress that will prefix each localization id with the given string
		/// </summary>
		/// <param name="localizationIdPrefix"></param>
		public virtual WebSocketProgress WithL10NPrefix(string localizationIdPrefix)
		{
			return new WebSocketProgress(_bloomWebSocketServer, _clientContext)
			{
				_l10IdPrefix = localizationIdPrefix
			};
		}

		protected WebSocketProgress()
		{
		}

		/// <summary>
		/// Get an object that you can use to send messages to a given clientContext
		/// </summary>
		/// <param name="bloomWebSocketServer"></param>
		/// <param name="clientContext">This goes out with our messages and, on the client side (typescript), messages are filtered
		/// down to the context (usualy a screen) that requested them.</param>
		public WebSocketProgress(IBloomWebSocketServer bloomWebSocketServer, string clientContext)
		{
			_bloomWebSocketServer = bloomWebSocketServer;
			_clientContext = clientContext;
		}

		[Obsolete("Instead, use normal messages with an kind=Error")]
		public virtual void ErrorWithoutLocalizing(string message)
		{
			MessageWithoutLocalizing(message, MessageKind.Error);
		}

		[Obsolete("Instead, use normal messages with an kind=Error")]
		public void Error(string idSuffix, string message, bool useL10nIdPrefix = true)
		{
			var localizedMessage = LocalizationManager.GetDynamicString(appId: "Bloom", id: GetL10nId(idSuffix, useL10nIdPrefix),
				englishText: message);
			ErrorWithoutLocalizing(localizedMessage);
			if (localizedMessage != message)
				SIL.Reporting.Logger.WriteEvent($"Error: {message}"); // repeat message in the log unlocalized.
		}

		public virtual void MessageWithoutLocalizing(string message, MessageKind kind=MessageKind.Progress)
		{
			dynamic messageBundle = new DynamicJson();
			messageBundle.message = message;
			messageBundle.messageKind = kind.ToString();
			_bloomWebSocketServer.SendBundle(_clientContext, "message", messageBundle);
		}

		public virtual void Message(string idSuffix, string comment, string message, MessageKind messageKind = MessageKind.Progress, bool useL10nIdPrefix =true)
		{
			MessageWithoutLocalizing(LocalizationManager.GetDynamicString(appId: "Bloom", id: GetL10nId(idSuffix, useL10nIdPrefix), englishText: message, comment: comment),kind:messageKind);
		}
		public void Message(string idSuffix, string message, MessageKind messageKind = MessageKind.Progress, bool useL10nIdPrefix = true)
		{
			MessageWithoutLocalizing(LocalizationManager.GetDynamicString(appId: "Bloom", id: GetL10nId(idSuffix, useL10nIdPrefix), englishText: message), kind: messageKind);
		}

		// Use with care: if the first parameter is a string, you can leave out one of the earlier arguments with no compiler warning.
		public virtual void MessageWithParams(string idSuffix, string comment, string message, MessageKind messageKind, params object[] parameters)
		{
			var formatted = GetMessageWithParams(idSuffix, comment, message, parameters);
			MessageWithoutLocalizing(formatted, messageKind);
		}

		// Use with care: if the first parameter is a string, you can leave out one of the earlier arguments with no compiler warning.
		public string GetMessageWithParams(string idSuffix, string comment, string message, params object[] parameters)
		{
			Debug.Assert(message.Contains("{0}"));
			var localized = LocalizationManager.GetDynamicString(appId: "Bloom", id: GetL10nId(idSuffix, true), englishText: message,
				comment: comment);
			var formatted = String.Format(localized, parameters);
			return formatted;
		}

		public void MessageUsingTitle(string idSuffix, string message, string bookTitle, MessageKind kind, bool useL10nIdPrefix = true)
		{
			var formatted = GetTitleMessage(idSuffix, message, bookTitle, useL10nIdPrefix);
			MessageWithoutLocalizing(formatted, kind);
		}

		public string GetTitleMessage(string idSuffix, string message, string bookTitle, bool useL10nIdPrefix = true)
		{
			Debug.Assert(message.Contains("{0}"));
			Debug.Assert(!message.Contains("{1}"));
			var localized = LocalizationManager.GetDynamicString(appId: "Bloom", id: GetL10nId(idSuffix, useL10nIdPrefix), englishText: message,
				comment: "{0} is a book title");
			var formatted = String.Format(localized, bookTitle);
			return formatted;
		}

		public void Exception(Exception exception)
		{
			ErrorWithoutLocalizing(exception.Message);
			ErrorWithoutLocalizing(exception.StackTrace);
		}
	}

	// Passing one of these where we don't need the progress report saves recipients handling nulls
	public class NullWebSocketProgress : WebSocketProgress
	{
		public override void MessageWithoutLocalizing(string message, MessageKind kind)
		{
		}

		public override WebSocketProgress WithL10NPrefix(string localizationIdPrefix)
		{
			return this;
		}
	}
}
