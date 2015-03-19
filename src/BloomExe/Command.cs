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
		event EventHandler EnabledChanged;
	}

	public abstract class Command : ICommand
	{
		public event EventHandler EnabledChanged;
		private bool _enabled;

		public Command(string name)
		{
			Name = name;
			_enabled = true;
		}

		public bool Enabled
		{
			get { return Implementer != null && _enabled; }
			set
			{
				_enabled = value;
				RaiseEnabledChanged();
			}
		}
		public Action Implementer { get; set; }
		public string Name { get; set; }

		public void Execute()
		{
			Implementer();
		}
		protected virtual void RaiseEnabledChanged()
		{
			var handler = EnabledChanged;
			if(handler != null)
				handler(this, EventArgs.Empty);
		}
	}

	public class CutCommand : Command
	{
		public CutCommand()
			: base("cut")
		{

		}
	}

	public class CopyCommand : Command
	{
		public CopyCommand()
			: base("copy")
		{

		}
	}

	public class PasteCommand : Command
	{
		public PasteCommand()
			: base("paste")
		{

		}
	}
	public class UndoCommand : Command
	{
		public UndoCommand()
			: base("undo")
		{

		}
	}

	public class DuplicatePageCommand : Command
	{
		public DuplicatePageCommand()
			: base("duplicateCurrentPage")
		{

		}
	}

	public class DeletePageCommand : Command
	{
		public DeletePageCommand()
			: base("deleteCurrentPage")
		{

		}
	}

}