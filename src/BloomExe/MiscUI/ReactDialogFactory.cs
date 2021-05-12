using System;

namespace Bloom.MiscUI
{
	public interface IReactDialogFactory
	{
		IBrowserDialog CreateReactDialog(string reactComponentName, object props, string urlQueryString);
	}

	class ReactDialogFactory: IReactDialogFactory
	{
		public IBrowserDialog CreateReactDialog(string reactComponentName, object props, string urlQueryString)
		{
			return new ReactDialog(reactComponentName, props, urlQueryString);
		}
	}
}
