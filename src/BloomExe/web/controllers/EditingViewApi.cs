using System.Windows.Forms;
using Bloom.Api;
using Bloom.Edit;
using L10NSharp;

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

			var fileType = ".wdgt";
			var dlg = new DialogAdapters.OpenFileDialogAdapter
			{
				Multiselect = false,
				CheckFileExists = true,
				Filter = $"{fileType} files|*{fileType}"
			};
			var result = dlg.ShowDialog();
			if (result != DialogResult.OK)
			{
				request.ReplyWithText("");
				return;
			}

			var fullWidgetPath = dlg.FileName;
			string activityName = View.Model.MakeActivity(fullWidgetPath);
			var url = UrlPathString.CreateFromUnencodedString(activityName);
			request.ReplyWithText(url.UrlEncodedForHttpPath);
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
	}
}
