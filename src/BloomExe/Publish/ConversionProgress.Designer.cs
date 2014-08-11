namespace GeckofxHtmlToPdf
{
	partial class ConversionProgress
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this._statusLabel = new System.Windows.Forms.Label();
			this._progressBar = new System.Windows.Forms.ProgressBar();
			this._pdfMaker = new GeckofxHtmlToPdfComponent(this.components);
			this.SuspendLayout();
			// 
			// _statusLabel
			// 
			this._statusLabel.AutoSize = true;
			this._statusLabel.Location = new System.Drawing.Point(27, 19);
			this._statusLabel.Name = "_statusLabel";
			this._statusLabel.Size = new System.Drawing.Size(54, 13);
			this._statusLabel.TabIndex = 0;
			this._statusLabel.Text = "Loading...";
			// 
			// _progressBar
			// 
			this._progressBar.Location = new System.Drawing.Point(30, 45);
			this._progressBar.Name = "_progressBar";
			this._progressBar.Size = new System.Drawing.Size(280, 23);
			this._progressBar.TabIndex = 1;
			// 
			// _pdfMaker
			// 
			this._pdfMaker.Finished += new System.EventHandler(this.OnPdfMaker_Finished);
			this._pdfMaker.StatusChanged += new System.EventHandler<PdfMakingStatus>(this.OnPdfMaker_StatusChanged);
			// 
			// ConversionProgress
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(343, 100);
			this.ControlBox = false;
			this.Controls.Add(this._progressBar);
			this.Controls.Add(this._statusLabel);
			this.Name = "ConversionProgress";
			this.Text = "GeckoFxHtmlToPdf";
			this.Load += new System.EventHandler(this.ConversionProgress_Load);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label _statusLabel;
		private System.Windows.Forms.ProgressBar _progressBar;
		private GeckofxHtmlToPdfComponent _pdfMaker;
	}
}