using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Palaso.UI.WindowsForms.Progress;

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
