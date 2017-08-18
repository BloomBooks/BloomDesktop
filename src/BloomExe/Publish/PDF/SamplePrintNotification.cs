using System.Windows.Forms;

namespace Bloom.Publish.PDF
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
