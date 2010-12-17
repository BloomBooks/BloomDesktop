using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Crop
{
	public partial class Form1 : Form
	{
		public Form1()
		{
			InitializeComponent();
		}

		private void imageCropper1_Load(object sender, EventArgs e)
		{
			imageCropper1.Image = Image.FromFile(@"C:\Art of Reading\Images\Brazil\B-NA-6.tif");
		}
	}
}
