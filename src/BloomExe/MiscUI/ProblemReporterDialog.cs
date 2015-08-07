using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Bloom.Book;
using L10NSharp;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Reporting;
#if !__MonoCS__
// This isn't fully satisfactory, as we need this functionality in Linux/Mono,
// but Bloom doesn't even run with this code in it on Linux/Mono.
using YouTrackSharp.Infrastructure;
using YouTrackSharp.Issues;
#endif

namespace Bloom.MiscUI
{
	/// <summary>
	/// This dialog lets users bring up an issue with us.
	/// It can include a description, a screenshot, and the file they were working on.
	/// It can try to send directly via internet. If this fails, it can make a single
	/// zip file and direct the user to email that to us.
	/// </summary>
	public partial class ProblemReporterDialog : Form
	{
		public delegate ProblemReporterDialog Factory(Control targetOfScreenshot);//autofac uses this

		protected enum State { WaitingForSubmission, ZippingUpBook, Submitting, CouldNotAutomaticallySubmit, Success }

		private readonly BookSelection _bookSelection;
		private Bitmap _screenshot;
		protected State _state;
		private string _emailableReportFilePath;
		private readonly string YouTrackUrl;
		protected string _youTrackProjectKey = "BL";
#if !__MonoCS__
		Connection _youTrackConnection = new Connection("issues.bloomlibrary.org", 80, false, "youtrack");
		IssueManagement _issueManagement;
#endif
		private string _youTrackIssueId = "unknown";
		private dynamic _youTrackIssue;

		public ProblemReporterDialog(Control targetOfScreenshot, BookSelection bookSelection)
		{
			// Haven't tried https here, as we're not using it for live YouTrack. Someday we may want to.
			// If so, check Linux, as we had problems there with the old Jira reporting. Until
			// then we use the unsecured URL.
			YouTrackUrl = "http://issues.bloomlibrary.org";
			Summary = "User Problem Report {0}";
            _bookSelection = bookSelection;

			InitializeComponent();

			// The GeckoFx-based _status control refuses to display the "Submitting to server..." message
			// on Linux, although it displays just fine on Windows.  Even moving the actual process of
			// submitting the information to another thread doesn't help -- the message still doesn't
			// display.  Substituting a normal Label control for that particular messages works just fine.
			// See https://jira.sil.org/browse/BL-1004 for details.
			_submitMsg.Text = LocalizationManager.GetString ("ReportProblemDialog.Submitting", "Submitting to server...",
				"This is shown while Bloom is sending the problem report to our server.");

			if (targetOfScreenshot != null)
			{
				//important to do this early, before this dialog obstructs the application
				GetScreenshot(targetOfScreenshot);
				_includeScreenshot.Checked = _screenshot != null; // if for some reason we couldn't get a screenshot, this will be null
				_includeScreenshot.Visible = _screenshot != null;
			}
			else
			{
				_includeScreenshot.Visible = false;
				_includeScreenshot.Checked = false;
			}

			_email.Text = Palaso.UI.WindowsForms.Registration.Registration.Default.Email;
			_name.Text = (Palaso.UI.WindowsForms.Registration.Registration.Default.FirstName + " " +
						 Palaso.UI.WindowsForms.Registration.Registration.Default.Surname).Trim();

			_screenshotHolder.Image = _screenshot;

			if (bookSelection != null && bookSelection.CurrentSelection != null)
			{
				_includeBook.Checked = false;
				_includeBook.Text = String.Format(_includeBook.Text, bookSelection.CurrentSelection.TitleBestForUserDisplay);
				const int maxIncludeBookLabelLength = 40;
				if (_includeBook.Text.Length > maxIncludeBookLabelLength)
				{
					_includeBook.Text = _includeBook.Text.Substring(0, maxIncludeBookLabelLength);
				}
			}
			else
			{
				_includeBook.Visible = false;
			}
			ChangeState(State.WaitingForSubmission);
		}

		public string Description
		{
			get { return _description.Text; }
			set { _description.Text = value; }
		}

		private void GetScreenshot(Control targetOfScreenshot)
		{
			try
			{
				var bounds = targetOfScreenshot.Bounds;
				_screenshot = new Bitmap(bounds.Width, bounds.Height);
				using (var g = Graphics.FromImage(_screenshot))
				{
					g.CopyFromScreen(targetOfScreenshot.PointToScreen(new Point(bounds.Left, bounds.Top)), Point.Empty, bounds.Size);
				}
			}
			catch (Exception e)
			{
				_screenshot = null;
				ErrorReport.NotifyUserOfProblem(e, "Bloom was unable to create a screenshot.");
			}
		}

		private void ProblemReporterDialog_Load(object sender, EventArgs e)
		{
			UpdateDisplay();
			Application.Idle += Startup;
		}

		private void Startup(object sender, EventArgs e)
		{
			Application.Idle -= Startup;
			//had trouble getting the cursor to start in this field, hence this Idle-time business
			if (_name.Text.Length > 0)
			{
				_description.Focus();
			}
		}

		protected virtual void ChangeState(State state)
		{
			_state = state;
			UpdateDisplay();
			Application.DoEvents();// make the state change show up.
		}

		private void UpdateDisplay(object sender, EventArgs e)
		{
			UpdateDisplay();
#if __MonoCS__
			// For some fonts that don't render properly in Mono BL-822
			Refresh();
#endif
		}

		public bool IsLegalEmail(string emailAddress)
		{
				//from http://stackoverflow.com/a/6893571/723299
				return Regex.IsMatch(emailAddress, @"^[\w!#$%&'*+\-/=?\^_`{|}~]+(\.[\w!#$%&'*+\-/=?\^_`{|}~]+)*"
					+ "@"
					+ @"((([\-\w]+\.)+[a-zA-Z]{2,4})|(([0-9]{1,3}\.){3}[0-9]{1,3}))$"); 
		}

		protected virtual void UpdateDisplay()
		{
			if(!string.IsNullOrWhiteSpace(_email.Text.Trim()))
			{
				_email.ForeColor = IsLegalEmail(_email.Text) ? Color.Black : Color.Red;
			}

			_submitButton.Enabled = !string.IsNullOrWhiteSpace(_name.Text.Trim()) && !string.IsNullOrWhiteSpace(_email.Text.Trim()) && IsLegalEmail(_email.Text) &&
								   !string.IsNullOrWhiteSpace(_description.Text.Trim());

			_screenshotHolder.Visible = _includeScreenshot.Checked;

			switch (_state)
			{
				case State.WaitingForSubmission:
					_status.Visible = false;
					_submitMsg.Visible = false;
					_seeDetails.Visible = true;
					Cursor = Cursors.Default;
					break;

				case State.ZippingUpBook:
					_seeDetails.Visible = false;
					_submitMsg.Visible = false;
					_status.Visible = true;
					_status.HTML = LocalizationManager.GetString("ReportProblemDialog.Zipping", "Zipping up book...",
						"This is shown while Bloom is creating the problem report. It's generally too fast to see, unless you include a large book.");
					_submitButton.Enabled = false;
					Cursor = Cursors.WaitCursor;
					break;

				case State.Submitting:
					_seeDetails.Visible = false;
					_status.Visible = false;
					_submitMsg.Visible = true;
					_submitButton.Enabled = false;
					Cursor = Cursors.WaitCursor;
					break;

				case State.CouldNotAutomaticallySubmit:
					_seeDetails.Visible = false;
					_submitMsg.Visible = false;
					_status.Visible = true;
					var message = LocalizationManager.GetString("ReportProblemDialog.CouldNotSendToServer",
						"Bloom was not able to submit your report directly to our server. Please retry or email {0} to {1}.");
					_status.HTML = string.Format("<span style='color:red'>" + message + "</span>", "<a href='file://" + _emailableReportFilePath + "'>" + Path.GetFileName(_emailableReportFilePath) + "</a>", "<a href='mailto:issues@bloomlibrary.org?subject=Problem Report'>issues@bloomlibrary.org</a>");

					_submitButton.Text = LocalizationManager.GetString("ReportProblemDialog.Retry", "Retry",
						"Shown if there was an error submitting the report. Lets the user try submitting it again.");
					Cursor = Cursors.Default;
					break;

				case State.Success:
					_seeDetails.Visible = false;
					_submitMsg.Visible = false;
					_status.Visible = true;
					_submitButton.Enabled = true;
					_submitButton.Text = LocalizationManager.GetString("ReportProblemDialog.Close", "Close", "Shown in the button that closes the dialog after a successful report submission.");
					message = LocalizationManager.GetString("ReportProblemDialog.Success",
						"We received your report, thanks for taking the time to help make Bloom better!");
					this.AcceptButton = _submitButton;
					_submitButton.Focus();
					_status.HTML = string.Format("<span style='color:blue'>" + message + "</span><br/><a href='{0}'>{1}</a>", YouTrackUrl + "/youtrack/issue/" + _youTrackIssueId, _youTrackIssueId);

					Cursor = Cursors.Default;
					break;

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		protected void _okButton_Click(object sender, EventArgs e)
		{
			if (_state == State.Success)
			{
				Close();
				return;
			}

			if (SubmitToYouTrack())
			{
				ChangeState(State.Success);
			}
			else
			{
				MakePackageForUserToEmail();
				ChangeState(State.CouldNotAutomaticallySubmit);
			}
		}

		private void MakePackageForUserToEmail()
		{
			if (string.IsNullOrWhiteSpace(_emailableReportFilePath) || !File.Exists(_emailableReportFilePath))
			{
				MakeEmailableReportFile();
			}
		}

#if !__MonoCS__
		private void AddAttachment(string file)
		{
			_issueManagement.AttachFileToIssue(_youTrackIssueId, file);
		}
#endif

		/// <summary>
		/// Using YouTrackSharp here. We can't submit
		/// the report as if it were from this person, even if they have an account (well, not without
		/// asking them for credentials, which is just not gonna happen). So we submit with an
		/// account we created just for this purpose, "auto_report_creator".
		/// </summary>
		private bool SubmitToYouTrack()
		{
#if __MonoCS__
			return false;
#else
			try
			{
				ChangeState(State.Submitting);



				_youTrackConnection.Authenticate("auto_report_creator", "thisIsInOpenSourceCode");
				_issueManagement = new IssueManagement(_youTrackConnection);
				_youTrackIssue = new Issue();
				_youTrackIssue.ProjectShortName = _youTrackProjectKey;
				_youTrackIssue.Type = "Awaiting Classification";
				_youTrackIssue.Summary = string.Format(Summary,_name.Text);
				_youTrackIssue.Description = GetFullDescriptionContents(false);
				_youTrackIssueId = _issueManagement.CreateIssue(_youTrackIssue);

				// this could all be done in one go, but I'm doing it in stages so as to increase the
				// chance of success in bad internet situations
				if (_includeScreenshot.Checked)
				{
					using (var file = TempFile.WithFilenameInTempFolder("screenshot.png"))
					{
						_screenshot.Save(file.Path, ImageFormat.Png);
						AddAttachment(file.Path);
					}
				}

				if (_includeBook.Checked)
				{
					ChangeState(State.ZippingUpBook);
					using (var bookZip = TempFile.WithExtension(".zip"))
					{
						var zip = new BloomZipFile(bookZip.Path);
						zip.AddDirectory(_bookSelection.CurrentSelection.FolderPath);
						zip.Save();
						AddAttachment(bookZip.Path);
					}
				}

				if (Logger.Singleton != null)
				{
					try
					{
						using (var logFile = GetLogFile())
						{
							AddAttachment(logFile.Path);
						}
					}
					catch (Exception e)
					{
						_issueManagement.UpdateIssue(_youTrackIssueId, _youTrackIssue.Summary, _youTrackIssue.Description + "Got exception trying to attach log file: " + e.Message);
					}
				}

				ChangeState(State.Success);
				return true;
			}
			catch (Exception error)
			{
				Debug.Fail(error.Message);
				return false;
			}
#endif
		}

		/// <summary>
		/// Will become the summary of the issue. Include {0} for the user name
		/// </summary>
		public string Summary { get; set; }

		/// <summary>
		/// If we are able to directly submit to YouTrack, we do that. But otherwise,
		/// this makes a zip file of everything we want to submit, in order to
		/// give the user a single thing they need to attach and send.
		/// </summary>
		private void MakeEmailableReportFile()
		{
			var filename = ("Report " + DateTime.UtcNow.ToString("u") + ".zip").Replace(':', '.');
			filename = filename.SanitizeFilename('#');
			var zipFile = TempFile.WithFilename(filename);
			_emailableReportFilePath = zipFile.Path;

			var zip = new BloomZipFile(_emailableReportFilePath);

			using (var file = TempFile.WithFilenameInTempFolder("report.txt"))
			{
				using (var stream = File.CreateText(file.Path))
				{
					stream.WriteLine(GetFullDescriptionContents(false));

					if (_includeBook.Checked)
					{
						stream.WriteLine();
						stream.WriteLine(
							"REMEMBER: if the attached zip file appears empty, it may have non-ascii in the file names. Open with 7zip and you should see it.");
					}
				}
				zip.AddTopLevelFile(file.Path);

				if (_includeBook.Checked)
				{
					zip.AddDirectory(_bookSelection.CurrentSelection.FolderPath);
				}
			}
			if (_includeScreenshot.Checked)
			{
				using (var file = TempFile.WithFilenameInTempFolder("screenshot.png"))
				{
					_screenshot.Save(file.Path, ImageFormat.Png);
					zip.AddTopLevelFile(file.Path);
				}
			}
			if (Logger.Singleton != null)
			{
				try
				{
					using (var logFile = GetLogFile())
					{
						zip.AddTopLevelFile(logFile.Path);
					}
				}
				catch (Exception)
				{
					// just ignore
				}
			}
			zip.Save();
		}

		private string GetFullDescriptionContents(bool appendLog)
		{
			string obfuscatedEmail;
			try
			{
				var m = new MailAddress(_email.Text);
				obfuscatedEmail = string.Format("{1} {0}", m.User, m.Host).Replace(".", "/");
			}
			catch(Exception)
			{
				obfuscatedEmail = _email.Text; // ah well, it's not valid anyhow, so no need to obfuscate (other code may not let the user get this far anyhow)
			}

			var bldr = new StringBuilder();
			bldr.AppendLine("Error Report from " + _name.Text + " (" + obfuscatedEmail + ") on " + DateTime.UtcNow.ToUniversalTime());
			bldr.AppendLine("=Problem Description=");
			bldr.AppendLine(_description.Text);
			bldr.AppendLine();
			GetStandardErrorReportingProperties(bldr, appendLog);
			return bldr.ToString();
		}

		//enhance: this is just copied from LibPalaso. When we move this whole class over there, we can get rid of it.
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

		private TempFile GetLogFile()
		{
			// NOTE: Logger holds a lock on the real log file, so we can't access it directly.
			// Instead we create a new temporary file that holds the content of the log file.
			var file = TempFile.WithFilenameInTempFolder(UsageReporter.AppNameToUseInReporting + ".log");
			try
			{
				File.WriteAllText(file.Path, Logger.LogText);
			}
			catch (Exception err)
			{
				//We have more than one report of dieing while logging an exception.
				File.WriteAllText(file.Path, "****Could not read from log: " + err.Message);
			}
			return file;
		}

		private void _cancelButton_Click(object sender, EventArgs e)
		{
			Close();
		}

		private void _seeDetails_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var temp = TempFile.WithExtension(".txt");
			File.WriteAllText(temp.Path, GetFullDescriptionContents(true));
			Process.Start(temp.Path);
			//yes, we're leaking this temp file
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			// BL-832: a bug in Mono requires us to wait to set Icon until handle created.
			this.Icon = global::Bloom.Properties.Resources.Bloom;
			this.ShowIcon = false;
		}

		private void _privacyLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var domain = "bloomlibrary.org";
			var msg = LocalizationManager.GetString("ReportProblemDialog.PrivacyNotice",
				@"If you don't care who reads your report, you can skip this notice.

Your report goes into our issue tracking system and will be visible via the web. We will obfuscate your address, so automatic spammers are unlikely to get your email this way.

So if you have something private to say, please email it to private@" + domain + ".");
			MessageBox.Show(this, msg, _privacyLabel.Text, MessageBoxButtons.OK);
		}
	}
}