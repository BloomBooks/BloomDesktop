using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Bloom.Project;

namespace Bloom
{
	public partial class Shell : Form
	{
		private readonly ProjectSettings _projectSettings;
		private readonly ProjectView _projectView;

		public Shell(ProjectView.Factory projectViewFactory, ProjectSettings projectSettings)
		{
			_projectSettings = projectSettings;
			InitializeComponent();

			_projectView = projectViewFactory();
			_projectView.CloseCurrentProject += ((x, y) =>
													{
														UserWantsToOpenADifferentProject = true;
														Close();
													});

			_projectView.BackColor =
				System.Drawing.Color.FromArgb(((int)(((byte)(64)))),
											  ((int)(((byte)(64)))),
											  ((int)(((byte)(64)))));
										_projectView.Dock = System.Windows.Forms.DockStyle.Fill;

			this.Controls.Add(this._projectView);

			SetWindowText();
		}

		private void SetWindowText()
		{
			var ver = Assembly.GetExecutingAssembly().GetName().Version;
			Text = string.Format("{0} - Bloom {1}.{2}.{3}", _projectView.Text, ver.Major, ver.Minor, ver.Build);
			if(_projectSettings.IsShellMakingProject)
			{
				Text += " SHELL MAKING PROJECT";
			}
		}

		public bool UserWantsToOpenADifferentProject { get; set; }

	}
}
