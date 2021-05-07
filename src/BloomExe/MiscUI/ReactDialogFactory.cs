namespace Bloom.MiscUI
{
	public interface IReactDialogFactory
	{
		IBrowserDialog CreateReactDialog(string reactComponentName, string urlQueryString);
	}

	class ReactDialogFactory: IReactDialogFactory
	{
		public IBrowserDialog CreateReactDialog(string reactComponentName, string urlQueryString)
		{
			return new ReactDialog(reactComponentName, urlQueryString);
		}
	}
}
