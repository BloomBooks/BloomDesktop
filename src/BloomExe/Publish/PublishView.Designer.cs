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
			this._adobeReader = new AxAcroPDFLib.AxAcroPDF();
			this._makePdfBackgroundWorker = new System.ComponentModel.BackgroundWorker();
			this._workingIndicator = new System.Windows.Forms.Panel();
			this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
			this._noBookletRadio = new System.Windows.Forms.RadioButton();
			this._coverRadio = new System.Windows.Forms.RadioButton();
			this._bodyRadio = new System.Windows.Forms.RadioButton();
			this._saveButton = new System.Windows.Forms.Button();
			this._printButton = new System.Windows.Forms.Button();
			this._workingIndicatorGif = new System.Windows.Forms.PictureBox();
			((System.ComponentModel.ISupportInitialize)(this._adobeReader)).BeginInit();
			this._workingIndicator.SuspendLayout();
			this.tableLayoutPanel1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._workingIndicatorGif)).BeginInit();
			this.SuspendLayout();
			// 
			// _adobeReader
			// 
			this._adobeReader.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._adobeReader.Enabled = true;
			this._adobeReader.Location = new System.Drawing.Point(103, 3);
			this._adobeReader.Name = "_adobeReader";
			this._adobeReader.OcxState = ((System.Windows.Forms.AxHost.State)(resources.GetObject("_adobeReader.OcxState")));
			this._adobeReader.Size = new System.Drawing.Size(730, 534);
			this._adobeReader.TabIndex = 5;
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
			this._workingIndicator.Controls.Add(this._saveButton);
			this._workingIndicator.Controls.Add(this._printButton);
			this._workingIndicator.Controls.Add(this._workingIndicatorGif);
			this._workingIndicator.Location = new System.Drawing.Point(103, 0);
			this._workingIndicator.Name = "_workingIndicator";
			this._workingIndicator.Size = new System.Drawing.Size(730, 540);
			this._workingIndicator.TabIndex = 8;
			// 
			// tableLayoutPanel1
			// 
			this.tableLayoutPanel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.tableLayoutPanel1.ColumnCount = 1;
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel1.Controls.Add(this._bodyRadio, 0, 2);
			this.tableLayoutPanel1.Controls.Add(this._coverRadio, 0, 1);
			this.tableLayoutPanel1.Controls.Add(this._noBookletRadio, 0, 0);
			this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Left;
			this.tableLayoutPanel1.ForeColor = System.Drawing.Color.White;
			this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
			this.tableLayoutPanel1.Name = "tableLayoutPanel1";
			this.tableLayoutPanel1.RowCount = 3;
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.Size = new System.Drawing.Size(100, 540);
			this.tableLayoutPanel1.TabIndex = 10;
			// 
			// _noBookletRadio
			// 
			this._noBookletRadio.AutoSize = true;
			this._noBookletRadio.Location = new System.Drawing.Point(3, 3);
			this._noBookletRadio.Name = "_noBookletRadio";
			this._noBookletRadio.Size = new System.Drawing.Size(94, 17);
			this._noBookletRadio.TabIndex = 3;
			this._noBookletRadio.TabStop = true;
			this._noBookletRadio.Text = "One page per piece of paper";
			this._noBookletRadio.UseVisualStyleBackColor = true;
			// 
			// _coverRadio
			// 
			this._coverRadio.AutoSize = true;
			this._coverRadio.Location = new System.Drawing.Point(3, 26);
			this._coverRadio.Name = "_coverRadio";
			this._coverRadio.Size = new System.Drawing.Size(94, 17);
			this._coverRadio.TabIndex = 4;
			this._coverRadio.TabStop = true;
			this._coverRadio.Text = "Booklet Cover Page";
			this._coverRadio.UseVisualStyleBackColor = true;
			// 
			// _bodyRadio
			// 
			this._bodyRadio.AutoSize = true;
			this._bodyRadio.Location = new System.Drawing.Point(3, 49);
			this._bodyRadio.Name = "_bodyRadio";
			this._bodyRadio.Size = new System.Drawing.Size(94, 17);
			this._bodyRadio.TabIndex = 5;
			this._bodyRadio.TabStop = true;
			this._bodyRadio.Text = "Booklet Inside Pages";
			this._bodyRadio.UseVisualStyleBackColor = true;
			// 
			// _saveButton
			// 
			this._saveButton.AutoSize = true;
			this._saveButton.BackColor = System.Drawing.Color.Transparent;
			this._saveButton.FlatAppearance.BorderSize = 0;
			this._saveButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._saveButton.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._saveButton.ForeColor = System.Drawing.Color.Black;
			this._saveButton.Image = global::Bloom.Properties.Resources.Usb;
			this._saveButton.Location = new System.Drawing.Point(240, 21);
			this._saveButton.Name = "_saveButton";
			this._saveButton.Size = new System.Drawing.Size(104, 71);
			this._saveButton.TabIndex = 13;
			this._saveButton.Text = "&Save...";
			this._saveButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._saveButton.UseVisualStyleBackColor = false;
			this._saveButton.Click += new System.EventHandler(this.OnSave_Click);
			// 
			// _printButton
			// 
			this._printButton.AutoSize = true;
			this._printButton.BackColor = System.Drawing.Color.Transparent;
			this._printButton.FlatAppearance.BorderSize = 0;
			this._printButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._printButton.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._printButton.ForeColor = System.Drawing.Color.Black;
			this._printButton.Image = global::Bloom.Properties.Resources.print;
			this._printButton.Location = new System.Drawing.Point(28, 21);
			this._printButton.Name = "_printButton";
			this._printButton.Size = new System.Drawing.Size(105, 64);
			this._printButton.TabIndex = 12;
			this._printButton.Text = "&Print...";
			this._printButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._printButton.UseVisualStyleBackColor = false;
			this._printButton.Click += new System.EventHandler(this.OnPrint_Click);
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
			// PublishView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.Controls.Add(this.tableLayoutPanel1);
			this.Controls.Add(this._workingIndicator);
			this.Controls.Add(this._adobeReader);
			this.Name = "PublishView";
			this.Size = new System.Drawing.Size(833, 540);
			((System.ComponentModel.ISupportInitialize)(this._adobeReader)).EndInit();
			this._workingIndicator.ResumeLayout(false);
			this._workingIndicator.PerformLayout();
			this.tableLayoutPanel1.ResumeLayout(false);
			this.tableLayoutPanel1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._workingIndicatorGif)).EndInit();
			this.ResumeLayout(false);

        }

        #endregion

		private System.Windows.Forms.Timer _loadTimer;
		private AxAcroPDFLib.AxAcroPDF _adobeReader;
		private System.ComponentModel.BackgroundWorker _makePdfBackgroundWorker;
		private System.Windows.Forms.Panel _workingIndicator;
		private System.Windows.Forms.PictureBox _workingIndicatorGif;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
		private System.Windows.Forms.RadioButton _bodyRadio;
		private System.Windows.Forms.RadioButton _coverRadio;
		private System.Windows.Forms.RadioButton _noBookletRadio;
		private System.Windows.Forms.Button _saveButton;
		private System.Windows.Forms.Button _printButton;
    }
}