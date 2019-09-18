using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.web.controllers
{
	class ProblemReportApi : IDisposable
	{
		//private readonly UserControl _controlForScreenshotting;
		private readonly BookSelection _bookSelection;
		private static TempFile _screenshotPath;

		public ProblemReportApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("problemReport/screenshot",
				(ApiRequest request) =>
				{
					request.ReplyWithImage(_screenshotPath.Path); 
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

			apiHandler.RegisterEndpointHandler("problemReport/diagnosticInfo",
				(ApiRequest request) =>
				{
					var includeBook = request.RequiredParam("includeBook") == "true";
					request.ReplyWithText(GetDiagnosticInfo());// todo includeBook));
				}, true);

			apiHandler.RegisterEndpointHandler("problemReport/submit",
				(ApiRequest request) =>
				{
					var report = DynamicJson.Parse(request.RequiredPostJson());
					var subject = report.kind == "User" ? "User Problem" : report.kind == "Fatal" ? "Crash Report" : "Error Report";

					var issueSubmission = new YouTrackIssueSubmitter("BL");
					if (report.includeScreenshot && _screenshotPath !=null && RobustFile.Exists(_screenshotPath.Path))
					{
						// Enhance: this won't have a nice name like "screenshot.png"
						issueSubmission.AddAttachment(_screenshotPath.Path);
					}
					if(report.includeBook)
					{
					//	submitter.AddAttachment(book);
					}
					if (report.email?.length > 0)
					{
						// remember their email
						SIL.Windows.Forms.Registration.Registration.Default.Email = report.email;
					}
					issueSubmission.SubmitToYouTrack(subject,  report.userInput);
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

		private string GetObfuscatedEmail()
		{
			var email = SIL.Windows.Forms.Registration.Registration.Default.Email;
			var description = GetDiagnosticInfo();
			string obfuscatedEmail;
			try
			{
				var m = new MailAddress(email);
				// note: we have code in YouTrack we de-obfuscates this particular format, so don't mess with it
				obfuscatedEmail = string.Format("{1} {0}", m.User, m.Host).Replace(".", "/");
			}
			catch (Exception)
			{
				obfuscatedEmail = email; // ah well, it's not valid anyhow, so no need to obfuscate (other code may not let the user get this far anyhow)
			}
			return obfuscatedEmail;
		}
		private string GetInformationAboutUser()
		{

			var bldr = new StringBuilder();
			bldr.AppendLine("Error Report from " + _name.Text + " (" + GetObfuscatedEmail() + ") on " + DateTime.UtcNow.ToUniversalTime());
		}
		private string GetDiagnosticInfo()
		{
			

			var bldr = new StringBuilder();

			
			bldr.AppendLine("=Problem Description=");
			bldr.AppendLine(_description.Text);
			bldr.AppendLine();

			GetStandardErrorReportingProperties(bldr, true);
			GetAdditionalEnvironmentInfo(bldr);
			GetAdditionalFileInfo(bldr, includeBook);
			return bldr.ToString();
		}

			private static void GetStandardErrorReportingProperties(StringBuilder bldr, bool appendLog)
		{
			bldr.AppendLine();
			bldr.AppendLine("=Error Reporting Properties=");
			foreach (string label in ErrorReport.Properties.Keys)
			{
				bldr.Append(label);
				bldr.Append(": ");
				bldr.AppendLine(ErrorReport.Properties[label]);
			}

			if (appendLog || Logger.Singleton == null)
			{
				bldr.AppendLine();
				bldr.AppendLine("=Log=");
				try
				{
					bldr.Append(Logger.LogText);
				}
				catch (Exception err)
				{
					//We have more than one report of dieing while logging an exception.
					bldr.AppendLine("****Could not read from log: " + err.Message);
				}
			}
		}

		private void GetAdditionalEnvironmentInfo(StringBuilder bldr)
		{
			var book = _bookSelection.CurrentSelection;
			var projectName = book?.CollectionSettings.CollectionName;
			bldr.AppendLine("=Additional User Environment Information=");
			if (book == null)
			{
				if (!string.IsNullOrEmpty(projectName))
					bldr.AppendLine("Collection name: " + projectName);
				bldr.AppendLine("No Book was selected.");
				return;
			}
			try
			{
				var sizeOrient = book.GetLayout().SizeAndOrientation;
				bldr.AppendLine("Page Size/Orientation: " + sizeOrient);
			}
			catch (Exception)
			{
				bldr.AppendLine("GetLayout() or SizeAndOrientation threw an exception.");
			}
			var settings = book.CollectionSettings;
			if (settings == null)
			{
				// paranoia, shouldn't happen
				bldr.AppendLine("Book's CollectionSettings was null.");
				return;
			}
			bldr.AppendLine("Collection name: " + settings.CollectionName);
			bldr.AppendLine("xMatter pack name: " + settings.XMatterPackName);
			bldr.AppendLine("Language1 iso: " + settings.Language1Iso639Code + " font: " +
							settings.DefaultLanguage1FontName + (settings.IsLanguage1Rtl ? " RTL" : string.Empty));
			bldr.AppendLine("Language2 iso: " + settings.Language2Iso639Code + " font: " +
							settings.DefaultLanguage2FontName + (settings.IsLanguage2Rtl ? " RTL" : string.Empty));
			bldr.AppendLine("Language3 iso: " + settings.Language3Iso639Code + " font: " +
							settings.DefaultLanguage3FontName + (settings.IsLanguage3Rtl ? " RTL" : string.Empty));
		}

		private void GetAdditionalFileInfo(StringBuilder bldr, bool includeBook)
		{
			var book = _bookSelection.CurrentSelection;
			if (book == null || String.IsNullOrEmpty(book.FolderPath))
				return;
			bldr.AppendLine();
			bldr.AppendLine("=Additional Files Bundled With Book=");
			var collectionFolder = Path.GetDirectoryName(book.FolderPath);
			if (WantReaderInfo( includeBook))
			{
				foreach (var file in Directory.GetFiles(collectionFolder, "ReaderTools*-*.json"))
					bldr.AppendLine(file);
				ListFolderContents(Path.Combine(collectionFolder, "Allowed Words"), bldr);
				ListFolderContents(Path.Combine(collectionFolder, "Sample Texts"), bldr);
			}
			foreach (var file in Directory.GetFiles(collectionFolder, "*CollectionStyles.css"))
				bldr.AppendLine(file);
			foreach (var file in Directory.GetFiles(collectionFolder, "*.bloomCollection"))
				bldr.AppendLine(file);
		}

		private bool WantReaderInfo(bool includeBook)
		{
			var book = _bookSelection.CurrentSelection;

			if (book==null || !includeBook)
				return false;
			foreach (var tool in book.BookInfo.Tools)
			{
				if (tool.ToolId == "decodableReader" || tool.ToolId == "leveledReader")
					return true;
			}
			return false;
		}

		private void ListFolderContents(string folder, StringBuilder bldr)
		{
			if (!Directory.Exists(folder))
				return;
			foreach (var file in Directory.GetFiles(folder))
				bldr.AppendLine(file);
			// Probably overkill, but if there are subfolders, they will be zipped up with the book.
			foreach (var sub in Directory.GetDirectories(folder))
				ListFolderContents(sub, bldr);
		}

	
		public void Dispose()
		{
			_screenshotPath?.Dispose();
		}
	}
}
