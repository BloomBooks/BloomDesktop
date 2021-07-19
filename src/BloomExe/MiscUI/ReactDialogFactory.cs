namespace Bloom.MiscUI
{
	public interface IReactDialogFactory
	{
		IBrowserDialog CreateReactDialog(string javascriptBundleName, object props);
	}

	class ReactDialogFactory: IReactDialogFactory
	{
		public IBrowserDialog CreateReactDialog( string javascriptBundleName,  object props)
		{
			return new ReactDialog( javascriptBundleName, props);
		}
	}
}
