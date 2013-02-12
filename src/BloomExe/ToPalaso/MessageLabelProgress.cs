using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using Palaso.Code;
using Palaso.Progress;
using Palaso.Progress;

namespace Bloom.ToPalaso
{
	/// <summary>
	/// Conveys messages into the label. Useful as a poor-man's status indicator when using command-line processors
	/// </summary>
	public class MessageLabelProgress :Label, IProgress
	{
		private bool _loaded;

		public MessageLabelProgress()
		{
			VisibleChanged += new EventHandler(MessageLabelProgress_VisibleChanged);
		}

		void MessageLabelProgress_VisibleChanged(object sender, EventArgs e)
		{
			_loaded = true;
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
//			if (!_loaded)
//				return;

			try
			{
//				string theMessage = GenericProgress.SafeFormat(message, args);
//				//Guard.AgainstNull(SyncContext,"SyncContext");
//				///SyncContext.Post(UpdateText, theMessage);
//
//				this.Invoke(new Action(() => { UpdateText(theMessage);}));
//				Application.DoEvents();

				string theMessage = GenericProgress.SafeFormat(message, args);

//				if (SyncContext != null)
//				{
//					SyncContext.Post(UpdateText, theMessage);//Post() means do it asynchronously, don't wait around and see if there is an exception
//				}
//				else
//				{
//					Text = theMessage;
//				}

				//if (IsHandleCreated)
				{
					if (InvokeRequired)
					{
						BeginInvoke(new Action(() => UpdateText(theMessage)));
					}
					else
					{
						UpdateText(theMessage);
					}
				}
//				else
//				{
//					// in this case InvokeRequired might lie - you need to make sure that this never happens!
//					throw new Exception("Somehow Handle has not yet been created on the UI thread!");
//				}

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
