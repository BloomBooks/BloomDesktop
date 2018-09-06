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

		public BookSettingsApi(BookSelection bookSelection, PageRefreshEvent pageRefreshEvent)
		{
			_bookSelection = bookSelection;
			_pageRefreshEvent = pageRefreshEvent;
		}

		public void RegisterWithServer(FileAndApiServer server)
		{
			// Not sure this needs UI thread, but it can result in saving the page, which seems
			// safest to do that way.
			server.RegisterEndpointHandler("book/settings", HandleBookSettings, true);
		}

		/// <summary>
		/// Get a json of the book's settings.
		/// </summary>
		private void HandleBookSettings(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					dynamic settings = new ExpandoObject();
					settings.isRecordedAsLockedDown = _bookSelection.CurrentSelection.RecordedAsLockedDown;
					settings.unlockShellBook = _bookSelection.CurrentSelection.TemporarilyUnlocked;
					settings.currentToolBoxTool = _bookSelection.CurrentSelection.BookInfo.CurrentTool;
#if UserControlledTemplate
					settings.isTemplateBook = GetIsBookATemplate();
#endif
					request.ReplyWithJson((object)settings);
					break;
				case HttpMethods.Post:
					//note: since we only have this one value, it's not clear yet whether the panel involved here will be more of a
					//an "edit settings", or a "book settings", or a combination of them.
					settings = DynamicJson.Parse(request.RequiredPostJson());
					_bookSelection.CurrentSelection.TemporarilyUnlocked = settings["unlockShellBook"];
					// This first refresh saves any changes.
					_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.SaveBeforeRefresh);
#if UserControlledTemplate
					UpdateBookTemplateMode(settings.isTemplateBook);
					// Now we need to update the active version of the page with possible new template settings
					// It's a bit wasteful to raise this twice...but we need to save any changes the user made to the page,
					// and we have no access to put the editable DOM into the right template/non-template state.
					_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.JustRedisplay);
#endif
					request.PostSucceeded();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private bool GetIsBookATemplate()
		{
			return _bookSelection.CurrentSelection.IsSuitableForMakingShells;
		}
#if UserControlledTemplate
		private void UpdateBookTemplateMode(bool isTemplateBook)
		{
			_bookSelection.CurrentSelection.SwitchSuitableForMakingShells(isTemplateBook);

			/* TODO (non-exhaustive)
			 * For each page, data-page="extra"; but that should be an option on each page.
			 * Add visual feedback that this is a template
			 * Add UI pointer to more help on this topic.
			 *
			 * Other things to think about/test
				User modified styles
				filename endcoding tests
				src vs vern collections
				same name in src collections already
				multiple copies (different versions) in src collections
				"template" in name?
				new pages as extra?
			 */
		}
#endif
	}
}
