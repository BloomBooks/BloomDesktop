using System;
using System.Diagnostics;
using System.Linq;
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


		// kick off an operation that typically will refresh the current page.
		// We want to post success BEFORE we proceed with an operation that will unload the page that sent
		// the request, otherwise, we tend to get spurious error messages, depending on just how unloaded
		// the page is by the time the post success message arrives.
		// Most of these operations also need to happen on the UI thread, so the BeginInvoke
		// serves a double purpose by achieving that and delaying the action.
		// Note that we never try to call EndInvoke. This is OK for control.BeingInvoke;
		// any unhandled exceptions will be sent to the unhandled event hander eventually.
		void ClaimSuccessThenDo(ApiRequest request, Action doSomething)
		{
			request.PostSucceeded();
			if (Program.RunningUnitTests)
			{
				doSomething();
				return;
			}

			var syncForm = Application.OpenForms.Cast<Form>().Last();
			if (syncForm != null)
			{
				syncForm.BeginInvoke(doSomething);
				return;
			}
			// strange situation...maybe we can still do it?
			doSomething();
		}

		public void RegisterWithServer(EnhancedImageServer server)
		{
			server.RegisterEndpointHandler(kApiUrlPart + "requestState", request =>
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

			server.RegisterEndpointHandler(kApiUrlPart + "addPage", request =>
			{
				ClaimSuccessThenDo(request, () => AddPageButton_Click());
			}, false);

			server.RegisterEndpointHandler(kApiUrlPart + "duplicatePage", request =>
			{
				ClaimSuccessThenDo(request, () =>_editingModel.OnDuplicatePage());
			}, false);

			server.RegisterEndpointHandler(kApiUrlPart + "deletePage", request =>
			{
				ClaimSuccessThenDo(request, () =>
				{
					if (ConfirmRemovePageDialog.Confirm())
						_editingModel.OnDeletePage();
				});
			}, false);

			server.RegisterEndpointHandler(kApiUrlPart + "lockBook", request =>
			{
				_editingModel.SaveNow(); // BL-5421 lock and unlock lose typing
				_editingModel.CurrentBook.TemporarilyUnlocked = false;
				request.PostSucceeded();
				UpdateState(); // because we aren't selecting a new page
				_editingModel.RefreshDisplayOfCurrentPage();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "unlockBook", request =>
			{
				_editingModel.SaveNow(); // BL-5421 lock and unlock lose typing
				_editingModel.CurrentBook.TemporarilyUnlocked = true;
				request.PostSucceeded();
				UpdateState(); // because we aren't selecting a new page
				_editingModel.RefreshDisplayOfCurrentPage();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "cleanup", request =>
			{
				SendCleanupState();
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "zoomMinus", request =>
			{
				_editingModel.AdjustPageZoom(-10);
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "zoomPlus", request =>
			{
				_editingModel.AdjustPageZoom(10);
				request.PostSucceeded();
			}, true);

			server.RegisterEndpointHandler(kApiUrlPart + "requestVideoPlaceHolder", request =>
			{
				_editingModel.RequestVideoPlaceHolder();
				request.PostSucceeded();
			}, true);
		}

		private void SendCleanupState()
		{
			var endState = "{\"CanAddPages\":false,\"CanDeletePage\":false,\"CanDuplicatePage\":false,\"BookLockedState\":\"OriginalBookMode\"}";
			_webSocketServer.Send(kWebsocketStateId, endState);
		}

		private void UpdateState()
		{
			_webSocketServer.Send(kWebsocketStateId, CurrentStateString);
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
