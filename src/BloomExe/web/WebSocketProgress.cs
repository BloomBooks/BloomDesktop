using System;
using System.Diagnostics;
using L10NSharp;

namespace Bloom.web
{
	/// <summary>
	/// Sends localized messages to a websocket, intended for html display
	/// </summary>
	public class WebSocketProgress
	{
		private readonly IBloomWebSocketServer _bloomWebSocketServer;
		private string _l10IdPrefix;

		/// <summary>
		/// Get a new WebSocketProgress that will prefix each localization id with the given string
		/// </summary>
		/// <param name="localizationIdPrefix"></param>
		/// <returns></returns>
		public WebSocketProgress WithL10NPrefix(string localizationIdPrefix)
		{
			return new WebSocketProgress(_bloomWebSocketServer)
			{
				_l10IdPrefix = localizationIdPrefix
			};
		}

		public WebSocketProgress(IBloomWebSocketServer bloomWebSocketServer)
		{
			_bloomWebSocketServer = bloomWebSocketServer;
		}

		public void ErrorWithoutLocalizing(string message, params object[] args)
		{
			MessageWithoutLocalizing($"<span style='color:red'>{message}</span>", args);
		}
		public void Error(string id, string message)
		{
			ErrorWithoutLocalizing(LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + id, englishText: message));
		}

		public void MessageWithoutLocalizing(string message, params object[] args)
		{
			_bloomWebSocketServer.Send("progress", message);
		}

		public void MessageWithStyleWithoutLocalizing(string message, string style)
		{
			_bloomWebSocketServer.Send("progress", message, style);
		}

		public void Message(string id, string message, string comment = null)
		{
			MessageWithoutLocalizing(LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + id, englishText: message, comment: comment));
		}

		public void MessageWithParams(string id, string comment, string message, params object[] parameters)
		{
			var formatted = GetMessageWithParams(id, comment, message, parameters);
			MessageWithoutLocalizing(formatted);
		}

		public string GetMessageWithParams(string id, string comment, string message, params object[] parameters)
		{
			Debug.Assert(message.Contains("{0}"));
			var localized = LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + id, englishText: message,
				comment: comment);
			var formatted = String.Format(localized, parameters);
			return formatted;
		}

		public void MessageUsingTitle(string id, string message, string bookTitle)
		{
			var formatted = GetTitleMessage(id, message, bookTitle);
			MessageWithoutLocalizing(formatted);
		}

		public string GetTitleMessage(string id, string message, string bookTitle)
		{
			Debug.Assert(message.Contains("{0}"));
			Debug.Assert(!message.Contains("{1}"));
			var localized = LocalizationManager.GetDynamicString(appId: "Bloom", id: _l10IdPrefix + id, englishText: message,
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
}
