using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Palaso.IO;

namespace Bloom.Library
{
	public partial class HtmlSettingsDialog : Form
	{
		public delegate HtmlSettingsDialog Factory();//autofac uses this

		private readonly IFileLocator _fileLocator;

		public HtmlSettingsDialog(IFileLocator fileLocator)
		{
			_fileLocator = fileLocator;
			InitializeComponent();
		}

		private void Settings_Load(object sender, EventArgs e)
		{
			_browser.Navigate(_fileLocator.LocateFile("settings.htm", "Settings Dialog Html"), false);
		}
	}
}
