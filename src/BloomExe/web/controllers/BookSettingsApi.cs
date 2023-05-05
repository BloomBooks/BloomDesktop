using System;
using System.Dynamic;
using Bloom.Book;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			// Not sure this needs UI thread, but it can result in saving the page, which seems
			// safest to do that way.
			apiHandler.RegisterEndpointLegacy("book/settings", HandleBookSettings, true);
		}

		/// <summary>
		/// Get a json of the book's settings.
		/// Not used at present. May be obsolete if book settings are done using config-r
		/// </summary>
		private void HandleBookSettings(ApiRequest request)
		{
			switch (request.HttpMethod)
			{
				case HttpMethods.Get:
					var settings = new
					{
						currentToolBoxTool = _bookSelection.CurrentSelection.BookInfo.CurrentTool,
						appearance = new { coverColor = _bookSelection.CurrentSelection.GetCoverColor() },
						//bloomPUB = new { imageSettings = new { maxWidth= _bookSelection.CurrentSelection.BookInfo.PublishSettings.BloomPub.ImageSettings.MaxWidth, maxHeight= _bookSelection.CurrentSelection.BookInfo.PublishSettings.BloomPub.ImageSettings.MaxHeight} }
						publish = _bookSelection.CurrentSelection.BookInfo.PublishSettings
					};
					var jsonData = JsonConvert.SerializeObject(settings);

#if UserControlledTemplate
					settings.isTemplateBook = GetIsBookATemplate();
#endif
					request.ReplyWithJson(jsonData);
					break;
				case HttpMethods.Post:
					//note: since we only have this one value, it's not clear yet whether the panel involved here will be more of a
					//an "edit settings", or a "book settings", or a combination of them.
					var json = request.RequiredPostJson();
					dynamic newSettings = Newtonsoft.Json.Linq.JObject.Parse(json);
					var c = newSettings.appearance.coverColor;
					_bookSelection.CurrentSelection.SetCoverColor(c.ToString());
					// review: crazy bit here, that above I'm taking json, parsing it into object, and grabbing part of it. But then
					// here we take it back to json and pass it to this thing that is going to parse it agian. In this case, speed
					// is irrelevant. The nice thing is, it retains the identity of PublishSettings in case someone is holing on it.
					var jsonOfJustPublishSettings = JsonConvert.SerializeObject(newSettings.publish);
					_bookSelection.CurrentSelection.BookInfo.PublishSettings.LoadNewJson(jsonOfJustPublishSettings);

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
