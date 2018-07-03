using System;
using System.Diagnostics;
using Bloom.Api;
using L10NSharp;

namespace Bloom.web
{
	/// <summary>
	/// Sends localized messages to a websocket, intended for html display
	/// </summary>
	public class WebSocketProgress: IWebSocketProgress
	{
		private readonly IBloomWebSocketServer _bloomWebSocketServer;
		private readonly string _clientContext;
		private string _l10IdPrefix;

		/// <summary>
		/// Get a new WebSocketProgress that will prefix each localization id with the given string
		/// </summary>
		/// <param name="localizationIdPrefix"></param>
		public WebSocketProgress WithL10NPrefix(string localizationIdPrefix)
		{
			return new WebSocketProgress(_bloomWebSocketServer, _clientContext)
			{
				_l10IdPrefix = localizationIdPrefix
			};
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

		public void ErrorWithoutLocalizing(string message, params object[] args)
		{
			MessageWithoutLocalizing($"<span style='color:red'>{message}</span>", args);
		}
		public void Error(string idSuffix, string message)
		{
			ErrorWithoutLocalizing(LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + idSuffix, englishText: message));
		}

		public void MessageWithoutLocalizing(string message, params object[] args)
		{
			_bloomWebSocketServer.SendString(_clientContext, "progress", String.Format(message, args));
		}

		public void MessageWithStyleWithoutLocalizing(string message, string cssStyleRules)
		{
			dynamic messageBundle = new DynamicJson();
			messageBundle.message = message;
			messageBundle.cssStyleRule = cssStyleRules;
			_bloomWebSocketServer.SendBundle(_clientContext, "progress", messageBundle);
		}

		public void Message(string idSuffix, string comment, string message)
		{
			MessageWithoutLocalizing(LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + idSuffix, englishText: message, comment: comment));
		}
		public void Message(string idSuffix, string message)
		{
			MessageWithoutLocalizing(LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + idSuffix, englishText: message));
		}

		// Use with care: if the first parameter is a string, you can leave out one of the earlier arguments with no compiler warning.
		public void MessageWithParams(string idSuffix, string comment, string message, params object[] parameters)
		{
			var formatted = GetMessageWithParams(idSuffix, comment, message, parameters);
			MessageWithoutLocalizing(formatted);
		}

		// Use with care: if the first parameter is a string, you can leave out one of the earlier arguments with no compiler warning.
		public void ErrorWithParams(string idSuffix, string comment, string message, params object[] parameters)
		{
			var formatted = GetMessageWithParams(idSuffix, comment, message, parameters);
			ErrorWithoutLocalizing(formatted);
		}

		// Use with care: if the first parameter is a string, you can leave out one of the earlier arguments with no compiler warning.
		public void MessageWithColorAndParams(string idSuffix, string comment, string color, string message, params object[] parameters)
		{
			var formatted = GetMessageWithParams(idSuffix, comment, message, parameters);
			MessageWithoutLocalizing("<span style='color:" + color + "'>" + formatted + "</span>");
		}

		// Use with care: if the first parameter is a string, you can leave out one of the earlier arguments with no compiler warning.
		public string GetMessageWithParams(string idSuffix, string comment, string message, params object[] parameters)
		{
			Debug.Assert(message.Contains("{0}"));
			var localized = LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + idSuffix, englishText: message,
				comment: comment);
			var formatted = String.Format(localized, parameters);
			return formatted;
		}

		public void MessageUsingTitle(string idSuffix, string message, string bookTitle)
		{
			var formatted = GetTitleMessage(idSuffix, message, bookTitle);
			MessageWithoutLocalizing(formatted);
		}

		public string GetTitleMessage(string idSuffix, string message, string bookTitle)
		{
			Debug.Assert(message.Contains("{0}"));
			Debug.Assert(!message.Contains("{1}"));
			var localized = LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + idSuffix, englishText: message,
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

	// Useful where we want to substitute a test stub. Currently I'm only including the methods we actually want to
	// use that way.
	public interface IWebSocketProgress
	{
		void MessageWithoutLocalizing(string message, params object[] args);
		void ErrorWithoutLocalizing(string message, params object[] args);
		void Message(string idSuffix, string comment, string message);
		void MessageWithParams(string id, string comment, string message, params object[] parameters);
		void ErrorWithParams(string id, string comment, string message, params object[] parameters);
		void MessageWithColorAndParams(string id, string comment, string color, string message, params object[] parameters);
	}

	// Passing one of these where we don't need the progress report saves recipients handling nulls
	public class NullWebSocketProgress : IWebSocketProgress
	{
		public void MessageWithoutLocalizing(string message, params object[] args)
		{
		}

		public void ErrorWithoutLocalizing(string message, params object[] args)
		{
		}

		public void Message(string id, string comment, string message)
		{
		}

		public void MessageWithParams(string id, string comment, string message, params object[] parameters)
		{
		}

		public void ErrorWithParams(string id, string comment, string message, params object[] parameters)
		{
		}

		public void MessageWithColorAndParams(string id, string comment, string color, string message, params object[] parameters)
		{
		}
	}
}
