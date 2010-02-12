using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom
{
	public partial class LibraryView : UserControl
	{
		private readonly LibraryModel _model;

		public LibraryView(LibraryModel model)
		{
			_model = model;
			InitializeComponent();
		}
	}
}
