using System;
using System.Collections.Generic;
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
		/// <summary>
		/// Map for obtaining the original types of books that we're looking at.
		/// </summary>
		private readonly Dictionary<Book.BookInfo, Book.Book.BookType> _bookOriginalType = new Dictionary<Book.BookInfo, Book.Book.BookType>();

		public BookSettingsApi(BookSelection bookSelection, PageRefreshEvent pageRefreshEvent)
		{
			_bookSelection = bookSelection;
			_pageRefreshEvent = pageRefreshEvent;
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
					settings.isTemplateBook = GetIsBookATemplate();
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

		private bool GetIsBookATemplate()
		{
			if (_bookSelection == null || _bookSelection.CurrentSelection == null || _bookSelection.CurrentSelection.BookInfo == null)
				return false;
			var info = _bookSelection.CurrentSelection.BookInfo;
			SaveOriginalTypeIfNecessary(info);
			return info.Type == Book.Book.BookType.Template;
		}

		private void UpdateBookTemplateMode(bool isTemplateBook)
		{
			if (_bookSelection == null || _bookSelection.CurrentSelection == null || _bookSelection.CurrentSelection.BookInfo == null)
				return;     // sheer paranoia
			var info = _bookSelection.CurrentSelection.BookInfo;
			SaveOriginalTypeIfNecessary(info);
			if (isTemplateBook)
			{
				info.Type = Book.Book.BookType.Template;
			}
			else
			{
				Book.Book.BookType type = _bookOriginalType[info];
				if (type == Book.Book.BookType.Template)
					type = Book.Book.BookType.Publication;
				info.Type = type;
			}
		}

		private void SaveOriginalTypeIfNecessary(BookInfo info)
		{
			if (!_bookOriginalType.ContainsKey(info))
				_bookOriginalType.Add(info, _bookSelection.CurrentSelection.BookInfo.Type);
		}
	}
}
