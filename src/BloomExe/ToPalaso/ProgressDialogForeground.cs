using System;
using System.Threading;
using System.Windows.Forms;
using Palaso.Progress;
using Palaso.Progress;

namespace Bloom.ToPalaso.Experimental
{
	/// <summary>
	/// A Palaso.IProgress-compatible progress dialog which keeps the work in the foreground, using
	/// the progress calls by the worker to keep the UI alive. This has the advantage that any
	/// errors raised by the worker don't need special handling.
	///
	/// NOTE: this dialog is more of an experiment: it doesn't normally work... it's a lot to ask
	/// the ui to freeze and still keep working by means of an occasionally hacked-in Application.DoEvents();
	/// </summary>
	public partial class ProgressDialogForeground : Form
	{
		public ProgressDialogForeground()
		{
			InitializeComponent();
		}

		public MultiProgress Progress = new MultiProgress();
		private Action<IProgress> _work;

		public void ShowAndDoWork(Action<IProgress> work)
		{
			_work = work;
			Progress.ProgressIndicator = ProgressBar;
			Progress.AddStatusProgress(_status);
			Progress.AddMessageProgress(_messageLabelProgress);
			Progress.Add(new ApplicationDoEventsProgress());//this will keep our UI alive
			var stringProgress = new StringBuilderProgress();
			Progress.Add(stringProgress);
			Application.Idle += StartWorking;
			ShowDialog();
			if (Progress.ErrorEncountered)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem("There was a problem performing that operation.\r\n\r\n"+stringProgress.Text);
			}

		}

		void StartWorking(object sender, EventArgs e)
		{
			Application.Idle -= new EventHandler(StartWorking);
			_work(Progress);
			Close();
		}

		/// <summary>
		/// Everytime some progress is reported, we just let the UI update
		/// </summary>
		class ApplicationDoEventsProgress : IProgress
		{
			public void WriteStatus(string message, params object[] args)
			{
				Application.DoEvents();
			}

			public void WriteMessage(string message, params object[] args)
			{
				Application.DoEvents();
			}

			public void WriteMessageWithColor(string colorName, string message, params object[] args)
			{
				Application.DoEvents();
			}

			public void WriteWarning(string message, params object[] args)
			{
				Application.DoEvents();
			}

			public void WriteException(Exception error)
			{
				Application.DoEvents();
			}

			public void WriteError(string message, params object[] args)
			{
				ErrorEncountered = true;
				Application.DoEvents();
			}

			public void WriteVerbose(string message, params object[] args)
			{
				Application.DoEvents();
			}

			public bool ShowVerbose
			{
				get { return false; }
				set { }
			}

			public virtual bool CancelRequested { get; set; }

			public virtual bool ErrorEncountered { get; set; }

			public IProgressIndicator ProgressIndicator { get; set; }

			public SynchronizationContext SyncContext { get; set; }
		}

		private void ProgressDialogForeground_Load(object sender, EventArgs e)
		{
			Progress.SyncContext = SynchronizationContext.Current;
			_messageLabelProgress.SyncContext = SynchronizationContext.Current;
		}
	}
}
