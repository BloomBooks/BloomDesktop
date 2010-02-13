using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl
	{
		private readonly EditingModel _model;

		public delegate EditingView Factory();//autofac uses this

		public EditingView(EditingModel model)
		{
			_model = model;
			InitializeComponent();
			model.CurrentBookChanged += new EventHandler(OnCurrentBookChanged);
		}

		void OnCurrentBookChanged(object sender, EventArgs e)
		{
			label1.Text = _model.CurrentBookName;
		}
	}
}