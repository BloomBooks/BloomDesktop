
namespace Bloom.web
{
	partial class ReactControl
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
			this._settingsDisplay = new System.Windows.Forms.TextBox();
			this.SuspendLayout();
			// 
			// _settingsDisplay
			// 
			this._settingsDisplay.BackColor = System.Drawing.Color.Honeydew;
			this._settingsDisplay.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this._settingsDisplay.Location = new System.Drawing.Point(20, 17);
			this._settingsDisplay.Multiline = true;
			this._settingsDisplay.Name = "_settingsDisplay";
			this._settingsDisplay.Size = new System.Drawing.Size(354, 101);
			this._settingsDisplay.TabIndex = 0;
			this._settingsDisplay.Text = "_settingsDisplay";
			this._settingsDisplay.Visible = false;
			// 
			// ReactControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.Honeydew;
			this.Controls.Add(this._settingsDisplay);
			this.Name = "ReactControl";
			this.Size = new System.Drawing.Size(401, 150);
			this.Load += new System.EventHandler(this.ReactControl_Load);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox _settingsDisplay;
	}
}
