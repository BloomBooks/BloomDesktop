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
			System.Windows.Forms.Panel panel1;
			this._templatePagesListHeading = new System.Windows.Forms.Label();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			panel1 = new System.Windows.Forms.Panel();
			panel1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// panel1
			// 
			panel1.Controls.Add(this._templatePagesListHeading);
			panel1.Dock = System.Windows.Forms.DockStyle.Top;
			panel1.Location = new System.Drawing.Point(0, 0);
			panel1.Name = "panel1";
			panel1.Size = new System.Drawing.Size(155, 39);
			panel1.TabIndex = 3;
			// 
			// _templatePagesListHeading
			// 
			this._templatePagesListHeading.AutoSize = true;
			this._templatePagesListHeading.Font = new System.Drawing.Font("Microsoft Sans Serif", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._templatePagesListHeading.ForeColor = System.Drawing.Color.WhiteSmoke;
			this._L10NSharpExtender.SetLocalizableToolTip(this._templatePagesListHeading, null);
			this._L10NSharpExtender.SetLocalizationComment(this._templatePagesListHeading, null);
			this._L10NSharpExtender.SetLocalizingId(this._templatePagesListHeading, "EditTab.TemplatePagesList.Heading");
			this._templatePagesListHeading.Location = new System.Drawing.Point(2, 7);
			this._templatePagesListHeading.Name = "_templatePagesListHeading";
			this._templatePagesListHeading.Size = new System.Drawing.Size(129, 18);
			this._templatePagesListHeading.TabIndex = 1;
			this._templatePagesListHeading.Text = "Template Pages";
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// TemplatePagesView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.Controls.Add(panel1);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "EditTab.TemplatePagesList");
			this.Name = "TemplatePagesView";
			this.Size = new System.Drawing.Size(155, 179);
			panel1.ResumeLayout(false);
			panel1.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

        }

        #endregion

		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Label _templatePagesListHeading;
    }
}
