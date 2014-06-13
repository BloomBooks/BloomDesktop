namespace Bloom.Edit
{
    partial class TemplatePagesView
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
           // this.VisibleChanged -= new System.EventHandler(this.TemplatePagesView_VisibleChanged);
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
			System.Windows.Forms.Label label1;
			System.Windows.Forms.Panel panel1;
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._thumbNailList = new Bloom.Edit.ThumbNailList();
			label1 = new System.Windows.Forms.Label();
			panel1 = new System.Windows.Forms.Panel();
			panel1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// label1
			// 
			label1.AutoSize = true;
			label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			label1.ForeColor = System.Drawing.Color.WhiteSmoke;
			this._L10NSharpExtender.SetLocalizableToolTip(label1, null);
			this._L10NSharpExtender.SetLocalizationComment(label1, null);
			this._L10NSharpExtender.SetLocalizingId(label1, "EditTab.TemplatePagesList.Heading");
			label1.Location = new System.Drawing.Point(2, 7);
			label1.Name = "label1";
			label1.Size = new System.Drawing.Size(129, 18);
			label1.TabIndex = 1;
			label1.Text = "Template Pages";
			// 
			// panel1
			// 
			panel1.Controls.Add(label1);
			panel1.Dock = System.Windows.Forms.DockStyle.Top;
			panel1.Location = new System.Drawing.Point(0, 0);
			panel1.Name = "panel1";
			panel1.Size = new System.Drawing.Size(155, 39);
			panel1.TabIndex = 3;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// _thumbNailList
			// 
			this._thumbNailList.BackColor = System.Drawing.SystemColors.Control;
			this._thumbNailList.Dock = System.Windows.Forms.DockStyle.Fill;
			this._thumbNailList.Font = new System.Drawing.Font("Tahoma", 9F);
			this._thumbNailList.ForeColor = System.Drawing.SystemColors.WindowText;
			this._thumbNailList.ItemWhichWouldPrecedeANewPageInsertion = null;
			this._L10NSharpExtender.SetLocalizableToolTip(this._thumbNailList, null);
			this._L10NSharpExtender.SetLocalizationComment(this._thumbNailList, null);
			this._L10NSharpExtender.SetLocalizingId(this._thumbNailList, "TemplatePagesView.ThumbNailList");
			this._thumbNailList.Location = new System.Drawing.Point(0, 39);
			this._thumbNailList.Name = "_thumbNailList";
			this._thumbNailList.PreferPageNumbers = false;
			this._thumbNailList.RelocatePageEvent = null;
			this._thumbNailList.Size = new System.Drawing.Size(155, 140);
			this._thumbNailList.TabIndex = 2;
			// 
			// TemplatePagesView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.Controls.Add(this._thumbNailList);
			this.Controls.Add(panel1);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "EditTab.TemplatePagesList");
			this.Name = "TemplatePagesView";
			this.Size = new System.Drawing.Size(155, 179);
			this.BackColorChanged += new System.EventHandler(this.TemplatePagesView_BackColorChanged);
			panel1.ResumeLayout(false);
			panel1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

        }

        #endregion

		private Bloom.Edit.ThumbNailList _thumbNailList;
        private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
    }
}
