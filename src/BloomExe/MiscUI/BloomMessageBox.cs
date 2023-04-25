using System;
using System.Windows.Forms;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.MiscUI
{

	/// <summary>
	/// This class is intended to replace the Windows.Forms MessageBox. It's fairly rudimentary at present, but
	/// already more flexible in that it allows arbitrary button options. Also, we can use MaterialUI style,
	/// making it more consistent with our other UI, and the message can be arbitrary HTML.
	/// As yet we only support MessageBoxIcon.Warning, but this can be added to (on the Typescript side)
	/// as needed.
	/// </summary>
	public class BloomMessageBox
	{
		private static bool s_justRecordMessageBoxMessagesForTesting=false;
		private static string s_previousMessageBoxMessage;

		public static string Show(IWin32Window owner, string messageHtml, MessageBoxButton[] rightButtons, MessageBoxIcon icon = MessageBoxIcon.None)
		{
			if (s_justRecordMessageBoxMessagesForTesting)
			{
				s_previousMessageBoxMessage = messageHtml;
				return "closedByReportButton";
			}
			using (var dlg = new ReactDialog("messageBoxBundle", new
			{
				messageHtml,
				rightButtonDefinitions = rightButtons,
				icon = icon.ToString().ToLowerInvariant(),
				closeWithAPICall = true
			}))
			{
				dlg.Width = 500;
				dlg.Height = 200;
				// This dialog is neater without a task bar. We don't need to be able to
				// drag it around. There's nothing left to give it one if we don't set a title
				// and remove the control box.
				dlg.ControlBox = false;
				if (owner == null)
					dlg.StartPosition = FormStartPosition.CenterScreen;
				dlg.ShowDialog(owner);
				return dlg.CloseSource;
			}
		}

		/// <summary>
		/// This version assumes we just want a message, info icon, and a single Close button.
		/// </summary>
		public static string ShowInfo(string message)
		{
			return ShowSimple(message, MessageBoxIcon.Information);
		}

		/// <summary>
		/// This version assumes we just want a message, warning icon, and a single Close button.
		/// </summary>
		public static string ShowWarning(string message)
		{
			return ShowSimple(message, MessageBoxIcon.Warning);
		}

		/// <summary>
		/// This version assumes we just want a message, (optional) icon, and a single Close button.
		/// </summary>
		public static string ShowSimple(string message, MessageBoxIcon icon = MessageBoxIcon.None)
		{
			var closeText = LocalizationManager.GetString("Common.Close", "Close");
			var messageBoxButtons = new[]
			{
				new MessageBoxButton() { Text = closeText, Id = "close", Default = true }
			};
			var openForm = Shell.GetShellOrOtherOpenForm();
			return Show(openForm, message, messageBoxButtons, icon);
		}

		/// <summary>
		/// Use this in unit tests to cleanly check that a messagebox would have been shown.
		/// E.g.  using (new Bloom.MiscUI.BloomMessageBox.ShowInfoExpected()) {...}
		/// </summary>
		/// <remarks>Based on the similar ErrorReport.NonFatalErrorReportExpected.</remarks>
		public class ShowExpected :IDisposable
		{
			private readonly bool previousJustRecordMessageBoxMessagesForTesting;
			public ShowExpected()
			{
				previousJustRecordMessageBoxMessagesForTesting = s_justRecordMessageBoxMessagesForTesting;
				s_justRecordMessageBoxMessagesForTesting = true;
				// This is a static, so a previous unit test could have filled it with something (yuck)
				s_previousMessageBoxMessage = null;
			}
			public void Dispose()
			{
				s_justRecordMessageBoxMessagesForTesting = previousJustRecordMessageBoxMessagesForTesting;
				if (s_previousMessageBoxMessage == null)
					throw new Exception("BloomMessageBox was expected but wasn't generated.");
				s_previousMessageBoxMessage = null;
			}
			/// <summary>
			/// use this to check the actual contents of the message that was triggered
			/// </summary>
			public string Message
			{
				get { return s_previousMessageBoxMessage; }
			}
		}
	}

	public class MessageBoxButton
	{
		[JsonProperty("text")]
		public string Text;
		[JsonProperty("id")]
		public string Id;
		[JsonProperty("default")]
		public bool Default;
	}
}
