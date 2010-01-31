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
using Skybound.Gecko;

namespace BloomApp
{
	public partial class Form1 : Form
	{
		private GeckoWebBrowser _browser;

		public Form1()
		{
			InitializeComponent();
			var programfiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
			Skybound.Gecko.Xpcom.Initialize(Path.Combine(programfiles,"Mozilla Firefox")); //@"%ProgramFiles%\Mozilla Firefox");

			Directory.CreateDirectory(ProjectDirectory);
			var bookName = "Lucy'sBigDay";
			CopyBookToProjectDir(Path.Combine(FactoryTemplatesDirectory,"A4Portrait"), bookName);


			var path = Path.Combine(ProjectDirectory, Path.Combine(bookName, bookName+".htm"));
			var control = new PageControl();
			control.Dock= DockStyle.Fill;
			htmlPreviewPage.Controls.Add(control);
			control.DocumentPath = path;
			control.AddStyleSheet(Path.Combine(FactoryTemplatesDirectory, "previewMode.css"));

			control = new PageControl();
			control.Dock = DockStyle.Fill;
			editPage.Controls.Add(control);
			control.DocumentPath = path;
			control.AddStyleSheet(Path.Combine(FactoryTemplatesDirectory, "editMode.css"));
		}

		private void CopyBookToProjectDir(string templateFolderPath, string bookName)
		{
			string targetDir = Path.Combine(ProjectDirectory, bookName);
			Directory.CreateDirectory(targetDir);
			//string sourceDir = Path.Combine(FactoryTemplatesDirectory, bookName);

			foreach (string source in
				Directory.GetFiles(templateFolderPath, "*.*"))
			{
				var fileName = Path.GetFileName(source).Replace("starter", bookName);
				string target = Path.Combine(targetDir, fileName);
				File.Copy(source, target, true);
			}
		}


		public static string FactoryTemplatesDirectory
		{
			get { return Path.Combine(GetTopAppDirectory(), "templates"); }
		}

		public static string ProjectDirectory
		{
			get { return Path.Combine(GetTopAppDirectory(), "userProject"); }
		}

		protected static string GetTopAppDirectory()
		{
			string path = DirectoryOfTheApplicationExecutable;
			char sep = Path.DirectorySeparatorChar;
			int i = path.ToLower().LastIndexOf(sep + "output" + sep);

			if (i > -1)
			{
				path = path.Substring(0, i + 1);
			}
			return path;
		}

		public static string DirectoryOfTheApplicationExecutable
		{
			get
			{
				string path;
				bool unitTesting = Assembly.GetEntryAssembly() == null;
				if (unitTesting)
				{
					path = new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath;
					path = Uri.UnescapeDataString(path);
				}
				else
				{
					path = Application.ExecutablePath;
				}
				return Directory.GetParent(path).FullName;
			}
		}


	}
}
