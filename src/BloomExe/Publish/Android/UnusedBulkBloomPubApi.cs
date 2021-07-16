using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.ImageProcessing;
using Bloom.Properties;
using Bloom.Publish.Android.file;
using SIL.Windows.Forms.Miscellaneous;

#if !__MonoCS__
using Bloom.Publish.Android.usb;
#endif
using Bloom.Publish.Android.wifi;
using Bloom.web;
using BloomTemp;
using DesktopAnalytics;
using SIL.IO;
using Newtonsoft.Json;
using SIL.Xml;

namespace Bloom.Publish.Android
{
	/// <summary>
	/// Handles api request dealing with the publishing of books to an Android device
	/// </summary>
	public class UnusedBulkBloomPubApi
	{
		private const string kApiUrlPart = "publish/bulkBloomPub/";
		private readonly BloomWebSocketServer _webSocketServer;
		private readonly BookServer _bookServer;
		private readonly WebSocketProgress _progress;
		private const string kWebSocketContext = "publish-android"; // must match what is in AndroidPublishUI.tsx

		public UnusedBulkBloomPubApi(BloomWebSocketServer bloomWebSocketServer, BookServer bookServer, RuntimeImageProcessor imageProcessor)
		{
			_webSocketServer = bloomWebSocketServer;
			_bookServer = bookServer;
			_progress = new WebSocketProgress(_webSocketServer, kWebSocketContext);
		}



		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler(kApiUrlPart + "go", request =>
			{

			}, true);
		}

	}
}
