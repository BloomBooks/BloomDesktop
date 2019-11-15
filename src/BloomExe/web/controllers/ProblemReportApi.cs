using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Mail;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.web.controllers
{
	internal class ProblemReportApi : IDisposable
	{
		private readonly BookSelection _bookSelection;
		private static TempFile _screenshotTempFile;
		private BloomZipFile _bookZipFile;
		private TempFile _bookZipFileTemp;

		public ProblemReportApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
			_bookZipFileTemp = TempFile.WithFilenameInTempFolder("book.zip");
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			// ProblemDialog.tsx uses this endpoint to get the screenshot image.
			apiHandler.RegisterEndpointHandler("problemReport/screenshot",
				(ApiRequest request) =>
				{
					request.ReplyWithImage(_screenshotTempFile.Path); 
				}, true);

			// ProblemDialog.tsx uses this endpoint to get the name of the book.
			apiHandler.RegisterEndpointHandler("problemReport/bookName",
				(ApiRequest request) =>
				{
					request.ReplyWithText(_bookSelection.CurrentSelection?.TitleBestForUserDisplay);
				}, true);

			// ProblemDialog.tsx uses this endpoint to get the registered user's email address.
			apiHandler.RegisterEndpointHandler("problemReport/emailAddress",
				(ApiRequest request) =>
				{
					request.ReplyWithText(SIL.Windows.Forms.Registration.Registration.Default.Email);
				}, true);

			// PrivacyScreen.tsx uses this endpoint to show the user what info will be included in the report.
			apiHandler.RegisterEndpointHandler("problemReport/diagnosticInfo",
				(ApiRequest request) =>
				{
					var userWantsToIncludeBook = request.RequiredParam("includeBook") == "true";
					var userInput = request.RequiredParam("userInput");
					var userEmail = request.RequiredParam("email");
					request.ReplyWithText(GetDiagnosticInfo(userWantsToIncludeBook, userInput, userEmail));
				}, true);

			// ProblemDialog.tsx uses this endpoint in its AttemptSubmit method; it expects an AxiosResponse, so it
			// knows it succeeded.
			apiHandler.RegisterEndpointHandler("problemReport/submit",
				(ApiRequest request) =>
				{

					// Object sent:
					// {
					//	 kind: props.kind,
					//	 email,
					//	 userInput: `How much: TODO < br />${ userInput}`,
					//	 includeBook,
					//	 includeScreenshot
					// }

					var report = DynamicJson.Parse(request.RequiredPostJson());
					var subject = report.kind == "User" ? "User Problem" : report.kind == "Fatal" ? "Crash Report" : "Error Report";

					var issueSubmission = new YouTrackIssueSubmitter("BL");
					//var issueSubmission = new YouTrackIssueSubmitter("BL");
					var userDesc = report.userInput as string;
					var userEmail = report.email as string;
					if (report.includeScreenshot && _screenshotTempFile != null && RobustFile.Exists(_screenshotTempFile.Path))
					{
						issueSubmission.AddAttachment(_screenshotTempFile?.Path);
					}
					if(report.includeBook)
					{
							try
							{
								_bookZipFile = new BloomZipFile(_bookZipFileTemp.Path);
								_bookZipFile.AddDirectory(_bookSelection.CurrentSelection.StoragePageFolder);
							//if (WantReaderInfo())
							//	AddReaderInfo(zip);
							//AddCollectionSettings(zip);
								_bookZipFile.Save();
							}
							catch (Exception error)
							{
								var msg = "***Error as ProblemReportApi attempted to zip up the book: " + error.Message;
								userDesc += Environment.NewLine + msg;
								Logger.WriteEvent(msg);
								// if an error happens in the zipper, the zip file stays locked, so we just leak it
								_bookZipFileTemp.Detach();
							}
							issueSubmission.AddAttachment(_bookZipFileTemp.Path);
					}
					var diagnosticInfo = GetDiagnosticInfo(report.includeBook, userDesc, userEmail);
					if (!string.IsNullOrWhiteSpace(userEmail))
					{
						// remember their email
						SIL.Windows.Forms.Registration.Registration.Default.Email = userEmail;
					}
					var issueId = issueSubmission.SubmitToYouTrack(subject, diagnosticInfo);
					request.ReplyWithJson(new{issueLink= "https://issues.bloomlibrary.org/youtrack/issue/" + issueId });
				}, true);
		}

		public static void ShowProblemDialog(Control controlForScreenshotting, string levelOfProblem="user")
		{
			SafeInvoke.InvokeIfPossible("Screen Shot", controlForScreenshotting, false,
				() =>
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

						_screenshotTempFile = TempFile.WithFilename("screenshot.png");
						RobustImageIO.SaveImage(screenshot, _screenshotTempFile.Path, ImageFormat.Png);
					}
					catch (Exception e)
					{
						_screenshotTempFile = null;
						Logger.WriteError("Bloom was unable to create a screenshot.", e);
					}

					var query = "?" + levelOfProblem;
					var problemDialogRootPath = BloomFileLocator.GetBrowserFile(false,  "problemDialog", "loader.html");
					var url = problemDialogRootPath.ToLocalhost() + query;
					using (var dlg = new BrowserDialog(url))
					{
						dlg.ShowDialog();
					}
				});
		}

		private string GetDiagnosticInfo(bool includeBook, string userDescription, string userEmail)
		{
			var bldr = new StringBuilder();

			bldr.AppendLine("=Problem Description=");
			bldr.AppendLine(userDescription);
			bldr.AppendLine();

			GetInformationAboutUser(bldr, userEmail);
			GetStandardErrorReportingProperties(bldr, true);
			GetAdditionalBloomEnvironmentInfo(bldr);
			GetAdditionalFileInfo(bldr, includeBook);
			return bldr.ToString();
		}

		private static string GetObfuscatedEmail(string userEmail = "")
		{
			var email = string.IsNullOrWhiteSpace(userEmail) ?
				SIL.Windows.Forms.Registration.Registration.Default.Email :
				userEmail;
			string obfuscatedEmail;
			try
			{
				var m = new MailAddress(email);
				// note: we have code in YouTrack that de-obfuscates this particular format, so don't mess with it
				obfuscatedEmail = string.Format("{1} {0}", m.User, m.Host).Replace(".", "/");
			}
			catch (Exception)
			{
				obfuscatedEmail = email; // ah well, it's not valid anyhow, so no need to obfuscate (other code may not let the user get this far anyhow)
			}
			return obfuscatedEmail;
		}

		private static void GetInformationAboutUser(StringBuilder bldr, string userEmail)
		{
			var firstName = SIL.Windows.Forms.Registration.Registration.Default.FirstName;
			var lastName = SIL.Windows.Forms.Registration.Registration.Default.Surname;
			bldr.AppendLine("Error Report from " + lastName + ", " + firstName + " (" + GetObfuscatedEmail(userEmail) + ") on " + DateTime.UtcNow.ToUniversalTime());
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
				bldr.AppendLine("```stacktrace");
				try
				{
					bldr.Append(Logger.LogText);
				}
				catch (Exception err)
				{
					// We have more than one report of dying while logging an exception.
					bldr.AppendLine("****Could not read from log: " + err.Message);
				}
				bldr.AppendLine("```");
			}
		}

		private void GetAdditionalBloomEnvironmentInfo(StringBuilder bldr)
		{
			var book = _bookSelection.CurrentSelection;
			var projectName = book?.CollectionSettings.CollectionName;
			bldr.AppendLine("=Additional User Environment Information=");
			if (book == null)
			{
				bldr.AppendLine("No Book was selected.");
				return;
			}
			try
			{
				bldr.AppendLine("Collection name: " + projectName);
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
							settings.Language1.FontName + (settings.Language1.IsRightToLeft ? " RTL" : string.Empty));
			bldr.AppendLine("Language2 iso: " + settings.Language2Iso639Code + " font: " +
							settings.Language2.FontName + (settings.Language2.IsRightToLeft ? " RTL" : string.Empty));
			bldr.AppendLine("Language3 iso: " + settings.Language3Iso639Code + " font: " +
							settings.Language3.FontName + (settings.Language3.IsRightToLeft ? " RTL" : string.Empty));
		}

		private void GetAdditionalFileInfo(StringBuilder bldr, bool includeBook)
		{
			var book = _bookSelection.CurrentSelection;
			if (string.IsNullOrEmpty(book?.FolderPath))
				return;
			bldr.AppendLine();
			bldr.AppendLine("=Additional Files Bundled With Book=");
			var collectionFolder = Path.GetDirectoryName(book.FolderPath);
			if (WantReaderInfo(includeBook))
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

			if (book == null || !includeBook)
				return false;
			foreach (var tool in book.BookInfo.Tools)
			{
				if (tool.ToolId == "decodableReader" || tool.ToolId == "leveledReader")
					return true;
			}
			return false;
		}

		private static void ListFolderContents(string folder, StringBuilder bldr)
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
			_screenshotTempFile?.Dispose();
			_bookZipFile = null;
			_bookZipFileTemp?.Dispose();
		}
	}
}
