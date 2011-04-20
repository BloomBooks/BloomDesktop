using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
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
			Text = string.Format("{0} - Bloom {1}", _projectView.Text, GetVersionInfo());
			if(_projectSettings.IsShellMakingProject)
			{
				Text += " SHELL MAKING PROJECT";
			}
		}

		public static string GetVersionInfo()
		{
			var asm = Assembly.GetExecutingAssembly();
			var ver = asm.GetName().Version;
			var file = asm.CodeBase.Replace("file:", string.Empty);
			file = file.TrimStart('/');
			var fi = new FileInfo(file);

			return string.Format("Version {0}.{1}.{2} Built on {3}", ver.Major, ver.Minor,
				ver.Revision, fi.CreationTime.ToString("dd-MMM-yyyy"));
		}

		public bool UserWantsToOpenADifferentProject { get; set; }

	}
}
