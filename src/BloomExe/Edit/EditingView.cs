using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Xml;
using Palaso.UI.WindowsForms.ImageToolbox;
using Skybound.Gecko;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl
	{
		private readonly EditingModel _model;
		private bool _updatePending;
		private PageListView _pageListView;
		private TemplatePagesView _templatePagesView;
		public delegate EditingView Factory();//autofac uses this


		public EditingView(EditingModel model, PageListView pageListView, TemplatePagesView templatePagesView)
		{
			_model = model;
			_pageListView = pageListView;
			_templatePagesView = templatePagesView;
			InitializeComponent();
			model.UpdateDisplay += new EventHandler(OnUpdateDisplay);
			model.UpdatePageList += new EventHandler((s,e)=>_pageListView.SetBook(_model.CurrentBook));
			splitContainer1.Tag = splitContainer1.SplitterDistance;//save it
			//don't let it grow automatically
			splitContainer1.SplitterMoved+= ((object sender, SplitterEventArgs e) => splitContainer1.SplitterDistance = (int)splitContainer1.Tag);

			_pageListView.Dock=DockStyle.Fill;
			_pageListView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		   _templatePagesView.BackColor = _pageListView.BackColor = splitContainer1.Panel1.BackColor;
			splitContainer1.Panel1.Controls.Add(_pageListView);

			_templatePagesView.Dock = DockStyle.Fill;
			_templatePagesView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			splitContainer2.Panel2.Controls.Add(_templatePagesView);


		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			OnUpdateDisplay(this,null);
		}

		protected override void OnVisibleChanged(EventArgs e)
		{
			base.OnVisibleChanged(e);
			if(_updatePending)
			{
				OnUpdateDisplay(this,null);
			}
		}
		void OnUpdateDisplay(object sender, EventArgs e)
		{
		   if(!Visible)
		   {
			   _updatePending = true;
			   return;
		   }
		   _updatePending = false;
		   if (_model.HaveCurrentEditableBook)
		   {
			var dom = _model.GetXmlDocumentForCurrentPage();
			_browser1.Navigate(dom);
		   }
		}



		private void _browser1_Validating(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (_model.HaveCurrentEditableBook)
			{
				_model.SaveNow();
			}
		}


		private void _browser1_OnBrowserClick(object sender, EventArgs e)
		{
			var ge = e as GeckoDomEventArgs;
			if (ge.Target.TagName != "IMG")
				return;
			string currentPath = ge.Target.GetAttribute("src");
			var imageInfo = new PalasoImage() { Image = Image.FromFile(Path.Combine(_model.CurrentBook.FolderPath, currentPath)) };
			using(var dlg = new ImageToolboxDialog(imageInfo))
			{
				if(DialogResult.OK== dlg.ShowDialog())
				{
					var path = MakeTempFileForImage(dlg.ImageInfo.Image);
					_model.ChangePicture(ge.Target.Id, path);
					File.Delete(path);
				}
			}
		}

		private string MakeTempFileForImage(Image image)
		{
			var path = Path.GetTempFileName();
			File.Delete(path);
			if (new[] { ImageFormat.Png, ImageFormat.Bmp, ImageFormat.Gif,ImageFormat.Tiff, ImageFormat.MemoryBmp}.Contains(image.RawFormat))
			{
				string filename = path + ".png";
				image.Save(filename, ImageFormat.Png);
				return filename;
			}
			if (new[] { ImageFormat.Jpeg }.Contains(image.RawFormat))
			{
				string filename = path + ".jpg";
				image.Save(filename, ImageFormat.Jpeg);
				return filename;
			}
			throw new ApplicationException("That image format is not supported by the Palaso Image Library: "+image.RawFormat.ToString());
		}
	}
}