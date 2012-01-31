namespace Bloom.Publish
{
    partial class PublishView
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PublishView));
			this._loadTimer = new System.Windows.Forms.Timer(this.components);
			this._noBookletRadio = new System.Windows.Forms.RadioButton();
			this._coverRadio = new System.Windows.Forms.RadioButton();
			this._bodyRadio = new System.Windows.Forms.RadioButton();
			this._adobeReader = new AxAcroPDFLib.AxAcroPDF();
			this.button1 = new System.Windows.Forms.Button();
			this._makePdfBackgroundWorker = new System.ComponentModel.BackgroundWorker();
			this._workingIndicator = new System.Windows.Forms.Panel();
			this._workingIndicatorGif = new System.Windows.Forms.PictureBox();
			this._saveButton = new System.Windows.Forms.Button();
			((System.ComponentModel.ISupportInitialize)(this._adobeReader)).BeginInit();
			this._workingIndicator.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._workingIndicatorGif)).BeginInit();
			this.SuspendLayout();
			// 
			// _noBookletRadio
			// 
			this._noBookletRadio.AutoSize = true;
			this._noBookletRadio.Location = new System.Drawing.Point(12, 3);
			this._noBookletRadio.Name = "_noBookletRadio";
			this._noBookletRadio.Size = new System.Drawing.Size(161, 17);
			this._noBookletRadio.TabIndex = 2;
			this._noBookletRadio.TabStop = true;
			this._noBookletRadio.Text = "One page per piece of paper";
			this._noBookletRadio.UseVisualStyleBackColor = true;
			this._noBookletRadio.CheckedChanged += new System.EventHandler(this._bookletRadio_CheckedChanged);
			// 
			// _coverRadio
			// 
			this._coverRadio.AutoSize = true;
			this._coverRadio.Location = new System.Drawing.Point(199, 3);
			this._coverRadio.Name = "_coverRadio";
			this._coverRadio.Size = new System.Drawing.Size(120, 17);
			this._coverRadio.TabIndex = 3;
			this._coverRadio.TabStop = true;
			this._coverRadio.Text = "Booklet Cover Page";
			this._coverRadio.UseVisualStyleBackColor = true;
			this._coverRadio.CheckedChanged += new System.EventHandler(this._bookletRadio_CheckedChanged);
			// 
			// _bodyRadio
			// 
			this._bodyRadio.AutoSize = true;
			this._bodyRadio.Location = new System.Drawing.Point(345, 3);
			this._bodyRadio.Name = "_bodyRadio";
			this._bodyRadio.Size = new System.Drawing.Size(125, 17);
			this._bodyRadio.TabIndex = 4;
			this._bodyRadio.TabStop = true;
			this._bodyRadio.Text = "Booklet Inside Pages";
			this._bodyRadio.UseVisualStyleBackColor = true;
			this._bodyRadio.CheckedChanged += new System.EventHandler(this._bookletRadio_CheckedChanged);
			// 
			// _adobeReader
			// 
			this._adobeReader.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._adobeReader.Enabled = true;
			this._adobeReader.Location = new System.Drawing.Point(3, 42);
			this._adobeReader.Name = "_adobeReader";
			this._adobeReader.OcxState = ((System.Windows.Forms.AxHost.State)(resources.GetObject("_adobeReader.OcxState")));
			this._adobeReader.Size = new System.Drawing.Size(827, 495);
			this._adobeReader.TabIndex = 5;
			// 
			// button1
			// 
			this.button1.Location = new System.Drawing.Point(587, 3);
			this.button1.Name = "button1";
			this.button1.Size = new System.Drawing.Size(56, 26);
			this.button1.TabIndex = 6;
			this.button1.Text = "&Print...";
			this.button1.UseVisualStyleBackColor = true;
			this.button1.Click += new System.EventHandler(this.button1_Click);
			// 
			// _makePdfBackgroundWorker
			// 
			this._makePdfBackgroundWorker.WorkerSupportsCancellation = true;
			this._makePdfBackgroundWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(this._makePdfBackgroundWorker_DoWork);
			// 
			// _workingIndicator
			// 
			this._workingIndicator.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._workingIndicator.BackColor = System.Drawing.Color.White;
			this._workingIndicator.Controls.Add(this._workingIndicatorGif);
			this._workingIndicator.Location = new System.Drawing.Point(3, 42);
			this._workingIndicator.Name = "_workingIndicator";
			this._workingIndicator.Size = new System.Drawing.Size(827, 495);
			this._workingIndicator.TabIndex = 8;
			// 
			// _workingIndicatorGif
			// 
			this._workingIndicatorGif.BackColor = System.Drawing.Color.White;
			this._workingIndicatorGif.Image = global::Bloom.Properties.Resources.spinner;
			this._workingIndicatorGif.Location = new System.Drawing.Point(316, 148);
			this._workingIndicatorGif.Name = "_workingIndicatorGif";
			this._workingIndicatorGif.Size = new System.Drawing.Size(179, 141);
			this._workingIndicatorGif.TabIndex = 8;
			this._workingIndicatorGif.TabStop = false;
			// 
			// _saveButton
			// 
			this._saveButton.Location = new System.Drawing.Point(674, 3);
			this._saveButton.Name = "_saveButton";
			this._saveButton.Size = new System.Drawing.Size(56, 26);
			this._saveButton.TabIndex = 9;
			this._saveButton.Text = "&Save...";
			this._saveButton.UseVisualStyleBackColor = true;
			this._saveButton.Click += new System.EventHandler(this._saveButton_Click);
			// 
			// PublishView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._saveButton);
			this.Controls.Add(this._workingIndicator);
			this.Controls.Add(this.button1);
			this.Controls.Add(this._adobeReader);
			this.Controls.Add(this._bodyRadio);
			this.Controls.Add(this._coverRadio);
			this.Controls.Add(this._noBookletRadio);
			this.Name = "PublishView";
			this.Size = new System.Drawing.Size(833, 540);
			((System.ComponentModel.ISupportInitialize)(this._adobeReader)).EndInit();
			this._workingIndicator.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this._workingIndicatorGif)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		private System.Windows.Forms.Timer _loadTimer;
        private System.Windows.Forms.RadioButton _noBookletRadio;
        private System.Windows.Forms.RadioButton _coverRadio;
        private System.Windows.Forms.RadioButton _bodyRadio;
		private AxAcroPDFLib.AxAcroPDF _adobeReader;
		private System.Windows.Forms.Button button1;
		private System.ComponentModel.BackgroundWorker _makePdfBackgroundWorker;
		private System.Windows.Forms.Panel _workingIndicator;
		private System.Windows.Forms.PictureBox _workingIndicatorGif;
		private System.Windows.Forms.Button _saveButton;
    }
}