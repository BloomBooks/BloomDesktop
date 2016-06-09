using System;
using System.Dynamic;
using Bloom.Book;

namespace Bloom.Api
{
	/// <summary>
	/// This class is responsible for handling Server requests that depend on knowledge of the current book.
	/// An exception is some reader tools requests, which have their own handler, though most of them depend
	/// on knowing the current book.
	/// </summary>
	public class CurrentBookHandler
	{
		private readonly BookSelection _bookSelection;
		private readonly PageRefreshEvent _pageRefreshEvent;

		public CurrentBookHandler(BookSelection bookSelection, PageRefreshEvent pageRefreshEvent)
		{
			_bookSelection = bookSelection;
			_pageRefreshEvent = pageRefreshEvent;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("bookSettings", HandleBookSettings);
		}

		/// <summary>
		/// Get a json of the book's settings.
		/// </summary>
		private  void HandleBookSettings(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					dynamic settings = new ExpandoObject();
					settings.isRecordedAsLockedDown = _bookSelection.CurrentSelection.RecordedAsLockedDown;
					settings.unlockShellBook = _bookSelection.CurrentSelection.TemporarilyUnlocked;
					settings.currentToolBoxTool = _bookSelection.CurrentSelection.BookInfo.CurrentTool;
					request.ReplyWithJson((object)settings);
					break;
				case HttpMethods.Post:
					//note: since we only have this one value, it's not clear yet whether the panel involved here will be more of a
					//an "edit settings", or a "book settings", or a combination of them.
					settings = DynamicJson.Parse(request.RequiredPostJson());
					_bookSelection.CurrentSelection.TemporarilyUnlocked = settings["unlockShellBook"];
					_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.SaveBeforeRefresh);
					request.Succeeded();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

	
	}
}
