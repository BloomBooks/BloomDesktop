using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom;

namespace TestGeckoPdf
{
	public partial class Form1 : Form
	{
		private string _pdfPath;

		public Form1()
		{
			Browser.SetUpXulRunner();
			InitializeComponent();
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			_pdfPath = @"C:\dev\temp\geckotest.pdf";
			var input = @"file:///C:/dev/Bloom/DistFiles/factoryCollections/Sample%20Shells/Vaccinations/Vaccinations.htm";
			browser1.Navigate(input,false);
			_geckoPdfMaker.Start(browser1, _pdfPath, "A4", false);
		}

		private void OnPdfReady(object sender, EventArgs e)
		{
			this.adobeReaderControl1.ShowPdf(_pdfPath);
		}
	}
}
