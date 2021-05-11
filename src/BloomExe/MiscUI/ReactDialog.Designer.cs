
namespace Bloom.MiscUI
{
	partial class ReactDialog
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
			this.reactControl = new Bloom.web.ReactControl();
			this.SuspendLayout();
			// 
			// reactControl1
			// 
			this.reactControl.BackColor = System.Drawing.Color.White;
			this.reactControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this.reactControl.Location = new System.Drawing.Point(0, 0);
			this.reactControl.Name = "reactControl";
			this.reactControl.ReactComponentName = null;
			this.reactControl.Size = new System.Drawing.Size(800, 450);
			this.reactControl.TabIndex = 0;
			this.reactControl.UseEditContextMenu = true;
			// 
			// ReactDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(800, 450);
			this.ControlBox = false;
			this.Controls.Add(this.reactControl);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			this.Name = "ReactDialog";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "ReactDialog";
			this.ResumeLayout(false);

		}

		#endregion

		private Bloom.web.ReactControl reactControl;
	}
}
