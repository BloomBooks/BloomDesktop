using System;
using System.Windows.Forms;

namespace Bloom.MiscUI
{
	// This interface allows the unit tests to mock a BrowserDialog
	// when it's undesirable to spin up a real one.
	public interface IBrowserDialog : IDisposable
	{
		string CloseSource { get; set; }

		// Various properties/methods from the Form class (Sadly, it doesn't have an interface)
		// ENHANCE: Add more methods from Form as needed, or if you have patience to add all of them
		#region Properties from Form class
		bool ControlBox { get; set; }
		FormBorderStyle FormBorderStyle { get; set; }
		int Height { get; set; }
		string Text { get; set; }
		int Width { get; set; }
		#endregion

		#region Methods from Form class
		DialogResult ShowDialog();  // Desirable to be mocked out by unit tests
		#endregion
	}
}
