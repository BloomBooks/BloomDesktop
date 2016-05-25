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
			if (Bouncing())
				return;
			Implementer();
			SetBounceTime();	// Start bounce timing here in case implementer is slow.
		}

		private readonly static TimeSpan _bounceWait = TimeSpan.FromMilliseconds(500);
		private static DateTime _previousClickTime = DateTime.MinValue;
		/// <summary>
		/// Check whether the click activating this command came too quickly to be a separate command.
		/// This handles people double-clicking when they should single click (or clicking again while
		/// a command is executing that takes longer than they think it should).
		/// </summary>
		private static bool Bouncing()
		{
			var now = DateTime.Now;
			var bouncing = now - _previousClickTime < _bounceWait;
			_previousClickTime = now;
			return bouncing;
		}

		private static void SetBounceTime()
		{
			_previousClickTime = DateTime.Now;
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