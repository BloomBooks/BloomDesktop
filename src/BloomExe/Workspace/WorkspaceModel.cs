using System;
using System.IO;
using Bloom.Book;

namespace Bloom.Workspace
{
	public class WorkspaceModel
	{
		private readonly BookSelection _bookSelection;
		private readonly string _directoryPath;

		public delegate WorkspaceModel Factory(string directoryPath);//autofac uses this
		public event EventHandler UpdateDisplay;


		public WorkspaceModel(BookSelection bookSelection, string directoryPath)
		{
			_bookSelection = bookSelection;
			_directoryPath = directoryPath;
			_bookSelection.SelectionChanged += new EventHandler(OnSelectionChanged);
		}

		public bool ShowEditPage
		{
			get { return _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.IsInEditableLibrary && !_bookSelection.CurrentSelection.HasFatalError; }
		}

		public bool ShowPublishPage
		{
			get { return _bookSelection.CurrentSelection != null && _bookSelection.CurrentSelection.CanPublish; }
		}

		public string ProjectName
		{
			get { return Path.GetFileName(_directoryPath); }
		}

		void OnSelectionChanged(object sender, EventArgs e)
		{
			InvokeUpdateDisplay();
		}

		private void InvokeUpdateDisplay()
		{
			EventHandler handler = UpdateDisplay;
			if (handler != null)
			{
				handler(this, null);
			}
		}

		public Book.Book FindTemplate(string key)
		{
			return null;
		}

		public bool CloseRequested()
		{
			return true;
		}
	}


}
