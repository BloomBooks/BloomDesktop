using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Atlassian.Jira;
using Bloom.Book;
using L10NSharp;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Reporting;

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

		private enum State { WaitingForSubmission, ZippingUpBook, Submitting, CouldNotAutomaticallySubmit, Success }

		private readonly BookSelection _bookSelection;
		private Bitmap _screenshot;
		private State _state;
		private string _emailableReportFilePath;
		private string _jiraProjectKey = "BL";
		private bool _closeAfterSubmittingForUnitTest;
		private Issue _jiraIssue;

		public ProblemReporterDialog(Control targetOfScreenshot, BookSelection bookSelection)
		{
			_bookSelection = bookSelection;

			InitializeComponent();

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

		private void ChangeState(State state)
		{
			_state = state;
			UpdateDisplay();
			Application.DoEvents();// make the state change show up.
		}

		private void UpdateDisplay(object sender, EventArgs e)
		{
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			_submitButton.Enabled = !string.IsNullOrWhiteSpace(_name.Text.Trim()) && !string.IsNullOrWhiteSpace(_email.Text.Trim()) &&
								   !string.IsNullOrWhiteSpace(_description.Text.Trim());

			_screenshotHolder.Visible = _includeScreenshot.Checked;

			switch (_state)
			{
				case State.WaitingForSubmission:
					_status.Visible = false;
					_seeDetails.Visible = true;
					Cursor = Cursors.Default;
					break;

				case State.ZippingUpBook:
					_seeDetails.Visible = false;
					_status.Visible = true;
					_status.HTML = LocalizationManager.GetString("ReportProblemDialog.Zipping", "Zipping up book...",
						"This is shown while Bloom is creating the problem report. It's generally too fast to see, unless you include a large book.");
					_submitButton.Enabled = false;
					Cursor = Cursors.WaitCursor;
					break;

				case State.Submitting:
					_seeDetails.Visible = false;
					_status.Visible = true;
					_status.HTML = LocalizationManager.GetString("ReportProblemDialog.Submitting", "Submitting to server...",
						"This is shown while Bloom is sending the problem report to our server.");
					_submitButton.Enabled = false;
					Cursor = Cursors.WaitCursor;
					break;

				case State.CouldNotAutomaticallySubmit:
					_seeDetails.Visible = false;
					_status.Visible = true;
					var message = LocalizationManager.GetString("ReportProblemDialog.CouldNotSendToServer",
						"Bloom was not able to submit your report directly to our server. Please retry or email {0} to {1}.");
					_status.HTML = string.Format("<span style='color:red'>" + message + "</span>", "<a href='file://" + _emailableReportFilePath + "'>" + Path.GetFileName(_emailableReportFilePath) + "</a>", "<a href='mailto://issues@bloomlibrary.org?subject=Problem Report'>issues@bloomlibrary.org</a>");

					_submitButton.Text = LocalizationManager.GetString("ReportProblemDialog.Retry", "Retry",
						"Shown if there was an error submitting the report. Lets the user try submitting it again.");
					Cursor = Cursors.Default;
					break;

				case State.Success:
					_seeDetails.Visible = false;
					_status.Visible = true;
					_submitButton.Enabled = true;
					_submitButton.Text = LocalizationManager.GetString("ReportProblemDialog.Close", "Close", "Shown in the button that closes the dialog after a successful report submission.");
					message = LocalizationManager.GetString("ReportProblemDialog.Success",
						"We received your report, thanks for taking the time to help make Bloom better!");
					this.AcceptButton = _submitButton;
					_submitButton.Focus();
					_status.HTML = string.Format("<span style='color:blue'>" + message + "</span><br/><a href='{0}'>{1}</a>", _jiraIssue.Jira.Url + "browse/" + _jiraIssue.Key.Value, _jiraIssue.Key.Value);

					Cursor = Cursors.Default;
					if (_closeAfterSubmittingForUnitTest)
					{
						_okButton_Click(this, null);
					}
					break;

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			if (_state == State.Success)
			{
				Close();
				return;
			}

			if (SubmitToJira())
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

		/// <summary>
		/// Using the Atlassian SDK here. That SDK doesn't permit looking up users, so we can't submit
		/// the report as if it were from this person, even if they have an account (well, not without
		/// asking them for credentials, which is just not gonna happen). So we submit with an
		/// account we created just for this purpose, "auto_report_creator".
		/// </summary>
		/// <returns></returns>
		private bool SubmitToJira()
		{
			try
			{
				ChangeState(State.Submitting);

				var jira = new Jira("https://jira.sil.org", "auto_report_creator", "thisIsInOpenSourceCode");
				_jiraIssue = jira.CreateIssue(_jiraProjectKey);
				_jiraIssue.Type = "Awaiting Classification";
				_jiraIssue.Summary = "User Problem Report " + _email.Text;
				_jiraIssue.Description = GetFullDescriptionContents();

				_jiraIssue.SaveChanges();

				//this could all be done in one go, but I'm doing it in stages so as to increase the chance of success in bad internet situations
				if (_includeScreenshot.Checked)
				{
					using (var file = TempFile.WithFilenameInTempFolder("screenshot.png"))
					{
						_screenshot.Save(file.Path, ImageFormat.Png);
						_jiraIssue.AddAttachment(file.Path);
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
						_jiraIssue.AddAttachment(bookZip.Path);
					}
				}

				ChangeState(State.Submitting);
				_jiraIssue.SaveChanges();
				ChangeState(State.Success);
				return true;
			}
			catch (Exception error)
			{
				Debug.Fail(error.Message);
				return false;
			}
		}

		/// <summary>
		/// If we are able to directly submit to JIRA, we do that. But otherwise,
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
					stream.WriteLine(GetFullDescriptionContents());

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
			zip.Save();
		}

		private string GetFullDescriptionContents()
		{
			var bldr = new StringBuilder();
			bldr.AppendLine("Error Report from " + _name.Text + " (" + _email.Text + ") on " + DateTime.UtcNow.ToUniversalTime());
			bldr.AppendLine("--Problem Description--");
			bldr.AppendLine(_description.Text);
			bldr.AppendLine();
			GetStandardErrorReportingProperties(bldr);
			return bldr.ToString();
		}

		//enhance: this is just copied from LibPalaso. When we move this whole class over there, we can get rid of it.
		private static void GetStandardErrorReportingProperties(StringBuilder bldr)
		{
			bldr.AppendLine();
			bldr.AppendLine("--Error Reporting Properties--");
			foreach (string label in ErrorReport.Properties.Keys)
			{
				bldr.Append(label);
				bldr.Append(": ");
				bldr.AppendLine(ErrorReport.Properties[label]);
			}

			bldr.AppendLine();
			bldr.AppendLine("--Log--");
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

		private void _cancelButton_Click(object sender, EventArgs e)
		{
			Close();
		}

		public void SetupForUnitTest(string jiraProjectKey)
		{
			this.Load += (sender, e) =>
			{
				_jiraProjectKey = jiraProjectKey;
				_description.Text = "created by unit test of " + Assembly.GetAssembly(this.GetType()).FullName;
				_closeAfterSubmittingForUnitTest = true;
				_okButton_Click(sender, e);
			};
		}

		private void _seeDetails_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var temp = TempFile.WithExtension(".txt");
			File.WriteAllText(temp.Path, GetFullDescriptionContents());
			Process.Start(temp.Path);
			//yes, we're leaking this temp file
		}
	}
}