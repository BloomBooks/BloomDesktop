using System;

namespace Bloom
{
	/// <summary>
	/// Everything which implements this will be found by the IOC assembly scanner
	/// and made available to any constructors which call for an IEnumerable<ICommand/>
	/// </summary>
	public interface ICommand
	{
		string Id { get; }
		void Execute();
		bool Enabled { get; }
		event EventHandler EnabledChanged;
	}

	public abstract class Command : ICommand
	{
		public event EventHandler EnabledChanged;
		private bool _enabled;
		public Command(string id)
		{
			Id = id;
			Enabled = true;
		}
		public string Id
		{ get; private set; }

		public bool Enabled
		{
			get { return _enabled; }
			private set
			{
				_enabled = value;
				if (EnabledChanged != null)
				{
					EnabledChanged.Invoke(this, null);
				}
			}

		}

		public abstract void Execute();
	}



}