using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using SIL.Windows.Forms.Progress;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// Interface that we wish ProgressDialog implemented
	/// </summary>
	public interface IProgressDialog
	{
		int ProgressRangeMaximum { get; set; }
		int Progress { get; set; }
		// Note: the ConsoleProgress implementation just runs the delegate on the current thread.
		object Invoke(Delegate method);
	}

	// For a real progress dialog we pass this.
	class ProgressDialogWrapper : IProgressDialog
	{
		private ProgressDialog _dialog;

		public ProgressDialogWrapper(ProgressDialog dialog)
		{
			_dialog = dialog;
		}

		public int ProgressRangeMaximum
		{
			get { return _dialog.ProgressRangeMaximum; }
			set { _dialog.ProgressRangeMaximum = value; }
		}

		public int Progress
		{
			get { return _dialog.Progress; }
			set { _dialog.Progress = value; }
		}

		public object Invoke(Delegate method)
		{
			return _dialog.Invoke(method);
		}
	}

	// If we're running from a console we pass this.
	class ConsoleProgress : IProgressDialog
	{
		private int _progress;
		public int ProgressRangeMaximum { get; set; }

		public int Progress
		{
			get { return _progress; }
			set
			{
				if (value > _progress)
				{
					Console.Write(".");
				}
				_progress = value;
			}
		}

		public object Invoke(Delegate method)
		{
			return method.Method.Invoke(method.Target, new object[0]);
		}
	}
}
