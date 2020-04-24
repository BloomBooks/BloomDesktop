using System;
using System.Drawing;
using System.Windows.Forms;

namespace Bloom.Workspace
{
	public partial class ZoomControl : UserControl
	{
		public const int kMinimumZoom =  30;	// 30% - 300% matches FireFox
		public const int kMaximumZoom = 300;

		private int _zoom;

		public ZoomControl()
		{
			InitializeComponent();
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				// Work around a bug in Mono 5 (and Mono 6?).
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-8360.
				this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
				this._minusButton.Location = new System.Drawing.Point(0, -8);	// restore original locations
				this._percentLabel.Location = new System.Drawing.Point(20, 4);
				this._plusButton.Location = new System.Drawing.Point(58, -8);
			}
			Zoom = 100;
			_plusButton.ForeColor = Color.FromKnownColor(KnownColor.MenuText);
			_plusButton.FlatAppearance.MouseOverBackColor = SystemColors.GradientActiveCaption;
			_minusButton.ForeColor = Color.FromKnownColor(KnownColor.MenuText);
			_minusButton.FlatAppearance.MouseOverBackColor = SystemColors.GradientActiveCaption;
			_percentLabel.ForeColor = Color.FromKnownColor(KnownColor.MenuText);
		}

		public int Zoom
		{
			get { return _zoom; }
			set
			{
				var newValue = Math.Min(Math.Max(value, kMinimumZoom), kMaximumZoom);
				if (newValue == _zoom)
					return;
				_zoom = newValue;
				_percentLabel.Text = _zoom + @"%";
				ZoomChanged?.Invoke(this, new EventArgs());
			}
		}

		public event EventHandler<EventArgs> ZoomChanged;

		private void _minusButton_Click(object sender, EventArgs e)
		{
			Zoom = Zoom - 10;
		}

		private void _plusButton_Click(object sender, EventArgs e)
		{
			Zoom = Zoom + 10;
		}
	}
}
