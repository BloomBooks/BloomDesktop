namespace Bloom.Edit
{
    partial class EditingView
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
			this.splitContainer1 = new System.Windows.Forms.SplitContainer();
			this.splitContainer2 = new System.Windows.Forms.SplitContainer();
			this._browser1 = new Bloom.Browser();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this.splitContainer2.Panel1.SuspendLayout();
			this.splitContainer2.SuspendLayout();
			this.SuspendLayout();
			// 
			// splitContainer1
			// 
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer1.Location = new System.Drawing.Point(0, 0);
			this.splitContainer1.Name = "splitContainer1";
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(63)))), ((int)(((byte)(64)))));
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this.splitContainer2);
			this.splitContainer1.Size = new System.Drawing.Size(686, 497);
			this.splitContainer1.SplitterDistance = 127;
			this.splitContainer1.SplitterWidth = 1;
			this.splitContainer1.TabIndex = 0;
			// 
			// splitContainer2
			// 
			this.splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
			this.splitContainer2.Location = new System.Drawing.Point(0, 0);
			this.splitContainer2.Name = "splitContainer2";
			// 
			// splitContainer2.Panel1
			// 
			this.splitContainer2.Panel1.Controls.Add(this._browser1);
			// 
			// splitContainer2.Panel2
			// 
			this.splitContainer2.Panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(63)))), ((int)(((byte)(64)))));
			this.splitContainer2.Size = new System.Drawing.Size(558, 497);
			this.splitContainer2.SplitterDistance = 459;
			this.splitContainer2.SplitterWidth = 1;
			this.splitContainer2.TabIndex = 0;
			// 
			// _browser1
			// 
			this._browser1.BackColor = System.Drawing.Color.DarkGray;
			this._browser1.Dock = System.Windows.Forms.DockStyle.Fill;
			this._browser1.Location = new System.Drawing.Point(0, 0);
			this._browser1.Name = "_browser1";
			this._browser1.Size = new System.Drawing.Size(459, 497);
			this._browser1.TabIndex = 1;
			this._browser1.Validating += new System.ComponentModel.CancelEventHandler(this._browser1_Validating);
			// 
			// EditingView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.splitContainer1);
			this.Name = "EditingView";
			this.Size = new System.Drawing.Size(686, 497);
			this.splitContainer1.Panel2.ResumeLayout(false);
			this.splitContainer1.ResumeLayout(false);
			this.splitContainer2.Panel1.ResumeLayout(false);
			this.splitContainer2.ResumeLayout(false);
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.SplitContainer splitContainer2;
        private Browser _browser1;


    }
}