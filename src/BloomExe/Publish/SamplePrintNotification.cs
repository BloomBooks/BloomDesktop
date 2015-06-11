using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Bloom.Publish
{
	public partial class SamplePrintNotification : Form
	{
		public SamplePrintNotification()
		{
			InitializeComponent();
		}

		public bool StopShowing
		{
			get { return _stopShowingCheckBox.Checked; }
		}
	}
}
