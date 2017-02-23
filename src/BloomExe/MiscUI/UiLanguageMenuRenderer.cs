using Bloom.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using L10NSharp;

namespace Bloom.MiscUI
{
	/// <summary>
	/// The idea for this class is from:
	/// http://stackoverflow.com/questions/23013481/toolstrip-drop-down-button-not-large-enough
	/// This allows us to override backcolor of selected menu items and borders, etc. to have the
	/// flat style that JH prefers.
	/// </summary>
	public class UiLanguageMenuRenderer : ToolStripProfessionalRenderer
	{
		private readonly Brush bloomBrush = new SolidBrush(Color.FromArgb(255, 229, 229, 229));

		/// <summary>
		/// Gives the menu button a gray background when mousing over, instead of the usual blue color.
		/// </summary>
		/// <param name="e"></param>
		protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
		{
			if (e.Item.GetType() == typeof(ToolStripDropDownButton) && e.Item.Selected)
			{
				e.Graphics.FillRectangle(bloomBrush, e.Item.Bounds);
			}
			else
			{
				base.OnRenderDropDownButtonBackground(e);
			}
		}

		/// <summary>
		/// Gets rid of the border around the containing toolstrip
		/// </summary>
		/// <param name="e"></param>
		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			// this is needed, especially on Linux
			e.SizeTextRectangleToText();
			base.OnRenderItemText(e);
		}

		/// <summary>
		/// Gives all the items on the dropdown list a gray background when mousing over them,
		/// instead of the usual blue color.
		/// </summary>
		/// <param name="e"></param>
		protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
		{
			if (e.Item.Selected)
			{
				var rect = new Rectangle(Point.Empty, e.Item.Size);
				e.Graphics.FillRectangle(bloomBrush, rect);
			}
			else
			{
				base.OnRenderMenuItemBackground(e);
			}
		}

		///// <summary>
		///// Make the dropdown arrow larger than the small standard arrow.
		///// </summary>
		///// <param name="e"></param>
		//protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
		//{
		//	if (e.Item.GetType() == typeof(ToolStripDropDownButton))
		//	{
		//		var arrowRectangle = e.ArrowRectangle;
		//		var points = new List<Point>();
		//		points.Add(new Point(arrowRectangle.Left - 2, arrowRectangle.Height / 2 - 3));
		//		points.Add(new Point(arrowRectangle.Right + 2, arrowRectangle.Height / 2 - 3));
		//		points.Add(new Point(arrowRectangle.Left + (arrowRectangle.Width / 2), arrowRectangle.Height / 2 + 3));
		//		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		//		e.Graphics.FillPolygon(Brushes.Black, points.ToArray());
		//	}
		//	else
		//	{
		//		base.OnRenderArrow(e);
		//	}
		//}

		/// <summary>
		/// This is a common method between CollectionChoosing.OpenCreateCloneControl.cs and Workspace.WorkspaceView.cs.
		/// If a better place to put this method is found, it could be moved.
		/// </summary>
		public static void SetupUiMenu(ToolStripDropDownButton uiMenuControl, EventHandler clickHandler)
		{
			uiMenuControl.DropDownItems.Clear();
			foreach (var lang in LocalizationManager.GetUILanguages(true))
			{
				var englishName = string.Empty;
				var languageNamesRecognizableByOtherLatinScriptReaders = new List<string> { "en", "fr", "es", "it", "tpi" };
				if ((lang.EnglishName != lang.NativeName) && !languageNamesRecognizableByOtherLatinScriptReaders.Contains(lang.Name))
				{
					englishName = " (" + lang.EnglishName + ")";
				}
				var item = uiMenuControl.DropDownItems.Add(lang.NativeName + englishName);
				item.Tag = lang;
				item.Click += clickHandler;
				if (((CultureInfo)item.Tag).IetfLanguageTag == Settings.Default.UserInterfaceLanguage)
				{
					uiMenuControl.Text = lang.NativeName;
					uiMenuControl.Width = item.Width;
				}
			}
		}
	}
}
