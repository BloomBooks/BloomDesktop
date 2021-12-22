using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using SIL.Progress;

namespace Bloom.ToPalaso
{
	/// <summary>
	/// Conveys messages into the label. Useful as a poor-man's status indicator when using command-line processors
	/// </summary>
	public class MessageLabelProgress :Label, IProgress
	{
		public MessageLabelProgress()
		{
			VisibleChanged += new EventHandler(MessageLabelProgress_VisibleChanged);
		}

		void MessageLabelProgress_VisibleChanged(object sender, EventArgs e)
		{
		}
		public SynchronizationContext SyncContext { get; set; }

		public bool ShowVerbose
		{
			set { }
		}
		public bool ErrorEncountered { get; set; }

		public IProgressIndicator ProgressIndicator { get; set; }

		public bool CancelRequested { get; set; }


		public void WriteStatus(string message, params object[] args)
		{
		}

		public void WriteMessage(string message, params object[] args)
		{
			try
			{
				string theMessage = GenericProgress.SafeFormat(message, args);
				if (InvokeRequired)
				{
					BeginInvoke(new Action(() => UpdateText(theMessage)));
				}
				else
				{
					UpdateText(theMessage);
				}
			}
			catch (Exception error)
			{
#if DEBUG
				Debug.Fail(error.Message);
#endif
			}
		}

		private void UpdateText(object state)
		{
			try
			{
				Debug.WriteLine("updatetext: "+state as string);
				Text = state as string;
			}
			catch (Exception error)
			{
#if DEBUG
				Debug.Fail(error.Message);
#endif
			}
		}
		public void WriteMessageWithColor(string colorName, string message, params object[] args)
		{
			WriteMessage(message, args);
		}

		public void WriteWarning(string message, params object[] args)
		{
		}

		public void WriteException(Exception error)
		{
		}

		public void WriteError(string message, params object[] args)
		{
		}

		public void WriteVerbose(string message, params object[] args)
		{
			WriteMessage(message, args);
		}
	}
}
