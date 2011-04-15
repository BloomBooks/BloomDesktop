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
using System.Xml;
using Bloom.Publish;
using Palaso.Xml;
using Skybound.Gecko;

namespace Bloom
{
	public partial class Form1 : Form
	{
		private EditPageControl _editControl;
		private PageControl _previewPage;

		public Form1()
		{
			InitializeComponent();
			var programfiles = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles);
			Skybound.Gecko.Xpcom.Initialize(Path.Combine(programfiles,"Mozilla Firefox")); //@"%ProgramFiles%\Mozilla Firefox");

			Directory.CreateDirectory(ProjectDirectory);
			var bookName = "Lucy'sBigDay";
			CopyBookToProjectDir(Path.Combine(FactoryTemplatesDirectory,"A4LandscapeBooklet"), bookName);


			var path = Path.Combine(ProjectDirectory, Path.Combine(bookName, bookName+".htm"));
			_previewPage = new PageControl(false);
			_previewPage.Dock= DockStyle.Fill;
			_previewPage.Name = "preview";
			htmlPreviewPage.Controls.Add(_previewPage);
			_previewPage.DocumentPath = path;
			_previewPage.AddStyleSheet(Path.Combine(FactoryTemplatesDirectory, "previewMode.css"));

			_editControl = new EditPageControl();
			_editControl.Name = "edit";
			_editControl.Dock = DockStyle.Fill;
			editPage.Controls.Add(_editControl);
			tabControl1.Selecting += new TabControlCancelEventHandler(tabControl1_Selecting);
			_editControl.DocumentPath = path;
			_editControl.AddStyleSheet(Path.Combine(FactoryTemplatesDirectory, "editMode.css"));
//
//            var b = new PdfView();
//            b.Dock = DockStyle.Fill;
//            b.DocumentPath= path;
//            pdfPage.Controls.Add(b);

			LoadDocumentThumbnails(path);
		}

		private void LoadDocumentThumbnails(string path)
		{
			_documentThumnails.Items.Clear();

			int pageNumber = 0;
			foreach (XmlNode page in GetElementsFromFile(path, "//x:div[contains(@class,'-bloom-page')]"))
			{
				var id = page.GetStringAttribute("id");
				var item = new ListViewItem(string.Format("Page {0} [{1}]", pageNumber, id));
				item.ImageIndex = 0;
				item.Tag = id;
				_documentThumnails.Items.Add(item);
				pageNumber++;
			}
		}

		public XmlNodeList GetElementsFromFile(string path, string queryWithXForNamespace)
		{
			XmlDocument dom = new XmlDocument();
			dom.Load(path);
			XmlNamespaceManager namespaceManager = new XmlNamespaceManager(dom.NameTable);
			namespaceManager.AddNamespace("x", "http://www.w3.org/1999/xhtml");
			return dom.SafeSelectNodes(queryWithXForNamespace, namespaceManager);
		}


		void tabControl1_Selecting(object sender, TabControlCancelEventArgs e)
		{
			if(e.TabPage!=editPage)
			{
				_previewPage.RefreshContents();
			}
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

		private void _saveButton_Click(object sender, EventArgs e)
		{
			_editControl.SaveHtml();
		}

		private void _documentThumnails_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_documentThumnails.SelectedItems.Count > 0)
			{
				_editControl.ShowPage(_documentThumnails.SelectedItems[0].Tag as string);
			}
		}



	}
}
