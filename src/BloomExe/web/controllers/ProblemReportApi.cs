using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.Workspace;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.web.controllers
{
	class ProblemReportApi : IDisposable
	{
		//private readonly UserControl _controlForScreenshotting;
		private readonly BookSelection _bookSelection;
		private static TempFile _screenshotPath;

		public ProblemReportApi(BookSelection bookSelection)//WorkspaceView workspaceView)
		{
			_bookSelection = bookSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("problemReport/screenshot",
				(ApiRequest request) =>
				{
					request.ReplyWithImage(_screenshotPath.Path); //"images/madBloomScientist.svg");
				}, true);

			apiHandler.RegisterEndpointHandler("problemReport/bookName",
				(ApiRequest request) =>
				{
					request.ReplyWithText(_bookSelection.CurrentSelection?.TitleBestForUserDisplay);
				}, true);

			apiHandler.RegisterEndpointHandler("problemReport/emailAddress",
				(ApiRequest request) =>
				{
					request.ReplyWithText(SIL.Windows.Forms.Registration.Registration.Default.Email);
				}, true);

			apiHandler.RegisterEndpointHandler("problemReport/log",
				(ApiRequest request) =>
				{
					request.ReplyWithText(Logger.LogText);
				}, true);

			apiHandler.RegisterEndpointHandler("problemReport/submit",
				(ApiRequest request) =>
				{
					//MessageBox.Show(request.RequiredPostJson()); 
					Thread.Sleep(3000);
					request.ReplyWithJson(new{issueLink="https://google.com"});
				}, true);
		}
		public static void ShowProblemDialog(Control controlForScreenshotting)
		{
			SafeInvoke.InvokeIfPossible("Screen Shot", controlForScreenshotting, false,
				(Action) (() =>
				{
					try
					{
						var bounds = controlForScreenshotting.Bounds;
						var screenshot = new Bitmap(bounds.Width, bounds.Height);
						using (var g = Graphics.FromImage(screenshot))
						{
							g.CopyFromScreen(controlForScreenshotting.PointToScreen(new Point(bounds.Left, bounds.Top)), Point.Empty,
								bounds.Size);
						}

						_screenshotPath = TempFile.WithExtension(".png");
						RobustImageIO.SaveImage(screenshot, _screenshotPath.Path, ImageFormat.Png);
					}
					catch (Exception e)
					{
						_screenshotPath = null;
						Logger.WriteError("Bloom was unable to create a screenshot.", e);
					}

					var rootFile = BloomFileLocator.GetBrowserFile(false,  "problemDialog", "loader.html");
					using (var dlg = new BrowserDialog(rootFile.ToLocalhost()))
					{
						dlg.ShowDialog();
					}
				}));
		}

		public void Dispose()
		{
			_screenshotPath?.Dispose();
		}
	}
}
