namespace Bloom.ToPalaso
{
	partial class WelcomeControl
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WelcomeControl));
			this._imageList = new System.Windows.Forms.ImageList(this.components);
			this._debounceListIndexChangedEvent = new System.Windows.Forms.Timer(this.components);
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this._templateLabel = new System.Windows.Forms.Label();
			this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
			this.label1 = new System.Windows.Forms.Label();
			this._templateButton = new System.Windows.Forms.Button();
			this.flowLayoutPanel1.SuspendLayout();
			this.SuspendLayout();
			//
			// _imageList
			//
			this._imageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_imageList.ImageStream")));
			this._imageList.TransparentColor = System.Drawing.Color.Magenta;
			this._imageList.Images.SetKeyName(0, "getFromUsb");
			this._imageList.Images.SetKeyName(1, "wesayProject");
			this._imageList.Images.SetKeyName(2, "getFromInternet");
			this._imageList.Images.SetKeyName(3, "newLibrary");
			this._imageList.Images.SetKeyName(4, "browse");
			//
			// toolTip1
			//
			this.toolTip1.AutomaticDelay = 300;
			//
			// _templateLabel
			//
			this._templateLabel.AutoSize = true;
			this._templateLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._templateLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(12)))), ((int)(((byte)(133)))), ((int)(((byte)(151)))));
			this._templateLabel.Location = new System.Drawing.Point(426, 3);
			this._templateLabel.Name = "_templateLabel";
			this._templateLabel.Size = new System.Drawing.Size(112, 20);
			this._templateLabel.TabIndex = 7;
			this._templateLabel.Text = "Template Label";
			this._templateLabel.Visible = false;
			//
			// flowLayoutPanel1
			//
			this.flowLayoutPanel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
			| System.Windows.Forms.AnchorStyles.Left)
			| System.Windows.Forms.AnchorStyles.Right)));
			this.flowLayoutPanel1.Controls.Add(this.label1);
			this.flowLayoutPanel1.Location = new System.Drawing.Point(27, -11);
			this.flowLayoutPanel1.Name = "flowLayoutPanel1";
			this.flowLayoutPanel1.Size = new System.Drawing.Size(557, 327);
			this.flowLayoutPanel1.TabIndex = 8;
			//
			// label1
			//
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(3, 0);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(35, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "label1";
			//
			// _templateButton
			//
			this._templateButton.FlatAppearance.BorderSize = 0;
			this._templateButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._templateButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._templateButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._templateButton.ImageKey = "newLibrary";
			this._templateButton.ImageList = this._imageList;
			this._templateButton.Location = new System.Drawing.Point(51, 0);
			this._templateButton.Name = "_templateButton";
			this._templateButton.Size = new System.Drawing.Size(351, 43);
			this._templateButton.TabIndex = 6;
			this._templateButton.Text = "   templateButton";
			this._templateButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._templateButton.UseVisualStyleBackColor = true;
			this._templateButton.Visible = false;
			this._templateButton.Click += new System.EventHandler(this._templateButton_Click);
			//
			// WelcomeControl
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.BackColor = System.Drawing.Color.White;
			this.Controls.Add(this._templateLabel);
			this.Controls.Add(this._templateButton);
			this.Controls.Add(this.flowLayoutPanel1);
			this.Name = "WelcomeControl";
			this.Size = new System.Drawing.Size(587, 316);
			this.Load += new System.EventHandler(this.OnLoad);
			this.flowLayoutPanel1.ResumeLayout(false);
			this.flowLayoutPanel1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ImageList _imageList;
		private System.Windows.Forms.Timer _debounceListIndexChangedEvent;
		private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.Button _templateButton;
		private System.Windows.Forms.Label _templateLabel;
		private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
		private System.Windows.Forms.Label label1;

	}
}
