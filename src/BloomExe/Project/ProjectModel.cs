using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;

namespace Bloom.Project
{
	public class ProjectModel
	{
		private readonly BookSelection _bookSelection;
		private readonly string _directoryPath;

		public delegate ProjectModel Factory(string directoryPath);//autofac uses this
		public event EventHandler UpdateDisplay;


		public ProjectModel(BookSelection bookSelection, string directoryPath)
		{
			_bookSelection = bookSelection;
			_directoryPath = directoryPath;
			_bookSelection.SelectionChanged += new EventHandler(OnSelectionChanged);
		}

		public bool ShowEditPage
		{
			get { return _bookSelection.CurrentSelection!=null && _bookSelection.CurrentSelection.CanEdit; }
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
