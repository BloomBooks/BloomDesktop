using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
using Palaso.Progress.LogBox;

namespace Bloom.ToPalaso
{
	/// <summary>
	/// A Palaso.IProgress-compatible progress dialog which does the work in the background
	/// </summary>
	public partial class ProgressDialogBackground : Form
	{
		public ProgressDialogBackground()
		{
			InitializeComponent();
			_statusLabel.Text= "";
		}

		public MultiProgress Progress = new MultiProgress();

		public void ShowAndDoWork(Action<IProgress, DoWorkEventArgs> work)
		{
			Progress.ProgressIndicator = ProgressBar;

			Progress.AddStatusProgress(_statusLabel);
			//Progress.AddMessageProgress(_messageLabelProgress);
			_backgroundWorker.RunWorkerCompleted += (sender, e) => Close();
			_backgroundWorker.WorkerReportsProgress = true;
			_backgroundWorker.WorkerSupportsCancellation = true;
			_backgroundWorker.ProgressChanged += new ProgressChangedEventHandler(_backgroundWorker_ProgressChanged);
			_backgroundWorker.DoWork += (sender, arg) => work(Progress, arg);
			ShowDialog();
		}

		void _backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			ProgressBar.PercentCompleted = e.ProgressPercentage;
			Refresh();
		}

		private void ProgressDialog_Load(object sender, EventArgs e)
		{
			Progress.SyncContext = SynchronizationContext.Current;
			_backgroundWorker.RunWorkerAsync(_backgroundWorker);
		}
	}
}
