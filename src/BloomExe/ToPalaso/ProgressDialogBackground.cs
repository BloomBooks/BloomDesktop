using System;
using System.Threading;
using System.Windows.Forms;
using Palaso.Progress.LogBox;

namespace Bloom.Edit
{
	public partial class ProgressDialogBackground : Form
	{
		public ProgressDialogBackground()
		{
			InitializeComponent();
		}

		public MultiProgress Progress = new MultiProgress();

		public void ShowAndDoWork(Action<IProgress> work)
		{
			Progress.ProgressIndicator = ProgressBar;

			Progress.AddStatusProgress(Status);
			_backgroundWorker.RunWorkerCompleted += (sender, e) => Close();
			_backgroundWorker.DoWork += (sender, arg) => work(Progress);
			ShowDialog();
		}

		private void ProgressDialog_Load(object sender, EventArgs e)
		{
			Progress.SyncContext = SynchronizationContext.Current;
			_backgroundWorker.RunWorkerAsync();
		}
	}
}
