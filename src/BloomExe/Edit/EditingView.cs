using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ImageToolbox;
using Skybound.Gecko;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl
	{
		private readonly EditingModel _model;
		private PageListView _pageListView;
		private TemplatePagesView _templatePagesView;
		private readonly CutCommand _cutCommand;
		private readonly CopyCommand _copyCommand;
		private readonly PasteCommand _pasteCommand;
		private readonly UndoCommand _undoCommand;
		private readonly DeletePageCommand _deletePageCommand;
		private string _previousClickElementId;

		public delegate EditingView Factory();//autofac uses this


		public EditingView(EditingModel model, PageListView pageListView, TemplatePagesView templatePagesView,
			CutCommand cutCommand, CopyCommand copyCommand, PasteCommand pasteCommand, UndoCommand undoCommand, DeletePageCommand deletePageCommand)
		{
			_model = model;
			_pageListView = pageListView;
			_templatePagesView = templatePagesView;
			_cutCommand = cutCommand;
			_copyCommand = copyCommand;
			_pasteCommand = pasteCommand;
			_undoCommand = undoCommand;
			_deletePageCommand = deletePageCommand;
			InitializeComponent();
			_splitContainer1.Tag = _splitContainer1.SplitterDistance;//save it
			//don't let it grow automatically
//            _splitContainer1.SplitterMoved+= ((object sender, SplitterEventArgs e) => _splitContainer1.SplitterDistance = (int)_splitContainer1.Tag);
			SetupThumnailLists();
			_model.SetView(this);
			_browser1.SetEditingCommands(cutCommand, copyCommand,pasteCommand, undoCommand);
		}

		private void SetupThumnailLists()
		{
			_pageListView.Dock=DockStyle.Fill;
			_pageListView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			_templatePagesView.BackColor = _pageListView.BackColor = _splitContainer1.Panel1.BackColor;
			_splitContainer1.Panel1.Controls.Add(_pageListView);

			_templatePagesView.Dock = DockStyle.Fill;
			_templatePagesView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
		}


		private void SetTranslationPanelVisibility()
		{
			_splitContainer2.Panel2.Controls.Clear();
			_splitTemplateAndSource.Panel1.Controls.Clear();
			_splitTemplateAndSource.Panel2.Controls.Clear();

			if (_model.ShowTemplatePanel && _model.ShowTranslationPanel)    //BOTH
			{
				_splitTemplateAndSource.Panel1.Controls.Add(_templatePagesView);
				_splitTemplateAndSource.Panel2.Controls.Add(_translationSourcesControl);
				_splitContainer2.Panel2.Controls.Add(_splitTemplateAndSource);
			}
			else if (_model.ShowTranslationPanel)    //Translation only
			{
				_splitContainer2.Panel2.Controls.Add(_translationSourcesControl);
			}
			else                //Templates only
			{
				_splitContainer2.Panel2.Controls.Add(_templatePagesView);
			}
		}

		void VisibleNowAddSlowContents(object sender, EventArgs e)
		{
			Application.Idle -=new EventHandler(VisibleNowAddSlowContents);

			Cursor = Cursors.WaitCursor;
			_model.ActualVisibiltyChanged(true);
			Cursor = Cursors.Default;
		}

		protected override void OnVisibleChanged(EventArgs e)
		{
			base.OnVisibleChanged(e);

			if (Visible)
			{
				if(_model.GetBookHasChanged())
				{
					//now we're doing it based on the focus textarea: ShowOrHideSourcePane(_model.ShowTranslationPanel);
					SetTranslationPanelVisibility();
					//even before showing, we need to clear some things so the user doesn't see the old stuff
					_pageListView.Clear();
					_templatePagesView.Clear();
				}
				Application.Idle += new EventHandler(VisibleNowAddSlowContents);
				Cursor = Cursors.WaitCursor;
				UsageReporter.SendNavigationNotice("Editing");
			}
			else
			{
				_browser1.Navigate("about:blank", false);//so we don't see the old one for moment, the next time we open this tab
			}
		}

		public void UpdateSingleDisplayedPage(IPage page)
		{
		   if(!Visible)
		   {
			   return;
		   }

		   if (_model.HaveCurrentEditableBook)
		   {
			   _pageListView.SelectThumbnailWithoutSendingEvent(page);
				var dom = _model.GetXmlDocumentForCurrentPage();
			   _browser1.Focus();
			   _browser1.Navigate(dom);

		   }
		}

		public void UpdateTemplateList()
		{
			_templatePagesView.Update();
		}
		public void UpdatePageList()
		{
			_pageListView.SetBook(_model.CurrentBook);
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
		   // UpdateDisplay();
			var ge = e as GeckoDomEventArgs;
			if (ge.Target.TagName == "IMG")
				OnClickOnImage(ge);
			if (ge.Target.TagName.ToLower() == "textarea")
				OnClickTextArea(ge.Target);
		}

		private void OnClickTextArea(GeckoElement element)
		{
			//this might be too heavy-handed, but I added it to fix a bug
			//where two clicks would actually take the focus out of the text area:

			// was always true... as if gecko was making a new element each time
			//if(element!=_previousClickElement)
				if (element.Id != _previousClickElementId)
				{
				//todo: what about if they tab to it?
				_model.HandleUserEnteredArea(element);
			}
			_previousClickElementId = element.Id;
		}

		private void OnClickOnImage(GeckoDomEventArgs ge)
		{
			if (!_model.CanChangeImages())
				return;

			string currentPath = ge.Target.GetAttribute("src");
			var imageInfo = new PalasoImage();
			var existingImagePath = Path.Combine(_model.CurrentBook.FolderPath, currentPath);
			if (File.Exists(existingImagePath))
			{
				try
				{
					imageInfo = PalasoImage.FromFile(existingImagePath);
				}
				catch (Exception)
				{
					//todo: log this
				}
			};
			using(var dlg = new ImageToolboxDialog(imageInfo))
			{
				if(DialogResult.OK== dlg.ShowDialog())
				{
					// var path = MakePngOrJpgTempFileForImage(dlg.ImageInfo.Image);
					try
					{
						_model.ChangePicture(ge.Target.Id, dlg.ImageInfo);
					}
					catch(System.IO.IOException error)
					{
						Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, error.Message);
					}
					catch (ApplicationException error)
					{
						Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, error.Message);
					}
					catch (Exception error)
					{
						Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,"Bloom had a problem including that image");
					}
				}
			}
		}


//        private string MakePngOrJpgTempFileForImage(Image image)
//        {
//            var path = Path.GetTempFileName();
//            File.Delete(path);
//            string pathWithoutExtension = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
//            if (new[] { ImageFormat.Png, ImageFormat.Bmp, ImageFormat.Gif,ImageFormat.Tiff, ImageFormat.MemoryBmp}.Contains(image.RawFormat))
//            {
//                string filename = pathWithoutExtension + ".png";
//                image.Save(filename, ImageFormat.Png);
//                return filename;
//            }
//            if (new[] { ImageFormat.Jpeg }.Contains(image.RawFormat))
//            {
//                string filename = pathWithoutExtension + ".jpg";
//                image.Save(filename, ImageFormat.Jpeg);
//                return filename;
//            }
//            throw new ApplicationException("Bloom cannot handle this kind of image: "+image.RawFormat.ToString());
//        }

		public void SetSourceText(Dictionary<string, string> sourceTexts)
		{
			//null means hide it, empty list means just empty it
			//SetTranslationPanelVisibility(sourceTexts != null);
			if (sourceTexts != null)
				_translationSourcesControl.SetTexts(sourceTexts);
		}

		public void ClearSourceText()
		{
			_translationSourcesControl.ClearTextContents();
		}
		/// <summary>
		/// this started as an experiment, where our textareas were not being read when we saved because of the need
		/// to change the picture
		/// </summary>
		public void ReadEditableAreasNow()
		{
			_browser1.ReadEditableAreasNow();
		}

		private void _copyButton_Click(object sender, EventArgs e)
		{
			_copyCommand.Execute();
		}

		private void _pasteButton_Click(object sender, EventArgs e)
		{
			_pasteCommand.Execute();
		}

		public void UpdateDisplay()
		{
			_cutButton.Enabled = _cutCommand != null && _cutCommand.Enabled;
			_copyButton.Enabled = _copyCommand != null && _copyCommand.Enabled;
			_pasteButton.Enabled = _pasteCommand != null && _pasteCommand.Enabled;
			_undoButton.Enabled = _undoCommand != null && _undoCommand.Enabled;

			_deletePageButton.Enabled = _deletePageCommand.Enabled = _model.CanDeletePage;
		}

		private void _editButtonsUpdateTimer_Tick(object sender, EventArgs e)
		{
			UpdateDisplay();
		}

		private void _cutButton_Click(object sender, EventArgs e)
		{
			_cutCommand.Execute();
		}

		private void _undoButton_Click(object sender, EventArgs e)
		{
			_undoCommand.Execute();
		}

		private void _deletePageButton_Click(object sender, EventArgs e)
		{
			_deletePageCommand.Execute();
		}

	}
}