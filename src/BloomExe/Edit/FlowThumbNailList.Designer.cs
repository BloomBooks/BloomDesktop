namespace Bloom.Edit
{
	partial class FlowThumbNailList
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
            //_listView = null;
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
			System.Drawing.Imaging.ImageAttributes imageAttributes1 = new System.Drawing.Imaging.ImageAttributes();
			this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
			this.button5 = new System.Windows.Forms.Button();
			this.bitmapButton1 = new Palaso.UI.WindowsForms.Widgets.BitmapButton();
			this.flowLayoutPanel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// flowLayoutPanel1
			// 
			this.flowLayoutPanel1.Controls.Add(this.button5);
			this.flowLayoutPanel1.Controls.Add(this.bitmapButton1);
			this.flowLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.flowLayoutPanel1.Location = new System.Drawing.Point(0, 0);
			this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
			this.flowLayoutPanel1.Name = "flowLayoutPanel1";
			this.flowLayoutPanel1.Size = new System.Drawing.Size(154, 150);
			this.flowLayoutPanel1.TabIndex = 1;
			// 
			// button5
			// 
			this.button5.AutoSize = true;
			this.button5.Dock = System.Windows.Forms.DockStyle.Top;
			this.button5.FlatAppearance.BorderSize = 0;
			this.button5.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button5.Image = global::Bloom.Properties.Resources.placeHolderBookThumbnail;
			this.button5.Location = new System.Drawing.Point(0, 0);
			this.button5.Margin = new System.Windows.Forms.Padding(0);
			this.button5.Name = "button5";
			this.button5.Size = new System.Drawing.Size(76, 93);
			this.button5.TabIndex = 9;
			this.button5.Text = "1";
			this.button5.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.button5.UseVisualStyleBackColor = true;
			// 
			// bitmapButton1
			// 
			this.bitmapButton1.BorderColor = System.Drawing.Color.Transparent;
			this.bitmapButton1.DisabledTextColor = System.Drawing.Color.DimGray;
			this.bitmapButton1.FlatAppearance.BorderSize = 0;
			this.bitmapButton1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.bitmapButton1.FocusRectangleEnabled = false;
			this.bitmapButton1.Image = null;
			this.bitmapButton1.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
			this.bitmapButton1.ImageAttributes = imageAttributes1;
			this.bitmapButton1.ImageBorderColor = System.Drawing.Color.Transparent;
			this.bitmapButton1.ImageBorderEnabled = false;
			this.bitmapButton1.ImageDropShadow = false;
			this.bitmapButton1.ImageFocused = null;
			this.bitmapButton1.ImageInactive = null;
			this.bitmapButton1.ImageMouseOver = null;
			this.bitmapButton1.ImageNormal = global::Bloom.Properties.Resources.placeHolderBookThumbnail;
			this.bitmapButton1.ImagePressed = null;
			this.bitmapButton1.InnerBorderColor = System.Drawing.Color.LightGray;
			this.bitmapButton1.InnerBorderColor_Focus = System.Drawing.Color.LightBlue;
			this.bitmapButton1.InnerBorderColor_MouseOver = System.Drawing.Color.Gold;
			this.bitmapButton1.Location = new System.Drawing.Point(76, 0);
			this.bitmapButton1.Margin = new System.Windows.Forms.Padding(0);
			this.bitmapButton1.Name = "bitmapButton1";
			this.bitmapButton1.OffsetPressedContent = true;
			this.bitmapButton1.Size = new System.Drawing.Size(61, 96);
			this.bitmapButton1.StretchImage = false;
			this.bitmapButton1.TabIndex = 10;
			this.bitmapButton1.Text = "2";
			this.bitmapButton1.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
			this.bitmapButton1.TextDropShadow = false;
			this.bitmapButton1.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.bitmapButton1.UseVisualStyleBackColor = false;
			// 
			// FlowThumbNailList
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.SystemColors.Control;
			this.Controls.Add(this.flowLayoutPanel1);
			this.ForeColor = System.Drawing.SystemColors.WindowText;
			this.Name = "FlowThumbNailList";
			this.Size = new System.Drawing.Size(154, 150);
			//this.BackColorChanged += new System.EventHandler(this.ThumbNailList_BackColorChanged);
			this.flowLayoutPanel1.ResumeLayout(false);
			this.flowLayoutPanel1.PerformLayout();
			this.ResumeLayout(false);

        }

        #endregion

		private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
		private System.Windows.Forms.Button button5;
		private Palaso.UI.WindowsForms.Widgets.BitmapButton bitmapButton1;

	}
}
