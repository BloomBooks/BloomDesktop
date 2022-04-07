using Bloom.MiscUI;
using System;

namespace Bloom.ErrorReporter
{
	/// <summary>
	/// </summary>
	public class PathTooLongErrorReporter
	{
		private PathTooLongErrorReporter()
		{
			using (var dlg = new ReactDialog("PathTooLongDialog", new { }, "Bloom Error"))
			{
				dlg.Width = 300;
				dlg.Height = 300;

				var result = dlg.ShowDialog();
			}
		}
	}
}
