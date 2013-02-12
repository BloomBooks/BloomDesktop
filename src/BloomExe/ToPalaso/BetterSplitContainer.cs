using System.ComponentModel;
using System.Windows.Forms;

namespace Bloom.ToPalaso
{
	//core code from http://windowsclient.net/blogs/faqs/archive/tags/SplitContainer/default.aspx

	/// <summary>
	/// A splitter which doesn't leave itself selected when you use it (that default behavior is lame)
	/// </summary>
	public partial class BetterSplitContainer : SplitContainer
	{
		public BetterSplitContainer()
		{
			InitializeComponent();
		}

		public BetterSplitContainer(IContainer container)
		{
			container.Add(this);

			InitializeComponent();
		}

		private Control _previouslyFocusedControl = null;

		private void OnMouseDown(object sender, MouseEventArgs e)
		{
			// Get the focused control before the splitter is focused
			_previouslyFocusedControl = GetCurrentlyFocusedControl(this.Controls);
		}

		private Control GetCurrentlyFocusedControl(Control.ControlCollection controls)
		{
			foreach (Control c in controls)
			{
				if (c.Focused)
				{
					// Return the focused control
					return c;
				}
				else if (c.ContainsFocus)
				{
					// If the focus is contained inside a control's children
					// return the child
					return GetCurrentlyFocusedControl(c.Controls);
				}
			}
			// No control on the form has focus
			return null;
		}

		private void OnMouseUp(object sender, MouseEventArgs e)
		{
			// If a previous control had focus
			if (_previouslyFocusedControl != null)
			{
				// Return focus and clear the temp variable for
				// garbage collection
				_previouslyFocusedControl.Focus();
				_previouslyFocusedControl = null;
			}
		}

	}
}
