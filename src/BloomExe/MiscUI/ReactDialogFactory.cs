namespace Bloom.MiscUI
{
	public interface IReactDialogFactory
	{
		IBrowserDialog CreateReactDialog(string reactComponentName, object props);
	}

	class ReactDialogFactory: IReactDialogFactory
	{
		public IBrowserDialog CreateReactDialog(string reactComponentName, object props)
		{
			return new ReactDialog(reactComponentName, props);
		}
	}
}
