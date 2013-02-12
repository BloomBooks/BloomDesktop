using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Bloom
{
	/// <summary>
	/// Everything which implements this will be found by the IOC assembly scanner
	/// and made available to any constructors which call for an IEnumerable<ICommand/>
	/// </summary>
	public interface ICommand
	{
		void Execute();
		bool Enabled { get; set; }
		//event EventHandler EnabledChanged;
	}

	public abstract class Command : ICommand
	{
		//public event EventHandler EnabledChanged;
		public bool Enabled { get; set; }
		public Command()
		{
			Enabled = true;
		}

//		public bool Enabled
//		{
//			get {return Implementer !=null && _enabled; }
//			private set
//			{
//				_enabled = value;
//				if (EnabledChanged != null)
//				{
//					EnabledChanged.Invoke(this, null);
//				}
//			}
//        }
	   public Action Implementer { get; set;}

		public void Execute()
		{
			Implementer();
		}
	}

	public class CutCommand : Command { }
	public class CopyCommand : Command { }
	public class PasteCommand : Command{}
	public class UndoCommand : Command { }
	public class DeletePageCommand : Command { }

}