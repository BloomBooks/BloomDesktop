using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Localization;

namespace Bloom.Publish
{
	/// <summary>
	/// Wraps the adobe reader ActiveX control primarily so that we can
	/// gracefully handle situations where the Reader control is not installed,
	/// has some version or other unknown problem.
	///
	/// This control will show a sad face and helpful information when the
	/// Reader can't be shown.
	/// </summary>
	public partial class AdobeReaderControl : UserControl
	{
		private AxAcroPDFLib.AxAcroPDF _adobeReader;

		public AdobeReaderControl()
		{
			InitializeComponent();
		}

		private bool InitializeAdobeReader()
		{
			try
			{
				this._adobeReader = new AxAcroPDFLib.AxAcroPDF();
				((System.ComponentModel.ISupportInitialize) (this._adobeReader)).BeginInit();
				this._adobeReader.Dock = DockStyle.Fill;
				this._adobeReader.Enabled = true;
				this._adobeReader.Location = new System.Drawing.Point(103, 3);
				this._adobeReader.Name = "_adobeReader";
				//this._adobeReader.TabIndex = 5;
				this.Controls.Add(this._adobeReader);
				((System.ComponentModel.ISupportInitialize) (this._adobeReader)).EndInit();
				return true;
			}
			catch (Exception error)
			{
				if (error.Message.Contains("0x80040154"))
				{
					_problemLabel.Text = LocalizationManager.GetString("AdobeReaderControl.NotInstalled", "Please install Adobe Reader X so that Bloom can show your completed book. Until then, you can still save the PDF Book and open it in some other program.");
				}
				else
				{
					_problemLabel.Text = LocalizationManager.GetString("AdobeReaderControl.UnknownError", "Sad News. Bloom wasn't able to get Adobe Reader to show here, so Bloom can't show your completed book.\r\nPlease uninstall your existing version of 'Adobe Reader' and (re)install 'Adobe Reader X'.\r\nUntil you get that fixed, you can still save the PDF Book and open it in some other program.");
				}
				_problemLabel.Visible = _problemPicture.Visible = true;
				_adobeReader = null;
				Enabled = false;
				return false;
			}
		}

		/// <summary>
		/// Load up the ActiveX control if needed, and show the doc if we can
		/// </summary>
		/// <param name="pdfFilePath"></param>
		/// <returns>true if it successfully showed the pdf</returns>
		public bool ShowPdf(string pdfFilePath)
		{
			if (_adobeReader == null)
			{
				if(!InitializeAdobeReader())
				{
					return false;
				}
			}

			try
			{
				//http://wwwimages.adobe.com/www.adobe.com/content/dam/Adobe/en/devnet/acrobat/pdfs/pdf_open_parameters.pdf

				_adobeReader.Hide();
				// can't handle non-ascii names _adobeReader.LoadFile(path);
				_adobeReader.src = pdfFilePath;

				_adobeReader.setShowToolbar(false);
				_adobeReader.setView("Fit");
				_adobeReader.Show();
				_problemLabel.Visible = _problemPicture.Visible = false;
				Enabled = true;
				return true;
			}
			catch (Exception e)
			{
				_adobeReader.Hide();
				_problemLabel.Text = LocalizationManager.GetString("AdobeReaderControl.ProblemShowingPDF", "That's strange... Adobe Reader gave an error when trying to show that PDF. You can still try saving the PDF Book.");
				_problemLabel.Visible = _problemPicture.Visible = true;
				Enabled = false;
				return false;
			}
		}

		public void Print()
		{
			_adobeReader.printAll();
		}
	}
}
