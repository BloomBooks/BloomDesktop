using System;
using System.Drawing;
using System.Windows.Forms;

namespace Bloom.Workspace
{
	public partial class ZoomControl : UserControl
	{
		private int _zoom;

		public ZoomControl()
		{
			InitializeComponent();
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
				var newValue = Math.Max(value, 30);
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
