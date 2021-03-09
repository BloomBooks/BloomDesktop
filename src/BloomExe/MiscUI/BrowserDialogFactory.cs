using System;

namespace Bloom.MiscUI
{
	public interface IBrowserDialogFactory
	{
		IBrowserDialog CreateBrowserDialog(string url, bool hidden = false, Action whenClosed = null);
	}

	class BrowserDialogFactory: IBrowserDialogFactory
	{
		public IBrowserDialog CreateBrowserDialog(string url, bool hidden = false, Action whenClosed = null)
		{
			return new BrowserDialog(url, hidden, whenClosed);
		}
	}
}
