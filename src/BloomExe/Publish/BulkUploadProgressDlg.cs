using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SIL.Windows.Forms.Progress;

namespace Bloom.Publish
{
	public partial class BulkUploadProgressDlg : Form
	{
		public LogBox Progress;
		public BulkUploadProgressDlg()
		{
			InitializeComponent();
			Progress = new LogBox();
			Progress.Dock = DockStyle.Fill;
			Controls.Add(Progress);
		}
	}
}
