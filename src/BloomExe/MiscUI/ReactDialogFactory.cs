using System;

namespace Bloom.MiscUI
{
	public interface IReactDialogFactory
	{
		IBrowserDialog CreateReactDialog(string javascriptBundleName, string reactComponentName, string urlQueryString);
	}

	class ReactDialogFactory: IReactDialogFactory
	{
		public IBrowserDialog CreateReactDialog(string javascriptBundleName, string reactComponentName, string urlQueryString)
		{
			return new ReactDialog(javascriptBundleName, reactComponentName, urlQueryString);
		}
	}
}
