using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Project;

namespace Bloom
{
	public partial class Shell : Form
	{
		private readonly ProjectView _projectView;

		public Shell(ProjectView.Factory projectViewFactory)
		{
			InitializeComponent();

			_projectView = projectViewFactory();

			_projectView.BackColor =
				System.Drawing.Color.FromArgb(((int)(((byte)(64)))),
											  ((int)(((byte)(64)))),
											  ((int)(((byte)(64)))));
										_projectView.Dock = System.Windows.Forms.DockStyle.Fill;

			this.Controls.Add(this._projectView);
		}
	}
}
