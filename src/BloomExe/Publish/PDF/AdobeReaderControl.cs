#if !__MonoCS__
using System;
using System.Windows.Forms;
using L10NSharp;

namespace Bloom.Publish.PDF
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
					_problemLabel.Text = LocalizationManager.GetString("PublishTab.AdobeReaderControl.NotInstalled", "Please install Adobe Reader so that Bloom can show your completed book. Until then, you can still save the PDF Book and open it in some other program.");
				}
				else
				{
					_problemLabel.Text = LocalizationManager.GetString("PublishTab.AdobeReaderControl.UnknownError", "Sad News. Bloom wasn't able to get Adobe Reader to show here, so Bloom can't show your completed book.\r\nPlease uninstall your existing version of 'Adobe Reader' and (re)install 'Adobe Reader'.\r\nUntil you get that fixed, you can still save the PDF Book and open it in some other program.");
				}
				_problemLabel.Visible = _problemPicture.Visible = true;
				if (this.Controls.Contains(this._adobeReader))//this actually gets in before the error
				{
					this.Controls.Remove(this._adobeReader);
				}
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
				_problemLabel.Text = LocalizationManager.GetString("PublishTab.AdobeReaderControl.ProblemShowingPDF", "That's strange... Adobe Reader gave an error when trying to show that PDF. You can still try saving the PDF Book.");
				_problemLabel.Visible = _problemPicture.Visible = true;
				Enabled = false;
				return false;
			}
		}

		public void Print()
		{
			_adobeReader.printWithDialog();//.printAll();
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			if (ParentForm != null)
				ParentForm.FormClosing += FormClosing;
		}

		private void FormClosing(object sender, FormClosingEventArgs e)
		{
			if (_adobeReader != null)
			{
				// This purposely SUPPRESSES the Dispose of _adobeReader that otherwise automatically happens
				// as the window shuts down. For some reason Dispose of this object is VERY slow; around 16 seconds
				// on my fast desktop. I don't think it can hang on to any important resources once the app
				// quits, so just prevent the Dispose.
				// (I tried various other things, such as loading a non-existent file and catching the resulting
				// exception, hiding the _adobeReader, Disposing it in advance (in this method)...nothing else
				// prevented the long delay on shutdown.)
				this.Controls.Remove(_adobeReader);
				_adobeReader = null;
			}
		}
	}
}
#endif
