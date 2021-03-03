using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Properties;
using SolidBrush = System.Drawing.SolidBrush;

namespace Bloom.TeamCollection
{
	public class TeamCollectionStatusButton : ToolStripButton
	{
		public bool ShowExtraIcon { get; set; }

		public void Update(TeamCollectionStatus status)
		{
			this.Visible = status != TeamCollectionStatus.None;
			// Message should be localizable.
			switch (status)
			{
				case TeamCollectionStatus.Nominal:
					Text = "Team Collection";
					ForeColor = Color.White;
					Image = Resources.Team32x32;
					ShowExtraIcon = false;
					break;
				case TeamCollectionStatus.NewStuff:
					Text = "Updates Available";
					ForeColor = Color.FromArgb(255, 88, 210, 85);
					Image = Resources.TC_Button_Updates_Available;
					ShowExtraIcon = true;
					break;
				case TeamCollectionStatus.ClobberPending: // don't expect to use this
				case TeamCollectionStatus.Error:
					Text = "Problems Encountered";
					ForeColor = Bloom.Palette.BloomYellow;
					Image = Resources.TC_Button_Warning;
					ShowExtraIcon = true;
					break;

			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			// We want a grey round-cornered rectangle behind everything.
			// Unfortunately Graphics doesn't have a round-cornered rectangle function.
			// So we make it ourselves by overlapping two rectangles to make a plus shape that is
			// the rectangle we want minus the four corners. Then add four circles which again overlap
			// and fill in the corners.

			// edges of the whole rounded-corner rectangle
			var top = 4;
			var bottom = this.Height;
			var left = 0;
			var right = this.Width;
			var radius = 8; // of the desired corners
			var topBelowCurve = top + radius; // top of the horizontal bar of the plus
			var bottomAboveCurve = bottom - radius; // bottom of the horizontal bar
			var leftInsideCurve = left + radius; // left of the vertical bar
			var rightInsideCurve = right - radius; // right of the vertical bar
			var diameter = radius * 2;
			using (var brush = new SolidBrush(Palette.UnselectedTabBackground))
			{
				e.Graphics.FillRectangle(brush, left, topBelowCurve, right - left, bottomAboveCurve - topBelowCurve);
				e.Graphics.FillRectangle(brush, leftInsideCurve, top, rightInsideCurve - leftInsideCurve, bottom - top);
				e.Graphics.FillEllipse(brush, left, top, diameter, diameter);
				e.Graphics.FillEllipse(brush, right - diameter, top, diameter, diameter);
				e.Graphics.FillEllipse(brush, left, bottom - diameter, diameter, diameter);
				e.Graphics.FillEllipse(brush, right - diameter, bottom - diameter, diameter, diameter);
			}

			base.OnPaint(e); // on top of the grey rectangle; fortunately works because background is set to transparent.
			if (ShowExtraIcon)
			{
				var image = Resources.TC_Button_Grey_Small_Team;
				e.Graphics.DrawImage(image, 15, 10, 15, 15);
			}
		}
	}
}
