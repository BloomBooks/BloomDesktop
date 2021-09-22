using System;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Edit;
using L10NSharp;
using SIL.IO;

namespace Bloom.web.controllers
{
	/// <summary>
	/// Api for handling requests regarding the edit tab view itself
	/// </summary>
	public class EditingViewApi
	{
		public EditingView View { get; set; }

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("editView/setModalState", HandleSetModalState, true);
			apiHandler.RegisterEndpointHandler("editView/chooseWidget", HandleChooseWidget, true);
			apiHandler.RegisterEndpointHandler("editView/getBookColors", HandleGetColors, true);
			apiHandler.RegisterEndpointHandler("editView/editPagePainted", HandleEditPagePainted, true);
			apiHandler.RegisterEndpointHandler("editView/saveToolboxSetting", HandleSaveToolboxSetting, true);
			apiHandler.RegisterEndpointHandler("editView/setTopic", HandleSetTopic, true);
			apiHandler.RegisterEndpointHandler("editView/isTextSelected", HandleIsTextSelected, false);
			apiHandler.RegisterEndpointHandler("editView/getBookLangs", HandleGetBookLangs, false);
			apiHandler.RegisterEndpointHandler("editView/isClipboardBookHyperlink", HandleIsClipboardBookHyperlink, false);
			apiHandler.RegisterEndpointHandler("editView/requestTranslationGroupContent", RequestDefaultTranslationGroupContent, true);
		}

		// Answer true if the current clipboard contents are something that makes sense to paste into the href
		// of a hyperlink in a Bloom Book. Currently we allow all http(s) and mailto links, plus internal links
		// (starting with #) provided they are to a non-xmatter page that is present in the book.
		private void HandleIsClipboardBookHyperlink(ApiRequest request)
		{
			string clipContent = ""; // initial value is not used, delegate will set it.
			Program.MainContext.Send(o =>
			{
				try
				{
					clipContent = Clipboard.GetText();
				}
				catch (Exception e)
				{
					// Need to make sure to handle exceptions.
					// If the worker thread dies with an unhandled exception,
					// it causes the whole program to immediately crash without opportunity for error reporting
					request.ReplyWithBoolean(false);
				}
			}, null);
			
			request.ReplyWithBoolean(IsBloomHyperlink(clipContent, request.CurrentBook));
		}

		private bool IsBloomHyperlink(string text, Book.Book book)
		{
			if (string.IsNullOrEmpty(text))
				return false;
			// This is simplisitic but enough to prevent most nonsensical URLs being put in links.
			if (text.StartsWith("http:") || text.StartsWith("https:") || text.StartsWith("mailto:"))
				return true;
			if (!text.StartsWith("#"))
				return false;
			// This is looking like an internal link. It had better be a valid page in this book.
			// For now it is no good linking to xmatter pages because their IDs change.
			var id = text.Substring(1);
			if (book == null)
				return false;
			return book.GetPages().Any(page => page.Id == id && !page.IsXMatter);
		}

		private void RequestDefaultTranslationGroupContent(ApiRequest request)
		{
			View.Model.RequestDefaultTranslationGroupContent(request);
		}

		private void HandleGetBookLangs(ApiRequest request)
		{
			var bookData = request.CurrentBook.BookData;
			dynamic answer = new ExpandoObject();
			answer.V = bookData.Language1.Name;
			answer.N1 = bookData.MetadataLanguage1.Name;
			var n2Name = bookData.MetadataLanguage2?.Name;
			answer.N2 = string.IsNullOrEmpty(n2Name)? "-----" : n2Name;
			request.ReplyWithJson(answer);
		}

		private void HandleIsTextSelected(ApiRequest request)
		{
			EditingModel.IsTextSelected = request.RequiredPostBooleanAsJson();
			request.PostSucceeded();
		}

		public void HandleSetModalState(ApiRequest request)
		{
			lock (request)
			{
				View.SetModalState(request.RequiredPostBooleanAsJson());
				request.PostSucceeded();
			}
		}

		private void HandleChooseWidget(ApiRequest request)
		{
			if (!View.Model.CanChangeImages())
			{
				// Enhance: more widget-specific message?
				MessageBox.Show(
					LocalizationManager.GetString("EditTab.CantPasteImageLocked",
						"Sorry, this book is locked down so that images cannot be changed."));
				request.ReplyWithText("");
				return;
			}

			using (var dlg = new DialogAdapters.OpenFileDialogAdapter
			{
				Multiselect = false,
				CheckFileExists = true,
				Filter = "Widget files|*.wdgt;*.html;*.htm"
			})
			{
				var result = dlg.ShowDialog();
				if (result != DialogResult.OK)
				{
					request.ReplyWithText("");
					return;
				}

				var fullWidgetPath = dlg.FileName;
				var ext = Path.GetExtension(fullWidgetPath);
				if (ext.EndsWith("htm") || ext.EndsWith("html"))
				{
					fullWidgetPath = EditingModel.CreateWidgetFromHtmlFolder(fullWidgetPath);
				}
				string activityName = View.Model.MakeActivity(fullWidgetPath);
				var url = UrlPathString.CreateFromUnencodedString(activityName);
				request.ReplyWithText(url.UrlEncodedForHttpPath);
				// clean up the temporary widget file we created.
				if (fullWidgetPath != dlg.FileName)
					RobustFile.Delete(fullWidgetPath);
			}
		}

		private void HandleGetColors(ApiRequest request)
		{
			var model = View.Model;
			if (!model.HaveCurrentEditableBook)
			{
				request.ReplyWithText("");
				return;
			}
			var currentBook = View.Model.CurrentBook;
			// Enhance: Two ideas. (1) Get colors from the current page first, in case the book has too many
			// colors to fit in our dialog's swatch array. (2) Order the list returned by frequency, so the most
			// frequently used colors are at the front of the resultant swatch array.
			var currentBookDom = currentBook.OurHtmlDom;
			var colors = currentBookDom.GetColorsUsedInBookBubbleElements();
			request.ReplyWithText(colors);
		}

		private void HandleEditPagePainted(ApiRequest request)
		{
			View.Model.HandleEditPagePaintedEvent(this, new EventArgs());
			request.PostSucceeded();
		}

		private void HandleSaveToolboxSetting(ApiRequest request)
		{
			var settingString = request.RequiredPostString();
			View.Model.SaveToolboxSettings(settingString);
			request.PostSucceeded();
		}

		private void HandleSetTopic(ApiRequest request)
		{
			var topicString = request.RequiredPostString();
			// RequiredPostString cannot be empty, so we use a substitute value for empty.
			if (topicString == "<NONE>")
				topicString = "";
			View.Model.SetTopic(topicString);
			request.PostSucceeded();
		}
	}
}
