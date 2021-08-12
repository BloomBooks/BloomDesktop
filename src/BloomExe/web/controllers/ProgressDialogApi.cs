using System;
using System.IO;
using System.Net;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
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
			apiHandler.RegisterEndpointHandler("progress/cancel", Cancel, false, false);
		}

		private void Cancel(ApiRequest request)
		{
			// if it's null, and that causes a throw, well... that *is* an error situation
			_cancelHandler();
			request.PostSucceeded();
		}
	}
}
