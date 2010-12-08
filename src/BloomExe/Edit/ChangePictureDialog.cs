using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.Edit
{
	public partial class ChangePictureDialog : Form
	{
		public ChangePictureDialog()
		{
			InitializeComponent();
		}

		public string CurrentPicturePath { get; set; }

		public string NewPicturePath{ get; set;}

		private void _artOfReadingPage_Click(object sender, EventArgs e)
		{

		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			NewPicturePath = @"C:\dev\Bloom\DistFiles\images\publish.png";
		}
	}
}
