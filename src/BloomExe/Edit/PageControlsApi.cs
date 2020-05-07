using System;
using System.Diagnostics;
using System.Windows.Forms;
using Bloom.Api;
using Newtonsoft.Json;

namespace Bloom.Edit
{
	/// <summary>
	/// Handles api request dealing with the page manipulation controls at the bottom of the WebThumbnailList
	/// (left side of Edit tab screen).
	/// </summary>
	public class PageControlsApi
	{
		private const string kApiUrlPart = "edit/pageControls/";
		private const string kWebsocketStateId = "edit/pageControls/state";
		private const string kWebsocketContext = "pageThumbnailList-pageControls";
		private readonly BloomWebSocketServer _webSocketServer;
		private readonly EditingModel _editingModel;
		private DateTime _lastButtonClickedTime = DateTime.Now; // initially, instance creation time

		public PageControlsApi(EditingModel model)
		{
			_editingModel = model;
			_webSocketServer = _editingModel.EditModelSocketServer;
			_editingModel.PageSelectModelChangesComplete += PageSelectModelChangesCompleteHandler;
		}

		private void PageSelectModelChangesCompleteHandler(object sender, EventArgs e)
		{
			UpdateState(); // tell React model that the C# state changed
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "requestState", request =>
			{
				if (request.HttpMethod == HttpMethods.Get)
				{
					request.ReplyWithJson(CurrentStateString);
				}
				else // post
				{
					Debug.Fail("We shouldn't ever be using the 'post' version.");
					request.PostSucceeded();
				}
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "addPage", request =>
			{
				AddPageButton_Click();
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "duplicatePage", request =>
			{
				_editingModel.OnDuplicatePage();
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "deletePage", request =>
			{
				if (ConfirmRemovePageDialog.Confirm())
					_editingModel.OnDeletePage();
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "lockBook", request =>
			{
				_editingModel.SaveNow(); // BL-5421 lock and unlock lose typing
				_editingModel.CurrentBook.TemporarilyUnlocked = false;
				request.PostSucceeded();
				UpdateState(); // because we aren't selecting a new page
				_editingModel.RefreshDisplayOfCurrentPage();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "unlockBook", request =>
			{
				_editingModel.SaveNow(); // BL-5421 lock and unlock lose typing
				_editingModel.CurrentBook.TemporarilyUnlocked = true;
				request.PostSucceeded();
				UpdateState(); // because we aren't selecting a new page
				_editingModel.RefreshDisplayOfCurrentPage();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "cleanup", request =>
			{
				SendCleanupState();
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "zoomMinus", request =>
			{
				_editingModel.AdjustPageZoom(-10);
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "zoomPlus", request =>
			{
				_editingModel.AdjustPageZoom(10);
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "requestVideoPlaceHolder", request =>
			{
				_editingModel.RequestVideoPlaceHolder();
				request.PostSucceeded();
			}, true);

			apiHandler.RegisterEndpointHandler(kApiUrlPart + "requestWidgetPlaceHolder", request =>
			{
				_editingModel.RequestWidgetPlaceHolder();
				request.PostSucceeded();
			}, true);
		}
		private void SendCleanupState()
		{
			var endState = "{\"CanAddPages\":false,\"CanDeletePage\":false,\"CanDuplicatePage\":false,\"BookLockedState\":\"OriginalBookMode\"}";
			_webSocketServer.SendString(kWebsocketContext, kWebsocketStateId, endState);
		}

		private void UpdateState()
		{
			_webSocketServer.SendString(kWebsocketContext, kWebsocketStateId, CurrentStateString);
		}

		private string CurrentStateString
		{
			get
			{
				return JsonConvert.SerializeObject(
					new
					{
						_editingModel.CanAddPages,
						_editingModel.CanDeletePage,
						_editingModel.CanDuplicatePage,
						BookLockedState
					});
			}
		}

		private string BookLockedState
		{
			get
			{

				if (_editingModel.CurrentBook.CollectionSettings.IsSourceCollection)
					return "NoLocking";
				return !_editingModel.CurrentBook.RecordedAsLockedDown
					? "OriginalBookMode"
					: _editingModel.CurrentBook.LockedDown
						? "BookLocked"
						: "BookUnlocked";
			}
		}

		private void AddPageButton_Click()
		{
			// Turn double-click into a single-click
			if (_lastButtonClickedTime > DateTime.Now.AddSeconds(-1))
				return;
			_lastButtonClickedTime = DateTime.Now;

			if (_editingModel.CanAddPages)
			{
				_editingModel.ShowAddPageDialog();
			}
			else
			{
				// TODO: localize buttons
				MessageBox.Show(EditingView.GetInstructionsForUnlockingBook(), "Bloom", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}
	}
}
