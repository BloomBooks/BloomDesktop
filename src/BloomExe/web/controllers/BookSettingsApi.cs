using System;
using System.Dynamic;
using Bloom.Book;

namespace Bloom.Api
{
	/// <summary>
	/// Exposes some settings of the current Book via API
	/// </summary>
	public class BookSettingsApi
	{
		private readonly BookSelection _bookSelection;
		private readonly PageRefreshEvent _pageRefreshEvent;
		private readonly Book.Book.BookType _originalBookType;
		private bool _isTemplateBook;

		public BookSettingsApi(BookSelection bookSelection, PageRefreshEvent pageRefreshEvent)
		{
			_bookSelection = bookSelection;
			_pageRefreshEvent = pageRefreshEvent;
			if (_bookSelection != null && _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.BookInfo != null)
				_originalBookType = _bookSelection.CurrentSelection.BookInfo.Type;
			else
				_originalBookType = Book.Book.BookType.Publication;
			_isTemplateBook = _originalBookType == Book.Book.BookType.Template;
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler("book/settings", HandleBookSettings);
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
					settings.isTemplateBook = _isTemplateBook;
					request.ReplyWithJson((object)settings);
					break;
				case HttpMethods.Post:
					//note: since we only have this one value, it's not clear yet whether the panel involved here will be more of a
					//an "edit settings", or a "book settings", or a combination of them.
					var json = request.RequiredPostJson();
					settings = DynamicJson.Parse(json);
					_bookSelection.CurrentSelection.TemporarilyUnlocked = settings["unlockShellBook"];
					_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.SaveBeforeRefresh);
					UpdateBookTemplateMode(settings.isTemplateBook);
					request.Succeeded();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void UpdateBookTemplateMode(bool isTemplateBook)
		{
			_isTemplateBook = isTemplateBook;
			if (isTemplateBook)
				_bookSelection.CurrentSelection.BookInfo.Type = Book.Book.BookType.Template;
			else if (_originalBookType == Book.Book.BookType.Template)
				_bookSelection.CurrentSelection.BookInfo.Type = Book.Book.BookType.Publication;
			else
				_bookSelection.CurrentSelection.BookInfo.Type = _originalBookType;
		}
	}
}
