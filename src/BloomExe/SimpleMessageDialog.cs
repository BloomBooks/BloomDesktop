using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom
{
	public partial class SimpleMessageDialog : Form
	{
		public SimpleMessageDialog(string message)
		{
			InitializeComponent();
			betterLabel1.Text = message;
		}
	}
}
