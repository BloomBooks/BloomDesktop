using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.WebLibraryIntegration;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;

namespace Bloom.web.controllers
{
	// ProblemReportApi should be written so that none of the API calls require sync (from BloomApiHandler).
	// The reason is because the ProblemReportApi could be spawned from an API call that requires sync.
	// If the parent API call required sync and the child ProblemReportApi call also required sync, the threads would deadlock
	// until the ProblemReportDialog is closed. (All of these requests to get the strings to populate into the dialog like bookname
	// or diagnosticInfo wouldn't return and the dialog will display their default values like "??" or "Loading..."
	//
	// This class should ideally save all of the values required by the API handlers at the time ShowProblemReport() is called.
	// (That's what the ReportInfo class is here for)
	// This will help with not requiring locking (you only need to worry about whether this class interferes with itself and can ignore the rest of Bloom)
	// It also locks the reported values to their value at the time ShowProblemDialog() was invoked, which is also a nice-to-have.
	internal class ProblemReportApi : IDisposable
	{
		private class ReportInfo
		{
			#region Readonly Properties
			// These fields are readonly to provide assurance that they haven't been changed after the ReportInfo was constructed.
			// (This makes proper threading/locking/synchronization easier to reason about)
			public string HeadingHtml { get; }	// What shows at the top of the dialog to indicate the nature of the problem.

			public string DetailedMessage { get; }	// usually from Bloom itself
			public Exception Exception { get; }
			public Bloom.Book.Book Book { get; }
			public string BookName { get; }
			public string UserEmail { get; }
			public string UserFirstName { get; }
			public string UserSurname { get; }
			#endregion

			// We make a special allowance for ScreenshotTempFile since it uses a more complicated SafeInvoke to be assigned.
			public TempFile ScreenshotTempFile { get; set; }

			public ReportInfo(string heading, string detailMessage, Exception exception, Bloom.Book.Book book,
				string bookName, string email, string firstName, string surname, bool isHeadingPreEncoded = false)
			{
				if (isHeadingPreEncoded)
				{
					HeadingHtml = heading;
				}
				else
				{
					HeadingHtml = UrlPathString.CreateFromUnencodedString(heading).HtmlXmlEncoded;
				}

				this.DetailedMessage = detailMessage;
				this.Exception = exception;
				this.Book = book;
				this.BookName = bookName;
				this.UserEmail = email;
				this.UserFirstName = firstName;
				this.UserSurname = surname;
			}
		}

		// Assumption: Assumes that only assigned to by ShowProblemReport(), and that only one problem report happens at a time.
		// (The ShowProblemReport() thread should check and set _showingProblemReport before modifying _reportInfo,
		//  thus basically establishing locked access to _reportInfo)
		private static ReportInfo _reportInfo = new ReportInfo("", "", null, null, "", "", "", "");

		private static BookSelection _bookSelection;

		private BloomZipFile _bookZipFile;
		private TempFile _bookZipFileTemp;
		protected string YouTrackProjectKey = "BL";

		/// <summary>
		/// We want this name "different" enough that it's not likely to be supplied by a user in a book,
		/// since we turn off caching for this file in web/RequestInfo.cs to avoid stale screenshots.
		/// </summary>
		internal const string ScreenshotName = "ProblemReportScreenshot.png";

		private string CollectionFolder => Path.GetDirectoryName(_bookSelection.CurrentSelection.StoragePageFolder);

		public ProblemReportApi(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{			
			// For the paranoid - We could also have showProblemReport block these handlers while _reportInfo is being populated.
			// I think it's unnecessary since the problem report dialog's URL isn't even set until after the _reportInfo is populated,
			// and we assume that nothing sends problemReport API requests except the problemReportDialog that this class loads.

			// ProblemDialog.tsx uses this endpoint to get the string to show at the top of the main dialog
			apiHandler.RegisterEndpointHandlerUsedByOthers("problemReport/reportHeadingHtml",
				(ApiRequest request) =>
				{
					request.ReplyWithText(_reportInfo.HeadingHtml ?? "");
				}, false);
			// ProblemDialog.tsx uses this endpoint to get the screenshot image.
			apiHandler.RegisterEndpointHandlerUsedByOthers("problemReport/screenshot",
				(ApiRequest request) =>
				{
					// Wait until the screenshot is finished.
					// If not available within the time limit, just continue anyway (so we don't deadlock or anything) and hope for the best.
					bool isLockTaken = _takingScreenshotLock.Wait(millisecondsTimeout: 5000);

					if (_reportInfo?.ScreenshotTempFile == null)
						request.Failed();
					else
						request.ReplyWithImage(_reportInfo.ScreenshotTempFile.Path);

					if (isLockTaken)
						_takingScreenshotLock.Release();
				}, true);

			// ProblemDialog.tsx uses this endpoint to get the name of the book.
			apiHandler.RegisterEndpointHandlerUsedByOthers("problemReport/bookName",
				(ApiRequest request) =>
				{
					request.ReplyWithText(_reportInfo.BookName ?? "??");
				}, true);

			// ProblemDialog.tsx uses this endpoint to get the registered user's email address.
			apiHandler.RegisterEndpointHandlerUsedByOthers("problemReport/emailAddress",
				(ApiRequest request) =>
				{
					request.ReplyWithText(_reportInfo.UserEmail);
				}, true);

			// PrivacyScreen.tsx uses this endpoint to show the user what info will be included in the report.
			apiHandler.RegisterEndpointHandlerUsedByOthers("problemReport/diagnosticInfo",
				(ApiRequest request) =>
				{
					var userWantsToIncludeBook = request.RequiredParam("includeBook") == "true";
					var userInput = request.RequiredParam("userInput");
					var userEmail = request.RequiredParam("email");
					request.ReplyWithText(GetDiagnosticInfo(userWantsToIncludeBook, userInput, userEmail));
				}, true);

			// ProblemDialog.tsx uses this endpoint in its AttemptSubmit method;
			// it expects a response that it will use to show the issue link to the user.
			apiHandler.RegisterEndpointHandlerUsedByOthers("problemReport/submit",
				(ApiRequest request) =>
				{
					var report = DynamicJson.Parse(request.RequiredPostJson());
					var subject = report.kind == "User" ? "User Problem" : report.kind == "Fatal" ? "Crash Report" : "Error Report";

					var issueSubmission = new YouTrackIssueSubmitter(YouTrackProjectKey);
					var userDesc = report.userInput as string;
					var userEmail = report.email as string;
					if (report.includeScreenshot && _reportInfo?.ScreenshotTempFile != null && RobustFile.Exists(_reportInfo.ScreenshotTempFile.Path))
					{
						issueSubmission.AddAttachmentWhenWeHaveAnIssue(_reportInfo.ScreenshotTempFile.Path);
					}
					string diagnosticInfo = GetDiagnosticInfo(report.includeBook, userDesc, userEmail);
					if (!string.IsNullOrWhiteSpace(userEmail))
					{
						// remember their email
						SIL.Windows.Forms.Registration.Registration.Default.Email = userEmail;
					}

					const string failureResult = "failed";
					string issueId;
					try
					{
						issueId = issueSubmission.SubmitToYouTrack(subject, diagnosticInfo);
					}
					catch (Exception e)
					{
						Debug.Fail("Submitting problem report to YouTrack failed with '" + e.Message + "'.");
						issueId = failureResult;
					}
					object linkToNewIssue;
					if (issueId == failureResult)
					{
						var zipPath = MakeEmailableReportFile(report.includeBook, report.includeScreenshot, userDesc, diagnosticInfo);
						linkToNewIssue = new {issueLink = failureResult + ":" + zipPath};
					}
					else
					{
						linkToNewIssue = new { issueLink = "https://issues.bloomlibrary.org/youtrack/issue/" + issueId};
						if (report.includeBook)
						{
							try
							{
								string zipPath = CreateBookZipFile(issueId, userDesc);
								if (zipPath != null)
								{
									// This could be used provided the file is not too large (about 10M as of July 2020),
									// but it seems simpler to do the same thing every time.
									//issueSubmission.AttachFileToExistingIssue(issueId, zipPath);
									var uploadUrl = ProblemBookUploader.UploadBook(
										BloomS3Client.ProblemBookUploadsBucketName, zipPath,
										new NullProgress());
									diagnosticInfo += Environment.NewLine + "Problem book uploaded to " + uploadUrl;
									// We don't want to change the summary, but currently the YouTrack API requires us to set both together.
									issueSubmission.UpdateSummaryAndDescription(issueId, subject, diagnosticInfo);
								}
							}
							catch (Exception error)
							{
								Debug.WriteLine($"Attaching book to new YouTrack issue failed with '{error.Message}'.");
								var msg = "***Error as ProblemReportApi attempted to upload the zipped book: " +
								          error.Message;
								userDesc += Environment.NewLine + msg;
								Logger.WriteEvent(userDesc);
								diagnosticInfo += Environment.NewLine + "Uploading the problem book failed with exception " + error.Message;
								// We don't want to change the summary, but currently the YouTrack API requires us to set both together.
								issueSubmission.UpdateSummaryAndDescription(issueId, subject, diagnosticInfo);
							}

							finally {
								_bookZipFileTemp.Detach();
							}
						}
					}
					request.ReplyWithJson(linkToNewIssue);
				}, true);
		}

		private string CreateBookZipFile(string basename, string userDesc)
		{
			try
			{
				if (_bookZipFileTemp != null)
					_bookZipFileTemp.Dispose();	// delete any previous report's temp file
				_bookZipFileTemp = TempFile.WithFilenameInTempFolder(basename + ".zip");
				_bookZipFile = new BloomZipFile(_bookZipFileTemp.Path);
				_bookZipFile.AddDirectory(_bookSelection.CurrentSelection.StoragePageFolder);
				if (WantReaderInfo(true))
				{
					AddReaderInfo();
				}
				AddCollectionSettings();
				_bookZipFile.Save();
				return _bookZipFileTemp.Path;
			}
			catch (Exception error)
			{
				var msg = "***Error as ProblemReportApi attempted to zip up the book: " + error.Message;
				userDesc += Environment.NewLine + msg;
				Logger.WriteEvent(userDesc);
				DisposeOfZipRemnants(true);
				return null;
			}
		}

		private void DisposeOfZipRemnants(bool includeBook)
		{
			// if an error happens in the zipper, the zip file stays locked, so we just leak it
			if (includeBook)
			{
				_bookZipFileTemp.Detach();
			}

			_bookZipFile = null;
		}

		private void AddReaderInfo()
		{
			var filePaths = GetReaderFilePaths(CollectionFolder);
			foreach (var filePath in filePaths)
			{
				_bookZipFile.AddTopLevelFile(filePath);
			}
		}

		private void AddCollectionSettings()
		{
			var filePaths = GetCollectionFilePaths(CollectionFolder);
			foreach (var filePath in filePaths)
			{
				_bookZipFile.AddTopLevelFile(filePath);
			}
		}

		private string MakeEmailableReportFile(bool includeBook, bool includeScreenshot, string userDesc, string diagnosticInfo)
		{
			try
			{
				var filename = ("Report " + DateTime.UtcNow.ToString("u") + ".zip").Replace(':', '.');
				filename = filename.SanitizeFilename('#');
				var emailZipPath = Path.Combine(Path.GetTempPath(), filename);
				var emailZipper = new BloomZipFile(emailZipPath);
				using (var file = TempFile.WithFilenameInTempFolder("report.txt"))
				{
					using (var stream = RobustFile.CreateText(file.Path))
					{
						stream.WriteLine(diagnosticInfo);
						if (includeBook)
						{
							stream.WriteLine();
							stream.WriteLine(
								"REMEMBER: if the attached zip file appears empty, it may have non-ascii in the file names. Open with 7zip and you should see it.");
						}
					}
					emailZipper.AddTopLevelFile(file.Path);
				}
				if (includeBook)
				{
					var bookZipPath = CreateBookZipFile("ProblemBook", userDesc);
					if (bookZipPath != null)
						emailZipper.AddTopLevelFile(bookZipPath);
				}
				if (includeScreenshot && _reportInfo?.ScreenshotTempFile != null && RobustFile.Exists(_reportInfo.ScreenshotTempFile.Path))
				{
					emailZipper.AddTopLevelFile(_reportInfo.ScreenshotTempFile.Path);
				}
				emailZipper.Save();
				return emailZipPath;
			}
			catch (Exception error)
			{
				var msg = "***Error as ProblemReportApi attempted to zip up error information to email: " + error.Message;
				userDesc += Environment.NewLine + msg;
				Logger.WriteEvent(userDesc);
				return null;
			}
		}

		/// <summary>
		/// Shows a problem dialog. Use to match <code>Action&lt;Exception, string&gt;</code> signature
		/// in ErrorReport.OnShowDetails().
		/// </summary>
		/// <param name="exception"></param>
		/// <param name="detailedMessage"></param>
		public static void ShowProblemDialogForNonFatalException(Exception exception,
			string detailedMessage = "")
		{
			ShowProblemDialog(null, exception, detailedMessage, "nonfatal");
		}

		static bool _showingProblemReport;
		// Extra locking object because 1) you can't lock primitives directly, and 2) you shouldn't use the object whose value you'll be reading as the lock object (reads to the object are not blocked)
		static object _showingProblemReportLock = new object();	

		/// <summary>
		/// Shows a problem dialog.
		/// </summary>
		/// <param name="controlForScreenshotting"></param>
		/// <param name="exception"></param>
		/// <param name="detailedMessage"></param>
		/// <param name="levelOfProblem"></param>
		public static void ShowProblemDialog(Control controlForScreenshotting, Exception exception,
			string detailedMessage = "", string levelOfProblem = "user", string shortUserLevelMessage = "", bool isShortMessagePreEncoded = false)
		{
			// Before we do anything that might be "risky", put the problem in the log.
			LogProblem(exception, detailedMessage, levelOfProblem);
			Program.CloseSplashScreen(); // if it's still up, it'll be on top of the dialog

			lock (_showingProblemReportLock)
			{
				if (_showingProblemReport)
				{
					// If a problem is reported when already reporting a problem, that could
					// be an unbounded recursion that freezes the program and prevents the original
					// problem from being reported.  So minimally report the recursive problem and stop
					// the recursion in its tracks.
					//
					// Alternatively, can happen if multiple async BloomAPI calls go out and return errors.
					// It's probably not helpful to have multiple problem report dialogs at the same time
					// in this case either (even if there are theoretically a finite (not infinite) number of them)
					const string msg = "MULTIPLE CALLS to ShowProblemDialog. Suppressing the subsequent calls";
					Console.Write(msg);
					Logger.WriteEvent(msg);
					return; // Abort
				}

				_showingProblemReport = true;
			}

			GatherReportInfoExceptScreenshot(exception, detailedMessage, shortUserLevelMessage, isShortMessagePreEncoded);

			if (controlForScreenshotting == null)
				controlForScreenshotting = Form.ActiveForm;
			if (controlForScreenshotting == null) // still possible if we come from a "Details" button
				controlForScreenshotting = FatalExceptionHandler.ControlOnUIThread;
			ResetScreenshotFile();
			// Originally, we used SafeInvoke for both the screenshot and the new dialog display. SafeInvoke was great
			// for trying to get a screenshot, but having the actual dialog inside
			// of it was causing problems for handling any errors in showing the dialog.
			// Now we use SafeInvoke only inside of this extracted method.
			TryGetScreenshot(controlForScreenshotting);

			SafeInvoke.InvokeIfPossible("Show Problem Dialog", controlForScreenshotting, false, () =>
			{
				// Uses a browser dialog to show the problem report
				try
				{
					var query = "?" + levelOfProblem;
					var problemDialogRootPath = BloomFileLocator.GetBrowserFile(false, "problemDialog", "loader.html");
					var url = problemDialogRootPath.ToLocalhost() + query;

					// Precondition: we must be on the UI thread for Gecko to work.
					using (var dlg = new BrowserDialog(url))
					{
						// The default height is not quite enough to show the contents without scrolling.
						dlg.Height += 30;

						// ShowDialog will cause this thread to be blocked (because it spins up a modal) until the dialog is closed.
						BloomServer._theOneInstance.RegisterThreadBlocking();
						try
						{
							dlg.ShowDialog();
						}
						finally
						{
							BloomServer._theOneInstance.RegisterThreadUnblocked();
						}
					}
				}
				catch (Exception problemReportException)
				{
					Logger.WriteError("*** ProblemReportApi threw an exception trying to display", problemReportException);
					// At this point our problem reporter has failed for some reason, so we want the old WinForms handler
					// to report both the original error for which we tried to open our dialog and this new one where
					// the dialog itself failed.
					// In order to do that, we create a new exception with the original exception (if there was one) as the
					// inner exception. We include the message of the exception we just caught. Then we call the
					// old WinForms fatal exception report directly.
					// In any case, both of the errors will be logged by now.
					var message = "Bloom's error reporting failed: " + problemReportException.Message;
					ErrorReport.ReportFatalException(new ApplicationException(message, _reportInfo.Exception ?? problemReportException));
				}
				finally
				{
					lock (_showingProblemReportLock)
					{
						_showingProblemReport = false;
					}
				}
			});
		}

		/// <summary>
		/// Instantiates _reportInfo with the relevant info from the reported problem.
		/// However, _screenshotTempFile will not be instantiated by this function,
		/// since that its more involved and involves getting the UI control / calling Invoke.
		/// </summary>
		internal static void GatherReportInfoExceptScreenshot(Exception exception, string detailedMessage, string shortUserLevelMessage, bool isShortMessagePreEncoded)
		{
			var heading = shortUserLevelMessage;
			var isHeadingPreEncoded = isShortMessagePreEncoded;

			if (string.IsNullOrEmpty(heading))
			{
				heading = detailedMessage;
				isHeadingPreEncoded = false;
			}
			if (string.IsNullOrEmpty(heading) && exception != null)
			{
				heading = exception.Message;
				isHeadingPreEncoded = false;
			}

			var book = _bookSelection.CurrentSelection;
			var bestBookName = book?.TitleBestForUserDisplay;

			var userEmail = SIL.Windows.Forms.Registration.Registration.Default.Email;
			var userFirstName = SIL.Windows.Forms.Registration.Registration.Default.FirstName;
			var userSurname = SIL.Windows.Forms.Registration.Registration.Default.Surname;

			_reportInfo = new ReportInfo(heading, detailedMessage, exception, book, bestBookName, userEmail, userFirstName, userSurname, isHeadingPreEncoded);			
		}


		// This lock uses a SemaphoreSlim instead of a Monitor, because a Monitor only works for one thread.
		// Even though the acquisition of the lock and release of the lock can be on the same thread,
		// it doesn't seem safe to assume that this is always the case. So we use a locking mechanism that works across threads.
		private static SemaphoreSlim _takingScreenshotLock = new SemaphoreSlim(1, 1);

		private static void TryGetScreenshot(Control controlForScreenshotting)
		{
			_takingScreenshotLock.Wait();	// Acquire the lock

			try
			{
				SafeInvoke.Invoke("Screen Shot", controlForScreenshotting, false, true, () =>
					{
						try
						{
							var bounds = controlForScreenshotting.Bounds;
							var screenshot = new Bitmap(bounds.Width, bounds.Height);
							using (var g = Graphics.FromImage(screenshot))
							{
								if (controlForScreenshotting.Parent == null)
									g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);	// bounds already in screen coords
								else
									g.CopyFromScreen(controlForScreenshotting.PointToScreen(new Point(bounds.Left, bounds.Top)), Point.Empty, bounds.Size);
							}

							_reportInfo.ScreenshotTempFile = TempFile.WithFilename(ScreenshotName);
							RobustImageIO.SaveImage(screenshot, _reportInfo.ScreenshotTempFile.Path, ImageFormat.Png);
						}
						catch (Exception e)
						{
							ResetScreenshotFile();
							Logger.WriteError("Bloom was unable to create a screenshot.", e);
						}
						finally
						{
							// Release lock (Unblock others)
							if (_takingScreenshotLock.CurrentCount == 0)
								_takingScreenshotLock.Release();	
						}
					}
				);
			}
			catch (Exception error)
			{
				// Release lock (Unblock others)
				if (_takingScreenshotLock.CurrentCount == 0)
					_takingScreenshotLock.Release();	

				Debug.Fail("This error would be swallowed in release version: " + error.Message);
				SIL.Reporting.Logger.WriteEvent("**** "+error.Message);
			}
		}

		private static void LogProblem(Exception exception, string detailedMessage, string levelOfProblem)
		{
			var sb = new StringBuilder();
			sb.AppendLine("*** ProblemReportApi is about to report:");
			if (exception != null)
				sb.AppendLineFormat("    exception = {0}", exception.ToString());
			if (!string.IsNullOrWhiteSpace(detailedMessage))
				sb.AppendLineFormat("   detailed message = {0}", detailedMessage);
			sb.AppendLineFormat("    level of problem = {0}", levelOfProblem);
			var msg = sb.ToString();
			Logger.WriteEvent(msg);
		}

		private static void ResetScreenshotFile()
		{
			_reportInfo.ScreenshotTempFile?.Dispose();
			_reportInfo.ScreenshotTempFile = null;
		}

		private string GetDiagnosticInfo(bool includeBook, string userDescription, string userEmail)
		{
			var bldr = new StringBuilder();

			bldr.AppendLine("### Problem Description");
			bldr.AppendLine(userDescription);
			bldr.AppendLine();

			GetInformationAboutUser(bldr, userEmail);
			GetExceptionInformation(bldr);
			GetStandardErrorReportingProperties(bldr, true);
			GetAdditionalBloomEnvironmentInfo(bldr);
			GetAdditionalFileInfo(bldr, includeBook);
			return bldr.ToString();
		}

		private static void GetExceptionInformation(StringBuilder bldr)
		{
			if (_reportInfo.Exception == null && string.IsNullOrWhiteSpace(_reportInfo.DetailedMessage))
				return;
			if (_reportInfo.Exception != null)
			{
				Exception dummy = null;
				bldr.AppendLine();
				bldr.AppendLine("#### Exception Details");
				if (!string.IsNullOrWhiteSpace(_reportInfo.DetailedMessage))
				{
					bldr.AppendLine(_reportInfo.DetailedMessage);
					bldr.AppendLine();
				}
				bldr.AppendLine("```stacktrace");
				bldr.Append(ExceptionHelper.GetHiearchicalExceptionInfo(_reportInfo.Exception, ref dummy));
				bldr.AppendLine("```");
			}
			else
			{
				// No exception, but we do have a detailed message from Bloom. This may not actually ever occur.
				bldr.AppendLine();
				bldr.AppendLine("#### Detailed message");
				bldr.AppendLine(_reportInfo.DetailedMessage);
			}
		}

		private static string GetObfuscatedEmail(string userEmail = "")
		{
			var email = string.IsNullOrWhiteSpace(userEmail) ?  _reportInfo?.UserEmail : userEmail;
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
			var firstName = _reportInfo.UserFirstName;
			var lastName = _reportInfo.UserSurname;
			var nameString = GetNameString(firstName, lastName);			
			var obfuscatedEmail = GetObfuscatedEmail(userEmail);
			var emailString = string.IsNullOrWhiteSpace(obfuscatedEmail) ? string.Empty : " (" + obfuscatedEmail + ")";
			bldr.AppendLine("Error Report from " + nameString + emailString + " on " + DateTime.UtcNow.ToUniversalTime() + " UTC");
		}

		private static object GetNameString(string firstName, string lastName)
		{
			return !string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName)
				? lastName + ", " + firstName
				: string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(firstName) ?
					"unknown" :
					(lastName + firstName).Trim();
		}


		private static void GetStandardErrorReportingProperties(StringBuilder bldr, bool appendLog)
		{
			const string Version = "Version";
			bldr.AppendLine("#### Error Reporting Properties");
			foreach (string label in ErrorReport.Properties.Keys)
			{
				if (label == Version)
					bldr.Append("**"); // Version is the most important of these; bring it out a bit
				bldr.Append(label);
				bldr.Append(": ");
				bldr.Append(ErrorReport.Properties[label]);
				bldr.AppendLine(label == Version ? "**" : "");
			}

			if (appendLog || Logger.Singleton == null)
			{
				bldr.AppendLine();
				bldr.AppendLine("#### Log");
				bldr.AppendLine("```stacktrace");
				bldr.AppendLine();
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
			var book = _reportInfo.Book;
			var projectName = book?.CollectionSettings.CollectionName;
			bldr.AppendLine("#### Additional User Environment Information");
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
			// TODO: rethink how to display language information if we expand the languages available.
			var language1 = book.BookData.Language1;
			bldr.AppendLine("Language1 -> iso: '" + language1.Iso639Code + "',  font: " +
							language1.FontName + (language1.IsRightToLeft ? " RTL" : string.Empty));
			var language2 = book.BookData.Language2;
			bldr.AppendLine("Language2 -> iso: '" + language2.Iso639Code + "',  font: " +
							language2.FontName + (language2.IsRightToLeft ? " RTL" : string.Empty));
			var language3 = book.BookData.Language3;
			if (string.IsNullOrEmpty(language3?.Iso639Code))
			{
				bldr.AppendLine("No Language3 defined");
			}
			else
			{
				bldr.AppendLine("Language3 -> iso: '" + language3.Iso639Code + "',  font: " +
					language3.FontName + (language3.IsRightToLeft ? " RTL" : string.Empty));
			}
		}

		private void GetAdditionalFileInfo(StringBuilder bldr, bool includeBook)
		{
			var book = _reportInfo.Book;
			if (string.IsNullOrEmpty(book?.FolderPath))
				return;
			bldr.AppendLine();
			bldr.AppendLine("#### Additional Files Bundled With Book");
			var collectionFolder = Path.GetDirectoryName(book.FolderPath);
			if (collectionFolder == null)
				return; // mostly to avoid blue squiggles in VS
			if (WantReaderInfo(includeBook))
			{
				var listOfReaderFiles = GetReaderFilePaths(collectionFolder);
				ListFiles(listOfReaderFiles, bldr);
			}
			var listOfCollectionFiles = GetCollectionFilePaths(collectionFolder);
			ListFiles(listOfCollectionFiles, bldr);
		}

		private static IEnumerable<string> GetReaderFilePaths(string collectionFolder)
		{
			var result = Directory.GetFiles(collectionFolder, "ReaderTools*-*.json").ToList();
			ListFolderContents(Path.Combine(collectionFolder, "Allowed Words"), result);
			ListFolderContents(Path.Combine(collectionFolder, "Sample Texts"), result);
			return result;
		}

		private static IEnumerable<string> GetCollectionFilePaths(string collectionFolder)
		{
			var result = Directory.GetFiles(collectionFolder, "*CollectionStyles.css").ToList();
			result.AddRange(Directory.GetFiles(collectionFolder, "*.bloomCollection"));
			return result;
		}

		private static void ListFiles(IEnumerable<string> filePaths, StringBuilder bldr)
		{
			foreach (var filePath in filePaths)
				bldr.AppendLine(filePath);
		}

		private bool WantReaderInfo(bool includeBook)
		{
			var book = _reportInfo.Book;

			if (book == null || !includeBook)
				return false;
			foreach (var tool in book.BookInfo.Tools)
			{
				if (tool.ToolId == "decodableReader" || tool.ToolId == "leveledReader")
					return true;
			}
			return false;
		}

		private static void ListFolderContents(string folder, List<string> listOfFilePaths)
		{
			if (!Directory.Exists(folder))
				return;
			listOfFilePaths.AddRange(Directory.GetFiles(folder));
			// Probably overkill, but if there are subfolders, they will be zipped up with the book.
			foreach (var sub in Directory.GetDirectories(folder))
				ListFolderContents(sub, listOfFilePaths);
		}

	
		public void Dispose()
		{
			_reportInfo.ScreenshotTempFile?.Dispose();
			_bookZipFile = null;
			_bookZipFileTemp?.Dispose();
		}
	}
}
