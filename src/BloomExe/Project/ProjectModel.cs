using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bloom.Project
{
	public class ProjectModel
	{
		private readonly BookSelection _bookSelection;

		public delegate ProjectModel Factory();//autofac uses this
		public event EventHandler UpdateDisplay;


		public ProjectModel(BookSelection bookSelection)
		{
			_bookSelection = bookSelection;
			_bookSelection.SelectionChanged += new EventHandler(OnSelectionChanged);
		}

		public bool ShowEditPage
		{
			get { return _bookSelection.CurrentSelection.CanEdit; }
		}

		public bool ShowPublishPage
		{
			get { return _bookSelection.CurrentSelection.CanPublish; }
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

		public Book FindTemplate(string key)
		{
			return null;
		}
	}


}
