using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.Properties;
using Bloom.ToPalaso;
using Bloom.ToPalaso.Experimental;
using BloomTemp;
using Newtonsoft.Json.Linq;
using Palaso.Extensions;
using Palaso.IO;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.UI.WindowsForms.ImageToolbox;
using Gecko;
using TempFile = Palaso.IO.TempFile;

namespace Bloom.Edit
{
	public partial class EditingView : UserControl, IBloomTabArea
	{
		private readonly EditingModel _model;
		private PageListView _pageListView;
		private TemplatePagesView _templatePagesView;
		private readonly CutCommand _cutCommand;
		private readonly CopyCommand _copyCommand;
		private readonly PasteCommand _pasteCommand;
		private readonly UndoCommand _undoCommand;
		private readonly DeletePageCommand _deletePageCommand;
		private GeckoElement _previousClickElement;
		private Action _pendingMessageHandler;
		private bool _updatingDisplay;
		private Color _enabledToolbarColor = Color.FromArgb(49, 32, 46);
		private Color _disabledToolbarColor= Color.FromArgb(114,74,106);
		private bool _visible;

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

			_browser1.GeckoReady+=new EventHandler(OnGeckoReady);



			_menusToolStrip.Renderer = new FixedToolStripRenderer();

			//we're giving it to the parent control through the TopBarControls property
			Controls.Remove(_topBarPanel);
		}

#if TooExpensive
		void OnBrowserFocusChanged(object sender, GeckoDomEventArgs e)
		{
			//prevent recursion
			_browser1.WebBrowser.DomFocus -= new EventHandler<GeckoDomEventArgs>(OnBrowserFocusChanged);
			_model.BrowserFocusChanged();
			_browser1.WebBrowser.DomFocus += new EventHandler<GeckoDomEventArgs>(OnBrowserFocusChanged);

		}
#endif

		public Control TopBarControl
		{
			get
			{
				return _topBarPanel;
			}
		}


		public class FixedToolStripRenderer : ToolStripSystemRenderer
		{
			protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
			{
				//just don't draw a boarder
			}
		}

		void ParentForm_Activated(object sender, EventArgs e)
		{
			if (!_visible) //else you get a totally non-responsive Bloom, if you come back to a Bloom that isn't in the Edit tab
				return;

			Debug.WriteLine("EditingView.ParentForm_Activated(): Selecting Browser");
//			Debug.WriteLine("browser focus: "+ (_browser1.Focused ? "true": "false"));
//			Debug.WriteLine("active control: " + ActiveControl.Name);
//			Debug.WriteLine("split container's control: " + _splitContainer1.ActiveControl.Name);
//			Debug.WriteLine("_splitContainer1.ContainsFocus: " + (_splitContainer1.ContainsFocus ? "true" : "false"));
//			Debug.WriteLine("_splitContainer2.ContainsFocus: " + (_splitContainer2.ContainsFocus ? "true" : "false"));
//			Debug.WriteLine("_browser.ContainsFocus: " + (_browser1.ContainsFocus ? "true" : "false"));
//			//focus() made it worse, select has no effect

			/* These two lines are the result of several hours of work. The problem this solves is that when
			 * you're switching between applications (e.g., building a shell book), the browser would highlight
			 * the box you were in, but not really focus on it. So no red border (from the css :focus), and typing/pasting
			 * was erratic.
			 * So now, when we come back to Bloom (this activated event), we *deselect* the browser, then reselect it, and it's happy.
			 */

			_splitContainer1.Select();
			//_browser1.Select();
			_browser1.WebBrowser.Select();
		}


		private void _handleMessageTimer_Tick(object sender, EventArgs e)
		{
			_handleMessageTimer.Enabled = false;
			_pendingMessageHandler();
			_pendingMessageHandler = null;
		}

		private void OnGeckoReady(object sender, EventArgs e)
		{
#if TooExpensive
			_browser1.WebBrowser.DomFocus += new EventHandler<GeckoDomEventArgs>(OnBrowserFocusChanged);
#endif
		}

		private void OnShowBookMetadataEditor()
		{
			try
			{
				if(!_model.CanEditCopyrightAndLicense)
				{
					MessageBox.Show("Sorry, the copyright and license for this book cannot be changed.");
					return;
				}

				_model.SaveNow();//in case we were in this dialog already and made changes, which haven't found their way out to the Book yet
				Metadata metadata = _model.CurrentBook.GetLicenseMetadata();

				Logger.WriteEvent("Showing Metadata Editor Dialog");
				using (var dlg = new Palaso.UI.WindowsForms.ClearShare.WinFormsUI.MetadataEditorDialog(metadata))
				{
					dlg.ShowCreator = false;
					if (DialogResult.OK == dlg.ShowDialog())
					{
						string imagePath = _model.CurrentBook.FolderPath.CombineForPath("license.png");
						if (File.Exists(imagePath))
							File.Delete(imagePath);
						Image licenseImage = dlg.Metadata.License.GetImage();
						if (licenseImage != null)
						{
							licenseImage.Save(imagePath);
						}
						else if (File.Exists(imagePath))
						{
							File.Delete(imagePath);
						}

						//NB: we are mapping "RightsStatement" (which comes from XMP-dc:Rights) to "LicenseNotes" in the html.
						//note that the only way currently to recognize a custom license is that RightsStatement is non-empty while description is emtpy
						string rights = dlg.Metadata.License.RightsStatement==null ? string.Empty : dlg.Metadata.License.RightsStatement.Replace("'", "\\'");
						string description = dlg.Metadata.License.GetDescription("en") == null ? string.Empty : dlg.Metadata.License.GetDescription("en").Replace("'", "\\'");
						string licenseImageName = licenseImage==null? string.Empty: "license.png";
						string result =
							string.Format(
								"{{ copyright: '{0}', licenseImage: '{1}', licenseUrl: '{2}',  licenseNotes: '{3}', licenseDescription: '{4}' }}",
								dlg.Metadata.CopyrightNotice.Replace("'","\\'"),
								licenseImageName,
								dlg.Metadata.License.Url, rights, description);
						_browser1.RunJavaScript("SetCopyrightAndLicense(" + result + ")");

						//ok, so the the dom for *that page* is updated, but if the page doesn't display some of those values, they won't get
						//back to the data div in the actual html file even when the page is read and saved, because individual pages don't
						//have the data div.
						_model.CurrentBook.UpdateLicenseMetdata(dlg.Metadata);
						_model.SaveNow();
						_model.RefreshDisplayOfCurrentPage();//the cleanup() that is part of Save removes qtips, so let' redraw everything
					}
				}
				Logger.WriteMinorEvent("Emerged from Metadata Editor Dialog");
			}
			catch (Exception error)
			{
#if DEBUG
				throw;
#endif
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "There was a problem recording your changes to the copyright and license.");
			}
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

			if (_model.ShowTemplatePanel)        //Templates only
			{
				_splitContainer2.Panel2Collapsed = false;
				_splitContainer2.Panel2.Controls.Add(_templatePagesView);
			}
			else
			{
				_splitContainer2.Panel2Collapsed = true;
			}
		}

	   void VisibleNowAddSlowContents(object sender, EventArgs e)
		{
		   //TODO: this is causing green boxes when you quit while it is still working
		   //we should change this to a proper background task, with good
		   //cancellation in case we switch documents.  Note we may also switch
		   //to some other way of making the thumbnails... e.g. it would be nice
		   //to have instant placeholders, with thumbnails later.

			Application.Idle -= new EventHandler(VisibleNowAddSlowContents);

			CheckFontAvailablility();

			Cursor = Cursors.WaitCursor;
			_model.ViewVisibleNowDoSlowStuff();
			Cursor = Cursors.Default;
		}

		private void CheckFontAvailablility()
		{
			var fontMessage = _model.GetFontAvailabilityMessage();
			if(!string.IsNullOrEmpty(fontMessage))
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(),
																 fontMessage);
			}
		}


		/// <summary>
	   /// this is called by our model, as a result of a "SelectedTabChangedEvent". So it's a lot more reliable than the normal winforms one.
		/// </summary>
		public void OnVisibleChanged(bool visible)
		{
			_visible = visible;
			if (visible)
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
				Logger.WriteEvent("Entered Edit Tab");
				UsageReporter.SendNavigationNotice("Entered Edit Tab");
			}
			else
			{
				Application.Idle -= new EventHandler(VisibleNowAddSlowContents);//make sure
				_browser1.Navigate("about:blank", false);//so we don't see the old one for moment, the next time we open this tab
			}
		}

		public void UpdateSingleDisplayedPage(IPage page)
		{
			if (!_model.Visible)
			{
				return;
			}

			if (_model.HaveCurrentEditableBook)
			{
				_pageListView.SelectThumbnailWithoutSendingEvent(page);
				var dom = _model.GetXmlDocumentForCurrentPage();
				_browser1.Focus();
				_browser1.Navigate(dom);
				_pageListView.Focus();
				_browser1.Focus();
			}
			UpdateDisplay();
		}

		public void UpdateTemplateList()
		{
			_templatePagesView.Update();
		}
		public void UpdatePageList(bool emptyThumbnailCache)
		{
			if (emptyThumbnailCache)
				_pageListView.EmptyThumbnailCache();
			_pageListView.SetBook(_model.CurrentBook);
		}

		private void _browser1_OnBrowserClick(object sender, EventArgs e)
		{
			var ge = e as GeckoDomEventArgs;
			if (ge.Target == null)
				return;//I've seen this happen

			if (ge.Target.ClassName.Contains("sourceTextTab"))
			{
				RememberSourceTabChoice(ge.Target);
				return;
			}
			if (ge.Target.ClassName.Contains("changeImageButton"))
				OnChangeImage(ge);
			if (ge.Target.ClassName.Contains("pasteImageButton"))
				OnPasteImage(ge);
			if (ge.Target.ClassName.Contains("editMetadataButton"))
				OnEditImageMetdata(ge);

			var anchor = ge.Target as Gecko.DOM.GeckoAnchorElement;
			if(anchor!=null)
			{
				if(anchor.Href.Contains("bookMetadataEditor"))
				{
					OnShowBookMetadataEditor();
					ge.Handled = true;
					return;
				}
				if (anchor.Href.Contains("("))//tied to, for example,  data-functionOnHintClick="ShowTopicChooser()"
				{
					var startOfFunctionName = anchor.Href.LastIndexOf("/")+1;
					var function = anchor.Href.Substring(startOfFunctionName, anchor.Href.Length - startOfFunctionName);
					_browser1.RunJavaScript(function);
					ge.Handled = true;
					return;
				}
				if(anchor.Href.ToLower().StartsWith("http"))//will cover https also
				{
					Process.Start(anchor.Href);
					ge.Handled = true;
					return;
				}
				if (anchor.Href.ToLower().StartsWith("file"))//source bubble tabs
				{
					ge.Handled = false;//let gecko handle it
					return;
				}
				else
				{
					ErrorReport.NotifyUserOfProblem("Bloom did not understand this link: " + anchor.Href);
					ge.Handled = true;
				}

			}
//			if (ge.Target.ClassName.Contains("bloom-metaData") || (ge.Target.ParentElement!=null && ge.Target.ParentElement.ClassName.Contains("bloom-metaData")))
		}


		private void RememberSourceTabChoice(GeckoElement target)
		{
			//"<a class="sourceTextTab" href="#tpi">Tok Pisin</a>"
			var start = 1+ target.OuterHtml.IndexOf("#");
			var end = target.OuterHtml.IndexOf("\">");
			Settings.Default.LastSourceLanguageViewed = target.OuterHtml.Substring(start, end - start);
		}


		private void OnEditImageMetdata(GeckoDomEventArgs ge)
		{
			var imageElement = GetImageNode(ge);
			if (imageElement == null)
				return;
			string fileName = imageElement.GetAttribute("src").Replace("%20", " ");

			var path = Path.Combine(_model.CurrentBook.FolderPath, fileName);
			using (var imageInfo = PalasoImage.FromFile(path))
			{
				bool looksOfficial = imageInfo.Metadata!=null && !string.IsNullOrEmpty(imageInfo.Metadata.CollectionUri);
				if(looksOfficial)
				{
					MessageBox.Show(imageInfo.Metadata.GetSummaryParagraph("en"));
					return;
				}
				Logger.WriteEvent("Showing Metadata Editor For Image");
				using (var dlg = new Palaso.UI.WindowsForms.ClearShare.WinFormsUI.MetadataEditorDialog(imageInfo.Metadata))
				{
					if (DialogResult.OK == dlg.ShowDialog())
					{
						imageInfo.Metadata = dlg.Metadata;
						imageInfo.SaveUpdatedMetadataIfItMakesSense();
						imageInfo.Metadata.StoreAsExemplar(Metadata.FileCategory.Image);
						//update so any overlays on the image are brough up to data
						var editor = new PageEditingModel();
						editor.UpdateMetdataAttributesOnImgElement(imageElement, imageInfo);

						var answer = MessageBox.Show("Copy this information to all other pictures in this book?", "Picture Intellectual Proprty Information", MessageBoxButtons.YesNo);
						if(answer == DialogResult.Yes)
						{
							Cursor = Cursors.WaitCursor;
							try
							{
								_model.CopyImageMetadataToWholeBook(dlg.Metadata);
							}
							catch (Exception e)
							{
								ErrorReport.NotifyUserOfProblem(e, "There was a problem copying the metadata to all the images.");
							}
							Cursor = Cursors.Default;
						}
					}
				}
			}

			//_model.SaveNow();
			//doesn't work: _browser1.WebBrowser.Reload();
		}

		private void OnPasteImage(GeckoDomEventArgs ge)
		{
			if (!_model.CanChangeImages())
			{
				MessageBox.Show(
					"Sorry, this book is locked down as shell. If you need to make changes to the pictures, create a library for the purposes of editing shells, and drag the book folder in there. Images will then be changeable.");
				return;
			}

			Image clipboardImage = null;
			try
			{
				clipboardImage = GetImageFromClipboard();
				if (clipboardImage == null)
				{
					MessageBox.Show("Before you can paste an image, copy one onto your 'clipboard', from another program.");
					return;
				}

				if (ge.Target.ClassName.Contains("licenseImage"))
					return;

				var imageElement = GetImageNode(ge);
				if (imageElement == null)
					return;
				Cursor = Cursors.WaitCursor;

				//nb: later, code closer to the the actual book folder will
				//improve this file name
				using (var temp = new TempFile())
				{
					clipboardImage.Save(temp.Path, ImageFormat.Png);
//                    using (var progressDialog = new ProgressDialogBackground())
//                    {
//                        progressDialog.ShowAndDoWork((progress, args) =>
//                                                         {
//                                                             ImageUpdater.CompressImage(temp.Path, progress);
//                                                         });
//                    }
					using (var palasoImage = PalasoImage.FromFile(temp.Path))
					{
						_model.ChangePicture(imageElement, palasoImage, new NullProgress());
					}
				}
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "The program had trouble getting an image from the clipboard.");
			}
			finally
			{
				if (clipboardImage != null)
					clipboardImage.Dispose();
			}
			Cursor = Cursors.Default;
		}



		private Image GetImageFromClipboard()
		{
			if(Clipboard.ContainsImage())
				return Clipboard.GetImage();

			var dataObject = Clipboard.GetDataObject();
			if(dataObject==null)
			   return null;

		   //People can do a "copy" from the WIndows Photo Viewer but what it puts on the clipboard is a path, not an image
			if (dataObject.GetDataPresent(DataFormats.FileDrop))
			{
				//This line gets all the file paths that were selected in explorer
				string[] files = dataObject.GetData(DataFormats.FileDrop) as string[];
				//Get the name of the file. This line only gets the first file name if many file were selected in explorer
				string path = files[0];

				try
				{
					return Image.FromFile(path);
				}
				catch (Exception)
				{
					return null;//not an image
				}
			}
			return null;
		}


		private static GeckoElement GetImageNode(GeckoDomEventArgs ge)
		{
			GeckoElement imageElement = null;
			foreach (var n in ge.Target.Parent.ChildNodes)
			{
				if (n is GeckoElement && ((GeckoElement) n).TagName.ToLower() == "img")
				{
					imageElement = (GeckoElement) n;
					break;
				}
			}

			if (imageElement == null)
			{
				Debug.Fail("Could not find image element");
				return null;
			}
			return imageElement;
		}

		private void OnChangeImage(GeckoDomEventArgs ge)
		{
			var imageElement = GetImageNode(ge);
			if (imageElement == null)
				return;
			 string currentPath = imageElement.GetAttribute("src").Replace("%20", " ");

			//TODO: this would let them set it once without us bugging them, but after that if they
			//go to change it, we would bug them because we don't have a way of knowing that it was a placeholder before.
			if (!currentPath.ToLower().Contains("placeholder")  //always alow them to put in something over a placeholder
				&& !_model.CanChangeImages())
			{
				if(DialogResult.Cancel== MessageBox.Show("This book is locked down as shell. Are you sure you want to change the picture?","Change Image",MessageBoxButtons.OKCancel))
				{
					return;
				}
			}
			if (ge.Target.ClassName.Contains("licenseImage"))
				return;

			Cursor = Cursors.WaitCursor;

			var imageInfo = new PalasoImage();
			var existingImagePath = Path.Combine(_model.CurrentBook.FolderPath, currentPath);

			//don't send the placeholder to the imagetoolbox... we get a better user experience if we admit we don't have an image yet.
			if (!currentPath.ToLower().Contains("placeholder") && File.Exists(existingImagePath))
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
			Logger.WriteEvent("Showing ImageToolboxDialog Editor Dialog");
			using(var dlg = new ImageToolboxDialog(imageInfo, null))
			{
				if(DialogResult.OK== dlg.ShowDialog())
				{
					// var path = MakePngOrJpgTempFileForImage(dlg.ImageInfo.Image);
					try
					{
						 _model.ChangePicture(imageElement, dlg.ImageInfo, new NullProgress());
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
			Logger.WriteMinorEvent("Emerged from ImageToolboxDialog Editor Dialog");
			Cursor = Cursors.Default;
		}

		public void UpdateThumbnailAsync(IPage page)
		{
			_pageListView.UpdateThumbnailAsync(page);
		}

		/// <summary>
		/// this started as an experiment, where our textareas were not being read when we saved because of the need
		/// to change the picture
		/// </summary>
		public void CleanHtmlAndCopyToPageDom()
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
			try
			{
				_updatingDisplay = true;

				_contentLanguagesDropdown.DropDownItems.Clear();
				foreach (var l in _model.ContentLanguages)
				{
					ToolStripMenuItem item = (ToolStripMenuItem) _contentLanguagesDropdown.DropDownItems.Add(l.ToString());
					item.Tag = l;
					item.Enabled = !l.Locked;
					item.Checked = l.Selected;
					item.CheckOnClick = true;
					item.CheckedChanged += new EventHandler(OnContentLanguageDropdownItem_CheckedChanged);
				}

				_layoutChoices.DropDownItems.Clear();
				var layout = _model.GetCurrentLayout();
				var layoutChoices = _model.GetLayoutChoices();
				foreach (var l in layoutChoices)
				{
					ToolStripMenuItem item = (ToolStripMenuItem) _layoutChoices.DropDownItems.Add(l.ToString());
					item.Tag = l;
					//we don't allow the split options here
					if(l.ElementDistribution == Book.Layout.ElementDistributionChoices.SplitAcrossPages)
					{
						item.Enabled = false;
						item.ToolTipText = "This option is only available in the Publish tab.";
					}
					item.Text = l.ToString();
					item.Click += new EventHandler(OnPaperSizeAndOrientationMenuClick);
				}

				if(layoutChoices.Count()<2)
				{
					ToolStripMenuItem item = (ToolStripMenuItem)_layoutChoices.DropDownItems.Add("There are no other layout options for this template.");
					item.Tag = null;
					item.Enabled = false;
				}

				_layoutChoices.Text = layout.ToString();

				switch (_model.NumberOfDisplayedLanguages)
				{
					case 1:
						_contentLanguagesDropdown.Text = "One Language";
						break;
					case 2:
						_contentLanguagesDropdown.Text = "Two Languages";
						break;
					case 3:
						_contentLanguagesDropdown.Text = "Three Languages";
						break;
				}
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "There was a problem updating the edit display.");
			}
			finally
			{
				_updatingDisplay = false;
			}
		}

		void OnPaperSizeAndOrientationMenuClick(object sender, EventArgs e)
		{
			var item = (ToolStripMenuItem)sender;
			_model.SetLayout((Layout)item.Tag);
			UpdateDisplay();
		}

		void OnContentLanguageDropdownItem_CheckedChanged(object sender, EventArgs e)
		{
			if(_updatingDisplay)
				return;
			var item = (ToolStripMenuItem) sender;
			((EditingModel.ContentLanguage)item.Tag).Selected = item.Checked;

			_model.ContentLanguagesSelectionChanged();
		}

		public void UpdateEditButtons()
		{
			UpdateButtonEnabled(_cutButton, _cutCommand);
			UpdateButtonEnabled(_copyButton, _copyCommand);
			UpdateButtonEnabled(_pasteButton,_pasteCommand);
			UpdateButtonEnabled(_undoButton, _undoCommand);
			UpdateButtonEnabled(_deletePageButton, _deletePageCommand);
		}

		private void UpdateButtonEnabled(Button button, Command command)
		{
			button.Enabled = command != null && command.Enabled;
			//doesn't work becuase the forecolor is ignored when disabled...
			button.ForeColor = button.Enabled ? _enabledToolbarColor : _disabledToolbarColor;//.DimGray;
			button.Invalidate();
		}

		private void _editButtonsUpdateTimer_Tick(object sender, EventArgs e)
		{
			UpdateEditButtons();
		}

		private void _cutButton_Click(object sender, EventArgs e)
		{
			_cutCommand.Execute();
		}

		private void _undoButton_Click(object sender, EventArgs e)
		{
			_undoCommand.Execute();
		}

		public void ClearOutDisplay()
		{
			_pageListView.Clear();
			_browser1.Navigate("about:blank",false);
		}

		private void _deletePageButton_Click_1(object sender, EventArgs e)
		{
			if(ConfirmRemovePageDialog.Confirm())
			{
				_deletePageCommand.Execute();
			}
		}

		private void EditingView_Load(object sender, EventArgs e)
		{
			ParentForm.Activated += new EventHandler(ParentForm_Activated);
		}

		public string HelpTopicUrl
		{
			get { return "/Tasks/Edit_tasks/Edit_tasks_overview.htm"; }
		}
	}
}