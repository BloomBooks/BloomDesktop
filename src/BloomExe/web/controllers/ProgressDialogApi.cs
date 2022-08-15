using System;
using System.IO;
using System.Net;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.Publish;
using Bloom.Utils;
using SIL.IO;

namespace Bloom.web.controllers
{
	public class ProgressDialogApi
	{
		private static Action _cancelHandler;
		public static void SetCancelHandler(Action cancelHandler)
		{
			_cancelHandler = cancelHandler;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointLegacy("progress/cancel", Cancel, false, false);
			apiHandler.RegisterEndpointHandler("progress/closed", BrowserProgressDialog.HandleProgressDialogClosed, false);
			apiHandler.RegisterEndpointHandler("progress/ready", BrowserProgressDialog.HandleProgressReady, false);
			apiHandler.RegisterEndpointHandler("progress/close", BrowserProgressDialog.HandleCloseProgressDialog, true);
		}

		private void Cancel(ApiRequest request)
		{
			// if it's null, and that causes a throw, well... that *is* an error situation
			_cancelHandler();
			request.PostSucceeded();
		}
	}
}
