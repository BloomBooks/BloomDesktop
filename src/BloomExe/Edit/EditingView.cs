//#define MEMORYCHECK
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.CollectionTab;
using Bloom.ErrorReporter;
using Bloom.ImageProcessing;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Utils;
using Bloom.web;
using Bloom.web.controllers;
using Bloom.Workspace;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.ImageToolbox.ImageGallery;
using SIL.Windows.Forms.Miscellaneous;
using SIL.Windows.Forms.Widgets;
using TempFile = SIL.IO.TempFile;

namespace Bloom.Edit
{
    public partial class EditingView : UserControl, IBloomTabArea, IZoomManager
    {
        private readonly EditingModel _model;
        private PageListController _pageListView;
        private ContextMenuStrip _contentLanguagesDropdown = new();
        private ContextMenuStrip _layoutChoicesDropdown = new();
        private readonly CutCommand _cutCommand;
        private readonly CopyCommand _copyCommand;
        private readonly PasteCommand _pasteCommand;
        private readonly UndoCommand _undoCommand;
        private readonly SignLanguageApi _signLanguageApi;
        private readonly CopyrightAndLicenseApi _copyrightAndLicenseApi;
        private Color _enabledToolbarColor = Palette.DarkTextAgainstBackgroundColor;
        private Color _disabledToolbarColor = Color.FromArgb(114, 74, 106);
        private bool _visible;
        private BloomWebSocketServer _webSocketServer;
        private ZoomModel _zoomModel;
        private PageListApi _pageListApi;
        private DateTime? _lastTopBarMenuClosedTime;

        public delegate EditingView Factory(); //autofac uses this

        public EditingView(
            EditingModel model,
            PageListController pageListView,
            CutCommand cutCommand,
            CopyCommand copyCommand,
            PasteCommand pasteCommand,
            UndoCommand undoCommand,
            ControlKeyEvent controlKeyEvent,
            SignLanguageApi signLanguageApi,
            CommonApi commonApi,
            EditingViewApi editingViewApi,
            PageListApi pageListApi,
            BookRenamedEvent bookRenamedEvent,
            CopyrightAndLicenseApi copyrightAndLicenseApi,
            LocalizationChangedEvent localizationChangedEvent
        )
        {
            _model = model;
            _pageListView = pageListView;
            _cutCommand = cutCommand;
            _copyCommand = copyCommand;
            _pasteCommand = pasteCommand;
            _undoCommand = undoCommand;
            _webSocketServer = model.EditModelSocketServer;
            _pageListApi = pageListApi;
            InitializeComponent();

            // This used to be part of InitializeComponent, but we want to make which browser to use
            // configurable. It can possibly move back to the Designer code once we settle on WebView2.
            // Turning off for this PR because it's not working well enough yet.
            if (!Program.RunningHarvesterMode)
            {
                _browser1 = BrowserMaker.MakeBrowser();
                _browser1.BackColor = System.Drawing.Color.DarkGray;
                _browser1.ContextMenuProvider = null;
                _browser1.ReplaceContextMenu = null;
                _browser1.ControlKeyEvent = null;
                _browser1.Dock = System.Windows.Forms.DockStyle.Fill;
                _browser1.Location = new System.Drawing.Point(0, 0);
                _browser1.Margin = new System.Windows.Forms.Padding(5);
                _browser1.Name = "_browser1";
                _browser1.Size = new System.Drawing.Size(826, 561);
                _browser1.TabIndex = 1;
                Controls.Add(_browser1);
            }

            //SetupThumbnailLists();
            _model.SetView(this);
            // We will need to handle this in another way if we ever have multiple projects open and thus
            // multiple models and views.
            _signLanguageApi = signLanguageApi;
            signLanguageApi.Model = _model;
            signLanguageApi.View = this;
            editingViewApi.View = this;
            commonApi.Model = _model;
            _copyrightAndLicenseApi = copyrightAndLicenseApi;
            copyrightAndLicenseApi.Model = _model;
            copyrightAndLicenseApi.View = this;
            if (!Program.RunningHarvesterMode)
            {
                _browser1.SetEditingCommands(cutCommand, copyCommand, pasteCommand, undoCommand);
                _browser1.ControlKeyEvent = controlKeyEvent;
            }

            controlKeyEvent.Subscribe(HandleControlKeyEvent);

            bookRenamedEvent.Subscribe(
                (oldToNewPath) =>
                {
                    // If the selected book is renamed, we should update our saved CurrentBookPath.
                    if (model.CurrentBook == null)
                    {
                        // Note: possibly all we need is this branch, which doesn't actually depend
                        // on model.CurrentBook being null. However, if we do have a model.CurrentBook,
                        // that's the definitive source of truth. We don't want by any chance to
                        // be updating our settings to indicate that anything else is selected.
                        // So I decided to use this only when we don't have that...usually only
                        // during startup, I think because of a duplicate name.
                        if (oldToNewPath.Key == Settings.Default.CurrentBookPath)
                        {
                            Settings.Default.CurrentBookPath = oldToNewPath.Value;
                        }
                    }
                    else if (oldToNewPath.Value == _model.CurrentBook?.FolderPath)
                    {
                        // This is the usual path, updating the settings to match the model's current book.
                        Settings.Default.CurrentBookPath = oldToNewPath.Value;
                    }
                    UpdatePageList(true);
                }
            );
#if __MonoCS__
            // The inactive button images look garishly pink on Linux/Mono, but look okay on Windows.
            // Merely introducing an "identity color matrix" to the image attributes appears to fix
            // this problem.  (The active form looks okay with or without this fix.)
            // See http://issues.bloomlibrary.org/youtrack/issue/BL-3714.
            float[][] colorMatrixElements =
            {
                new float[] { 1, 0, 0, 0, 0 }, // red scaling factor of 1
                new float[] { 0, 1, 0, 0, 0 }, // green scaling factor of 1
                new float[] { 0, 0, 1, 0, 0 }, // blue scaling factor of 1
                new float[] { 0, 0, 0, 1, 0 }, // alpha scaling factor of 1
                new float[] { 0, 0, 0, 0, 1 },
            }; // three translations of 0.0
            var colorMatrix = new ColorMatrix(colorMatrixElements);
            _undoButton.ImageAttributes.SetColorMatrix(
                colorMatrix,
                ColorMatrixFlag.Default,
                ColorAdjustType.Bitmap
            );
            _cutButton.ImageAttributes.SetColorMatrix(
                colorMatrix,
                ColorMatrixFlag.Default,
                ColorAdjustType.Bitmap
            );
            _pasteButton.ImageAttributes.SetColorMatrix(
                colorMatrix,
                ColorMatrixFlag.Default,
                ColorAdjustType.Bitmap
            );
            _copyButton.ImageAttributes.SetColorMatrix(
                colorMatrix,
                ColorMatrixFlag.Default,
                ColorAdjustType.Bitmap
            );

            // Prevent the layout choices and book language dropdown menus from closing
            // immediately in Linux/Gnome (default for ubuntu 18.04 aka bionic).
            _layoutChoices.DropDown.Closing += DropDown_Closing;
            _contentLanguagesDropdown.DropDown.Closing += DropDown_Closing;
            _layoutChoices.DropDown.Opening += DropDown_Opening;
            _contentLanguagesDropdown.DropDown.Opening += DropDown_Opening;
#endif
        }

#if __MonoCS__
        private bool _ignoreNextAppFocusChange;

        /// <summary>
        /// Prevent the book language and layout dropdown menus from closing prematurely.
        /// This is a big problem for Gnome, which is the default window manager in Ubuntu 18.04.
        /// (This must be due to the way Gnome sends out various windowing messages.)
        /// </summary>
        /// <remarks>
        /// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6107.
        /// See also WorkspaceView.DropDown_Closing (UI language menu dropdown), which has largely
        /// been in place for some time.  This is similar to that method.
        /// The side-effect of this method on systems other than Gnome is that the first click
        /// outside the menu will not close it.  But since we don't have a good way to detect
        /// Gnome, this seems like a minimal price to pay for allowing Gnome to work.
        /// </remarks>
        void DropDown_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (e.CloseReason)
            {
                case ToolStripDropDownCloseReason.AppFocusChange:
                    // With Linux/Gnome, a spurious focus change happens as soon as the menu is opened.
                    // So we want to ignore the first closing caused by AppFocusChange.
                    e.Cancel = _ignoreNextAppFocusChange;
                    break;
                case ToolStripDropDownCloseReason.Keyboard:
                    // "reason" is Keyboard, but this seems to be generated just by moving the mouse over
                    // the adjacent (visible) button.
                    var mousePos = _layoutChoices.Owner.PointToClient(MousePosition);
                    var bounds =
                        (sender == _layoutChoices.DropDown)
                            ? _contentLanguagesDropdown.Bounds
                            : _layoutChoices.Bounds;
                    if (bounds.Contains(mousePos))
                    {
                        e.Cancel = true; // probably a false positive
                    }
                    break;
                default: // includes ItemClicked, CloseCalled, AppClicked
                    break;
            }
            _ignoreNextAppFocusChange = false;
            Debug.WriteLine(
                "DEBUG EditingView.DropDown_Closing: reason={0}, cancel={1}",
                e.CloseReason.ToString(),
                e.Cancel
            );
        }

        void DropDown_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _ignoreNextAppFocusChange = true;
        }
#endif

        public EditingModel Model => _model;

        private void HandleControlKeyEvent(object keyData)
        {
            if (_visible && (Keys)keyData == (Keys.Control | Keys.N))
            {
                // This is for now a TODO
                //_model.HandleAddNewPageKeystroke(null);
            }
        }

        /// <summary>
        /// Prevents a white line from appearing below the tool strip
        /// Be careful if using this on Linux; it can have strange side-effects (https://jira.sil.org/browse/BL-509).
        /// </summary>
        public class FixedToolStripRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                //just don't draw a border
            }
        }

        void ParentForm_Activated(object sender, EventArgs e)
        {
            if (!_visible) //else you get a totally non-responsive Bloom, if you come back to a Bloom that isn't in the Edit tab
                return;

            Debug.WriteLine("EditTab.ParentForm_Activated(): Selecting Browser");
            _browser1.SelectBrowser();

            _editButtonsUpdateTimer.Enabled = Parent != null;
        }

        public void CheckFontAvailability()
        {
            var fontMessage = _model.GetFontAvailabilityMessage();
            if (!string.IsNullOrEmpty(fontMessage))
            {
                // Yes, we experienced a font name in the wild which contained curly brackets and crashed Bloom. BL-9340.
                fontMessage = fontMessage.Replace("{", "{{").Replace("}", "}}");
                ErrorReport.NotifyUserOfProblem(
                    new ShowOncePerSessionBasedOnExactMessagePolicy(),
                    fontMessage
                );
            }
        }

        /// <summary>
        /// this is called by our model, as a result of a "SelectedTabChangedEvent". So it's a lot more reliable than the normal winforms one.
        /// </summary>
        public async Task OnVisibleChanged(bool visible)
        {
            _visible = visible;
            if (visible)
            {
                Cursor = Cursors.WaitCursor;
                if (_model.GetBookHasChanged())
                {
                    //even before showing, we need to clear some things so the user doesn't see the old stuff
                    _pageListView.Clear();
                }
                _model.OnBecomeVisible();
                Logger.WriteEvent("Entered Edit Tab");
                Cursor = Cursors.Default;
            }
            else
            {
                // This will rarely do anything. It's typically called from the OnTabChanged event, which is invoked after
                // onTabAboutToChange, which (typically, in state Editing) initiates a Save with a pending action that returns null,
                // which will also cause a change to NoPage. However, it will be ignored in states where it's not valid,
                // and may be helpful in some cases (e.g., if somehow we're navigating), so I decided to put it in.
                _model.StateMachine.ToNoPage();
            }
        }

        /// <summary>
        /// Done when the state machine determines it really is safe to switch to the NoPage state, that is, we've
        /// done any necessary saving before switching away from the Edit tab.
        /// </summary>
        public void OnHideEditTab()
        {
            _browser1.Navigate("about:blank", false); //so we don't see the old one for moment, the next time we open this tab
            _model.ClearBookForToolboxContent(); // there's no longer a frame ready for a new page displayed in the browser.
        }

        DateTime _beginPageLoad;

        public void NextReloadChangesUiLanguage()
        {
            _changingUiLanguage = true;
        }

        /// <summary>
        /// Initiate switching to the specified page. The system should be in a state where we know
        /// there are no unsaved changes.
        /// </summary>
        public void GoToPage(IPage page, bool changingUiLanguage = false)
        {
            if (!_model.Visible)
            {
                return;
            }
            _changingUiLanguage |= changingUiLanguage;

#if MEMORYCHECK
            // Check memory for the benefit of developers.
            Bloom.Utils.MemoryManagement.CheckMemory(
                false,
                "EditingView - about to update the displayed page",
                false
            );
#endif
            if (_model.HaveCurrentEditableBook)
            {
                _beginPageLoad = DateTime.Now;
                if (!_model.StateMachine.ToNavigating(page.Id))
                {
                    throw new ApplicationException(
                        "GotoPage called while not in a valid state. Changes to the page may not get saved. Please report and connect to BL-13120."
                    );
                }
            }
        }

        private bool _changingUiLanguage = false; // review: should this be part of the state machine state?

        /// <summary>
        /// This is the View part of what happens when the state machine determines that we should navigate
        /// to a particular page (possibly the one we're on after it was saved and stripped).
        /// Internal as a hint that it should only be called in that one way. To goto a particular page
        /// with appropriate state changes and checks, call GoToPage.
        /// </summary>
        internal void StartNavigationToEditPage(IPage page)
        {
            _pageListView.SelectThumbnailWithoutSendingEvent(page);
            _pageListView.UpdateThumbnailAsync(page);
            _model.SaveStateForFullSaveDecision();
            // The following comment applies to GeckoFx. The logic described has moved into GeckoFxBrowser.
            // Hopefully the WebView2 DocumentCompleted is more reliable and it won't be needed there.
            // Unfortunately this comment isn't easily modifiable for the new context, so I'm leaving it here for now.
            // So far, the most reliable way I've found to detect that the page is fully loaded and we can call
            // initialize() is the ReadyStateChanged event (combined with checking that ReadyState is "complete").
            // This works for most pages but not all...some (e.g., the credits page in a basic book) seem to just go on
            // being "interactive". As a desperate step I tried looking for DocumentCompleted (which fires too soon and often),
            // but still, we never get one where the ready state is completed. This page just stays 'interactive'.
            // A desperate expedient would be to try running some Javascript to test whether the 'initialize' function
            // has actually loaded. If you try that, be careful...this function seems to be used in cases where that
            // never happens.
            // Do this before we change the src of the iframe to make sure we're ready when the document-completed arrives.
            _browser1.DocumentCompleted += WebBrowser_ReadyStateChanged;
            if (
                _model.AreToolboxAndOuterFrameCurrent()
                && !_changingUiLanguage
                && !ShouldDoFullReload()
            )
            {
                // Keep the top document and toolbox iframe, just navigate the page iframe to the new page.
                var pageUrl = _model.GetUrlForCurrentPage();
                var urlFile = Path.GetFileName(pageUrl); // this actually works with a leading http://.
                Logger.WriteEvent(
                    $"changing page via editTabBundle.switchContentPage('{urlFile}')"
                );
                _browser1.RunJavascriptFireAndForget(
                    "editTabBundle.switchContentPage('" + pageUrl + "');"
                );
            }
            else
            {
                // Set everything up and navigate the top browser to a new root document.
                _model.SetupServerWithCurrentBookToolboxContents();
                var dom = _model.GetXmlDocumentForEditScreenWebPage();
                _browser1.Navigate(
                    dom,
                    setAsCurrentPageForDebugging: true,
                    source: InMemoryHtmlFileSource.Frame
                );
            }
            SetModalState(false); // ensure _pageListView is enabled (BL-9712).
#if MEMORYCHECK
            // Check memory for the benefit of developers.
            Bloom.Utils.MemoryManagement.CheckMemory(
                false,
                "EditingView - StartNavigationToEditPage() about to call UpdateDropdownButtons()",
                false
            );
#endif
            _changingUiLanguage = false; // we've done a top-level navigate if this required it.
            if (_model.CurrentPage != null)
            {
                UpdateDropdownButtons();
            }
#if MEMORYCHECK
            // Check memory for the benefit of developers.
            Bloom.Utils.MemoryManagement.CheckMemory(
                false,
                "EditingView - StartNavigationToEditPage() finished",
                false
            );
#endif
        }

        // This method supports an approach of doing a reload of the top page only if we are short of memory,
        // because we get large memory leaks just reloading the iframe, but can recover most of it
        // by occasionally reloading everything.
        // Easy to change between never, if-shift-key-is-down, always, or MemoryUtils.SystemIsShortOfMemory().
        //
        // For 4.9 and 5.0 betas/releases, we set this to MemoryUtils.SystemIsShortOfMemory(),
        // but you can also set it to true so the full reload gets more testing (e.g. in alpha).
        private bool ShouldDoFullReload() => MemoryUtils.SystemIsShortOfMemory();

        private bool UseBackgroundGC()
        {
            // Note that ModifierKeys does not seem to work on Linux.
            return ((ModifierKeys & Keys.Alt) == Keys.Alt)
                || RobustFile.Exists("/tmp/UseBackgroundGC");
        }

        void WebBrowser_ReadyStateChanged(object sender, EventArgs e)
        {
            _browser1.DocumentCompleted -= WebBrowser_ReadyStateChanged;
            HidePageAndShowWaitCursor(false);
            _model.DocumentCompleted();
            _browser1.Focus(); //fix BL-3078 No Initial Insertion Point when any page shown
            var beginGarbageCollect = DateTime.Now;
            if (UseBackgroundGC())
            {
                GC.Collect(2, GCCollectionMode.Optimized, false, true);
            }
            else
            {
                GC.Collect( /*2, GCCollectionMode.Optimized, false, true*/
                );
                GC.WaitForPendingFinalizers();
            }
            var endPageLoad = DateTime.Now;
        }

        public void UpdatePageList(bool emptyThumbnailCache)
        {
            if (emptyThumbnailCache)
            {
                _pageListView.EmptyThumbnailCache();
            }
            _pageListApi.ClearPagesCache();
            _pageListView.SetBook(_model.CurrentBook);
        }

        internal async Task<string> GetStringFromJavascriptAsync(string script)
        {
            return await _browser1.GetStringFromJavascriptAsync(script);
        }

        internal async Task RunJavascriptAsync(string script)
        {
            await _browser1.RunJavascriptAsync(script);
            return;
        }

        private Metadata _originalImageMetadataFromImageToolbox;

        // When the copyright and license dialog is launched from the palaso image toolbox,
        // this action will be set as the thing to do to save the metadata (into the image)
        // when the user confirms changes in the C/L dialog.
        private Action<Metadata> _saveNewImageMetadataActionForImageToolbox;

        private string _fileNameOfImageBeingModified;

        public string FileNameOfImageBeingModified => _fileNameOfImageBeingModified;

        public Metadata PrepareToEditImageMetadata(string fileName)
        {
            if (fileName == null)
            {
                // Without a file name, we are coming from the palaso image toolbox
                return _originalImageMetadataFromImageToolbox;
            }

            if (fileName.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    LocalizationManager.GetString(
                        "EditTab.ImageMetadata.CannotEditEmbeddedImage",
                        "Bloom can't edit image information for this image because it is embedded data, not a file image."
                    )
                );
            }

            // keep a reference to the fileName rather the image to avoid dispose issues
            _fileNameOfImageBeingModified = fileName;

            using (
                var imageBeingModified = ImageUpdater.GetImageInfoSafelyFromFilePath(
                    _model.CurrentBook.FolderPath,
                    fileName
                )
            )
            {
                if (imageBeingModified == null)
                {
                    return null; // exception handled in ImageUpdater
                }

                if (ImageUpdater.ImageHasMetadata(imageBeingModified))
                {
                    // If we have metadata with an official collectionUri
                    // just give a summary of the metadata
                    if (
                        ImageUpdater.ImageIsFromOfficialCollection(imageBeingModified.Metadata)
                        || ImageUpdater.ImageIsStockGameImage(fileName, imageBeingModified.Metadata)
                    )
                    {
                        MessageBox.Show(
                            imageBeingModified.Metadata.GetSummaryParagraph(
                                new string[] { "en" },
                                out string _
                            )
                        );
                        return null;
                    }
                }

                var metadata = imageBeingModified.Metadata;
                // If the license is not set, default to CC-BY.
                metadata.SetupReasonableLicenseDefaultBeforeEditing();

                return metadata;
            }
        }

        /// <returns>false if saving via libpalaso image toolbox or saving failed; true otherwise</returns>
        public bool SaveImageMetadata(Metadata metadata)
        {
            if (_saveNewImageMetadataActionForImageToolbox != null)
            {
                _saveNewImageMetadataActionForImageToolbox(metadata);
                return false;
            }

            try
            {
                ImageUtils.SaveImageMetadataIfNeeded(
                    metadata,
                    _model.CurrentBook.FolderPath,
                    _fileNameOfImageBeingModified
                );
            }
            catch (Exception e)
            {
                ImageUtils.ReportImageMetadataProblem(
                    Path.Combine(_model.CurrentBook.FolderPath, _fileNameOfImageBeingModified),
                    e
                );
                return false;
            }
            return true;
        }

        public void UpdateMetadataForCurrentImage()
        {
            _model.UpdateMetaData(_fileNameOfImageBeingModified);
        }

        private void LaunchCopyrightAndLicenseDialogForImage(Metadata imageMetadata)
        {
            var dialogTitle = LocalizationManager.GetString(
                "CopyrightAndLicense",
                "Copyright and License"
            );
            var data = _copyrightAndLicenseApi.GetJsonFromMetadata(imageMetadata, forBook: false);
            using (
                var dlg = new ReactDialog(
                    "copyrightAndLicenseBundle",
                    new { isForBook = false, data },
                    dialogTitle
                )
            )
            {
                dlg.Width = 500;
                dlg.Height = 700;

                dlg.ShowDialog(this);
            }
        }

        public bool AskUserIfCopyImageMetadataToAllImages(Metadata metadata)
        {
            var answer = MessageBox.Show(
                LocalizationManager.GetString(
                    "EditTab.CopyImageIPMetadataQuestion",
                    "Copy this information to all other pictures in this book?",
                    "get this after you edit the metadata of an image"
                ),
                LocalizationManager.GetString(
                    "EditTab.TitleOfCopyIPToWholeBooksDialog",
                    "Picture Intellectual Property Information"
                ),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2
            );
            return answer == DialogResult.Yes;
        }

        public void CopyImageMetadataToAllImages(Metadata metadata)
        {
            {
                Cursor = Cursors.WaitCursor;

                // Note: if something goes wrong in this call, the user gets notified, so we don't have
                // to catch errors here.
                _model.CopyImageMetadataToWholeBook(metadata);
                // There might be more than one image on this page. Update canvas image elements.
                _model.RefreshDisplayOfCurrentPage();

                Cursor = Cursors.Default;
            }
        }

        public void OnCopyImage(UrlPathString imageSrc, bool imageIsGif)
        {
            // NB: bloomImages.js contains code that prevents us arriving here
            // if our image is simply the placeholder flower

            CopyImageToClipboard(imageSrc, _model.CurrentBook.FolderPath, imageIsGif);
        }

        public void OnPasteImage(string imageId, UrlPathString priorImageSrc, bool imageIsGif)
        {
            var pictureChanged = false;
            using (var measure = PerformanceMeasurement.Global.Measure("Paste Image"))
            {
                PalasoImage clipboardImage = null;
                try
                {
                    if (imageIsGif)
                    {
                        // The only way we currently support copying and pasting a gif is through a text path.
                        var path = PortableClipboard.GetText();
                        if (
                            string.IsNullOrEmpty(path)
                            || !RobustFile.Exists(path)
                            || Path.GetExtension(path).ToLowerInvariant() != ".gif"
                        )
                        {
                            throw new InvalidOperationException(
                                LocalizationManager.GetString(
                                    "EditTab.NoGifOnClipboard",
                                    "To paste a Gif, copy a path to a Gif file, or copy from another Bloom GIF element"
                                )
                            );
                        }
                        SetGifImage(imageId, priorImageSrc, path);
                        return;
                    }
                    try
                    {
                        clipboardImage = PortableClipboard.GetImageFromClipboardWithExceptions();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            LocalizationManager.GetString(
                                "EditTab.NoValidImageFoundOnClipboard",
                                "Bloom failed to interpret the clipboard contents as an image. Possibly it was a damaged file, or too large. Try copying something else."
                            ),
                            ex
                        );
                    }

                    if (clipboardImage == null)
                    {
                        throw new InvalidOperationException(
                            LocalizationManager.GetString(
                                "EditTab.NoImageFoundOnClipboard",
                                "Before you can paste an image, copy one onto your 'clipboard', from another program."
                            )
                        );
                    }

                    Cursor = Cursors.WaitCursor;

                    //nb: Taglib# requires an extension that matches the file content type.
                    if (ImageUtils.AppearsToBeJpeg(clipboardImage))
                    {
                        if (ShouldBailOutBecauseUserAgreedNotToUseJpeg(clipboardImage))
                            return;
                        Logger.WriteMinorEvent(
                            "[Paste Image] Pasting jpeg image {0}",
                            clipboardImage.OriginalFilePath
                        );
                        _model.ChangePicture(imageId, priorImageSrc, clipboardImage);
                        pictureChanged = true;
                    }
                    else
                    {
                        //At this point, it could be a bmp, tiff, or PNG. We want it to be a PNG.
                        if (clipboardImage.OriginalFilePath == null) //they pasted an image, not a path
                        {
                            Logger.WriteMinorEvent(
                                "[Paste Image] Pasting image directly from clipboard (e.g. screenshot)"
                            );
                            _model.ChangePicture(imageId, priorImageSrc, clipboardImage);
                            pictureChanged = true;
                        }
                        //they pasted a path to a png
                        else if (
                            Path.GetExtension(clipboardImage.OriginalFilePath).ToLowerInvariant()
                            == ".png"
                        )
                        {
                            Logger.WriteMinorEvent(
                                "[Paste Image] Pasting png file {0}",
                                clipboardImage.OriginalFilePath
                            );
                            _model.ChangePicture(imageId, priorImageSrc, clipboardImage);
                            pictureChanged = true;
                        }
                        else // they pasted a path to some other bitmap format
                        {
                            var pathToPngVersion = Path.Combine(
                                Path.GetTempPath(),
                                Path.GetFileNameWithoutExtension(clipboardImage.FileName) + ".png"
                            );
                            Logger.WriteMinorEvent(
                                "[Paste Image] Saving {0} ({1}) as {2} and converting to PNG",
                                clipboardImage.FileName,
                                clipboardImage.OriginalFilePath,
                                pathToPngVersion
                            );
                            if (RobustFile.Exists(pathToPngVersion))
                            {
                                RobustFile.Delete(pathToPngVersion);
                            }

                            using (var temp = TempFile.TrackExisting(pathToPngVersion))
                            {
                                SIL.IO.RobustImageIO.SaveImage(
                                    clipboardImage.Image,
                                    pathToPngVersion,
                                    ImageFormat.Png
                                );

                                using (var palasoImage = PalasoImage.FromFileRobustly(temp.Path))
                                {
                                    _model.ChangePicture(imageId, priorImageSrc, palasoImage);
                                    pictureChanged = true;
                                }
                            }
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                        error,
                        "The program had trouble getting an image from the clipboard."
                    );
                }
                finally
                {
                    if (clipboardImage != null)
                        clipboardImage.Dispose();
                    if (!pictureChanged)
                        RemoveUnneededImageId(imageId);
                }
            }

            Cursor = Cursors.Default;
        }

        private static PalasoImage GetImageFromClipboard()
        {
            return PortableClipboard.GetImageFromClipboard();
        }

        private bool CopyImageToClipboard(
            UrlPathString imageSrc,
            string bookFolderPath,
            bool imageIsGif
        )
        {
            var path = Path.Combine(bookFolderPath, imageSrc.PathOnly.NotEncoded);
            try
            {
                if (imageIsGif)
                {
                    // Haven't been able to find a way to copy the Gif itself into the clipboard.
                    // Possibly we could also copy the first frame as an actual image.
                    // For now, at least we can copy one GIF canvas element to another, and nothing bad happens
                    PortableClipboard.SetText(path);
                    return true;
                }
                using (var image = PalasoImage.FromFileRobustly(path))
                {
                    PortableClipboard.CopyImageToClipboard(image);
                }
                return true;
            }
            catch (NotImplementedException)
            {
                var msg = LocalizationManager.GetDynamicString(
                    "Bloom",
                    "ImageToClipboard",
                    "Copying an image to the clipboard is not yet implemented in Bloom for Linux.",
                    "message for messagebox warning to user"
                );
                var header = LocalizationManager.GetDynamicString(
                    "Bloom",
                    "NotImplemented",
                    "Not Yet Implemented",
                    "header for messagebox warning to user"
                );
                MessageBox.Show(msg, header);
            }
            catch (ExternalException e)
            {
                Logger.WriteEvent("CopyImageToClipboard -> ExternalException: " + e.Message);
                var msg =
                    LocalizationManager.GetDynamicString(
                        "Bloom",
                        "EditTab.Image.CopyImageFailed",
                        "Bloom had problems using your computer's clipboard. Some other program may be interfering."
                    )
                    + Environment.NewLine
                    + Environment.NewLine
                    + LocalizationManager.GetDynamicString(
                        "Bloom",
                        "EditTab.Image.TryRestart",
                        "Try closing other programs and restart your computer if necessary."
                    );
                MessageBox.Show(msg);
            }
            catch (Exception e)
            {
                Debug.Fail(e.Message);
                Logger.WriteEvent("CopyImageToClipboard:" + e.Message);
            }

            return false;
        }

        /// <summary>
        /// Returns true if it is either: a) OK to change images, or b) user overrides
        /// Returns false if user cancels message box
        /// </summary>
        /// <param name="imagePath"></param>
        /// <returns></returns>
        internal bool CheckIfLockedAndWarn(string imagePath)
        {
            // Enhance: we may want to reinstate some sort of (disableable) warning when they edit a picture while translating.
            // Original comment:  this would let them set it once without us bugging them, but after that if they
            //go to change it, we would bug them because we don't have a way of knowing that it was a placeholder before.
            //if (!imagePath.ToLower().Contains("placeholder")  //always allow them to put in something over a placeholder
            //	&& !_model.CanChangeImages())
            //{
            //	if (DialogResult.Cancel == MessageBox.Show(LocalizationManager.GetString("EditTab.ImageChangeWarning", "This book is locked down as shell. Are you sure you want to change the picture?"), LocalizationManager.GetString("EditTab.ChangeImage", "Change Image"), MessageBoxButtons.OKCancel))
            //	{
            //		return false;
            //	}
            //}
            return true;
        }

        private string _gifDirectory; // Todo: worth saving this as a UserPrefs? Or can/should we use the same one as for images?

        public void OnChangeImage(string imageId, UrlPathString imageSrc, bool imageIsGif)
        {
            Cursor = Cursors.WaitCursor;

            string newImagePath = null;
            if (imageIsGif)
            {
                // We don't want to use the image toolbox for GIFs, because it will convert them to PNGs.
                // Instead, we'll just use a file chooser
                using (
                    var dlg = new BloomOpenFileDialog
                    {
                        InitialDirectory =
                            _gifDirectory ?? Environment.SpecialFolder.MyPictures.ToString(),
                        Filter = "gif|*.gif",
                    }
                )
                {
                    var result = dlg.ShowDialog();
                    if (result == DialogResult.OK)
                        SetGifImage(imageId, imageSrc, dlg.FileName);
                }
                return; // we don't want to do the rest of this method, calling the toolbox, etc.
            }

            var imageInfo = new PalasoImage();
            Image oldImage = null;
            var oldSize = new Size() { Height = 0, Width = 0 };

            var existingImagePath = Path.Combine(
                _model.CurrentBook.FolderPath,
                imageSrc.PathOnly.NotEncoded
            );

            //don't send the placeholder to the imagetoolbox... we get a better user experience if we admit we don't have an image yet.
            if (
                !imageSrc.NotEncoded.ToLowerInvariant().Contains("placeholder")
                && RobustFile.Exists(existingImagePath)
            )
            {
                try
                {
                    // Copy the old file we're passing in to the dialog.  It's possible that we'll just crop it, and return the modified
                    // image file with an unchanged path.  That's okay, but what if it's from a copied/duplicated page?  Then all the
                    // pages with that image get the modification, which is probably not what is wanted.  Excess files will get trimmed
                    // later when the book is reopened for editing.  See http://issues.bloomlibrary.org/youtrack/issue/BL-3689.
                    var folder = Path.GetDirectoryName(existingImagePath);
                    var newFilename = ImageUtils.GetUnusedFilename(
                        folder,
                        Path.GetFileNameWithoutExtension(existingImagePath),
                        Path.GetExtension(existingImagePath)
                    );
                    newImagePath = Path.Combine(folder, newFilename);
                    RobustFile.Copy(existingImagePath, newImagePath);
                    Debug.WriteLine("Created image copy: " + newImagePath);
                    Logger.WriteEvent("Created image copy: " + newImagePath);
                    imageInfo = PalasoImage.FromFileRobustly(newImagePath);
                    oldSize = imageInfo.Image.Size;
                    oldImage = imageInfo.Image;
                }
                catch (Exception e)
                {
                    Logger.WriteMinorEvent(
                        "Not able to load image for ImageToolboxDialog: " + e.Message
                    );
                }
            }
            Logger.WriteEvent("Showing ImageToolboxDialog Editor Dialog");
#if MEMORYCHECK
            // Check memory for the benefit of developers.  The user won't see anything.
            Bloom.Utils.MemoryManagement.CheckMemory(false, "about to choose picture", false);
#endif
            // Deep in the ImageToolboxDialog, when the user asks to see images from the ArtOfReading,
            // We need to use the Gecko version of the thumbnail viewer, since the original ListView
            // one has a sticky scroll bar in applications that are using Gecko.
            ThumbnailViewer.UseWebViewer = false; // no longer using Gecko at all.
            // The Gecko version of the text box keeps causing trouble on Linux, first with Wasta 14
            // (Trusty/Ubuntu 14.04 + Mint + Cinnamon), now with Bionic/Ubuntu 18.04 + Gnome.
            // See https://silbloom.myjetbrains.com/youtrack/issue/BL-1147 and
            // https://silbloom.myjetbrains.com/youtrack/issue/BL-6126.
            var useWebTextBox = TextInputBox.UseWebTextBox;
            if (SIL.PlatformUtilities.Platform.IsLinux)
                TextInputBox.UseWebTextBox = false;

            // not using a "using for this" because we want to disentagle the cost of the dialog vs. working
            // with the results of the dialog.
            var performanceMeasureForShowingDialog = PerformanceMeasurement.Global.Measure(
                "Show ImageToolbox Dialog"
            );
            using (
                var dlg = new ImageToolboxDialog(
                    imageInfo,
                    null,
                    // This action overrides the default "edit metadata" action in the palaso image toolbox.
                    // Its default is to launch the original palaso WinForms C/L dialog.
                    // But we launch our own react dialog. If/when the user clicks ok, we call back to palaso
                    // to save the new metadata using the saveNewImageMetadata.
                    // If the user cancels, then we never save and things get cleaned up when the
                    // modal image toolbox is closed.
                    // Note, it would, in theory, improve things to just save the new metadata
                    // here locally via an api call and then not close the dialog until that was saved.
                    // But we couldn't convince ourselves with 100% certainty that the save metadata api
                    // call would finish before the close dialog api call.
                    (originalMetadata, saveAction) =>
                    {
                        _saveNewImageMetadataActionForImageToolbox = saveAction;
                        _originalImageMetadataFromImageToolbox = originalMetadata;
                        LaunchCopyrightAndLicenseDialogForImage(originalMetadata);
                    }
                )
            )
            {
                // With the .net 8 upgrade, WinForms things shifted a bit so that the search language dropdown gets
                // covered up. Since we're going to redo this dialog anyway, it's not worth messing with the toolbox so
                // we simply make it wider to keep the language dropdown visible for now
                dlg.Width += 120;

                var searchLanguage = Settings.Default.ImageSearchLanguage;
                dlg.ImageLoadingExceptionReporter = (path, ex, msg) =>
                {
                    ReportFailureToLoadImage(path, ex);
                };
                if (String.IsNullOrWhiteSpace(searchLanguage))
                {
                    // Pass in the current UI language.  We want only the main language part of the tag.
                    // (for example, "zh-Hans" should pass in as "zh".)
                    searchLanguage = Settings.Default.UserInterfaceLanguage;
                    var idx = searchLanguage.IndexOfAny(new char[] { '-', '_' });
                    if (idx > 0)
                        searchLanguage = searchLanguage.Substring(0, idx);
                }

                dlg.SearchLanguage = searchLanguage;
                DialogResult result;
                try
                {
                    result = dlg.ShowDialog();
                }
                finally
                {
                    // These variables get set during a callback from the dialog that allows us to use our own way of
                    // editing metadata for an image. It's important that they get cleaned up once the dialog is closed
                    // so they don't interfere with future uses of the metadata editing code.
                    _originalImageMetadataFromImageToolbox = null;
                    _saveNewImageMetadataActionForImageToolbox = null;
                }
                if (performanceMeasureForShowingDialog != null)
                    performanceMeasureForShowingDialog.Dispose();
#if MEMORYCHECK
                // Check memory for the benefit of developers.  The user won't see anything.
                Bloom.Utils.MemoryManagement.CheckMemory(
                    false,
                    "picture chosen or canceled",
                    false
                );
#endif
                var imageChanged = false;
                if (DialogResult.OK == result && dlg.ImageInfo != null)
                {
                    using (PerformanceMeasurement.Global?.Measure("Processing Image"))
                    {
                        try
                        {
                            var sameAsOriginalImage = (
                                imageInfo == dlg.ImageInfo
                                && oldImage == dlg.ImageInfo.Image
                                && oldSize == dlg.ImageInfo.Image.Size
                                && newImagePath == dlg.ImageInfo.OriginalFilePath
                            );
                            // Avoid saving the Image data if possible.  A large PNG file can take 5-10 seconds to save.
                            // So check the current image dimensions against the original image dimensions to see if we
                            // can avoid saving.  See https://issues.bloomlibrary.org/youtrack/issue/BL-9377.
                            int height,
                                width; // set to -1, -1 if next call fails.
                            var gotSize = TryGetOriginalImageDimensions(
                                dlg.ImageInfo,
                                out height,
                                out width
                            );
                            var copyOriginalImage =
                                gotSize
                                && height == dlg.ImageInfo.Image.Height
                                && width == dlg.ImageInfo.Image.Width;
                            // Copy an uncropped image's file or save a cropped image to a file before processing further.
                            // The code for ensuring non-transparency uses GraphicsMagick on the file content if possible, so the
                            // file must be in sync with the imageInfo.  See https://issues.bloomlibrary.org/youtrack/issue/BL-8638.
                            // This applies to newly selected files as well as cropping previously selected files.
                            if (
                                newImagePath == null
                                || dlg.ImageInfo.OriginalFilePath != newImagePath
                            )
                            {
                                var originalImagePath = dlg.ImageInfo.OriginalFilePath;
                                var basename = Path.GetFileNameWithoutExtension(originalImagePath);
                                // ImageInfo.Save throws an exception for .bmp files because they can't store metadata.
                                // ImageInfo.Save doesn't save .tif file properly, creating a blank image file.  So always
                                // save images in PNG format if they aren't originally JPEG format.
                                // It's important to get this right: ImageInfo.Save() throws if the actual file format
                                // doens't match the extension.
                                // (Bloom always saves images in either PNG or JPEG format anyway.  If the image is
                                // actually BMP or TIFF, it will get either get converted to PNG when resized later or
                                // it will be saved in PNG form later if it's not resized.  ImageInfo.Save is slow for
                                // PNG images, so we avoid it if at all possible.)
                                var extension = ImageUtils.AppearsToBeJpeg(dlg.ImageInfo)
                                    ? ".jpg"
                                    : ".png";

                                var newFilename = ImageUtils.GetUnusedFilename(
                                    Path.GetTempPath(),
                                    basename,
                                    extension
                                );
                                newImagePath = Path.Combine(Path.GetTempPath(), newFilename);
                            }

                            var exceptionMsg = "Bloom had a problem including that image";
                            if (copyOriginalImage || sameAsOriginalImage)
                            {
                                // Try to copy the file only if we actually need to copy it.  (BL-9737)
                                if (dlg.ImageInfo.OriginalFilePath != newImagePath)
                                    RobustFile.Copy(dlg.ImageInfo.OriginalFilePath, newImagePath);
                            }
                            else
                            {
                                // Cropping may have occurred, so we need to save the image.
                                dlg.ImageInfo.Save(newImagePath);
                            }
                            dlg.ImageInfo.SetCurrentFilePath(newImagePath);
                            SaveChangedImage(imageId, imageSrc, dlg.ImageInfo, exceptionMsg);
                            imageChanged = true;
                        }
                        catch (Exception error)
                        {
                            var path = dlg.ImageInfo.OriginalFilePath;
                            if (dlg.ImageInfo.OriginalFilePath != newImagePath)
                                path += $" (or {newImagePath})";
                            ReportFailureToLoadImage(path, error);
                        }
                        finally
                        {
                            dlg.ImageInfo.SetCurrentFilePath(null); // clears internal cache
                            if (newImagePath != dlg.ImageInfo.OriginalFilePath)
                            {
                                RobustFile.Delete(newImagePath);
                            }
                        }
#if MEMORYCHECK
                        // Warn the user if we're starting to use too much memory.
                        Bloom.Utils.MemoryManagement.CheckMemory(
                            false,
                            "picture chosen and saved",
                            true
                        );
#endif
                    }
                }
                if (!imageChanged)
                {
                    // remove imageId from the element since it's no longer needed
                    RemoveUnneededImageId(imageId);
                }

                // If the user changed the search language for art of reading, remember their change. But if they didn't
                // touch it, don't remember it. Instead, let it continue to track the UI language so that if
                // they are new and just haven't got around to setting the main UI Language,
                // AOR can automatically start using that when they do.
                if (searchLanguage != dlg.SearchLanguage)
                {
                    //store their language selection even if they hit "cancel"
                    Settings.Default.ImageSearchLanguage = dlg.SearchLanguage;
                    Settings.Default.Save();
                }
            }
            TextInputBox.UseWebTextBox = useWebTextBox;
            Logger.WriteMinorEvent("Emerged from ImageToolboxDialog Editor Dialog");
            Cursor = Cursors.Default;
            imageInfo.Dispose(); // ensure memory doesn't leak
        }

        private void SetGifImage(string imageId, UrlPathString priorImageSrc, string sourcePath)
        {
            var baseName = Path.GetFileNameWithoutExtension(sourcePath);
            var destName = ImageUtils.GetUnusedFilename(
                _model.CurrentBook.FolderPath,
                baseName,
                ".gif",
                "gif"
            );
            var dest = Path.Combine(_model.CurrentBook.FolderPath, destName);
            RobustFile.Copy(sourcePath, dest);
            var args = new PageEditingModel.ImageInfoForJavascript()
            {
                imageId = imageId,
                src = UrlPathString.CreateFromUnencodedString(destName).UrlEncoded,
                // Enhance: can we provide any of this for a GIF?
                copyright = "",
                license = "",
                creator = "",
            };
            _model.UpdateImageInBrowser(args);
        }

        /// <summary>
        /// Get the original dimensions of the image if we can from the stored metadata.
        /// </summary>
        private bool TryGetOriginalImageDimensions(
            PalasoImage palasoImage,
            out int height,
            out int width
        )
        {
            height = -1;
            width = -1;
            try
            {
                // Yes, this is cheating, but why Taglib hides the original height and width is
                // beyond me, and we know (and have control over) PalasoImage's internals.
                BindingFlags bindFlags =
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic;
                var metaInfo = palasoImage
                    .Metadata.GetType()
                    .GetField("_originalTaglibMetadata", bindFlags);
                var taglibMetadata = metaInfo.GetValue(palasoImage.Metadata) as TagLib.Image.File;
                if (taglibMetadata == null)
                {
                    // TagLib doesn't understand IPTC profiles very well, which can cause
                    // exceptions leading to a null _originalTaglibMetadata value.  We can
                    // remove the IPTC profile using graphicsmagick, but that starts getting
                    // involved:
                    // 1) check palasoImage.Metadata.ExceptionCaughtWhileLoading.StackTrace for
                    //    "ReadAPP13Segment" to verify that it has a problem with the IPTC profile.
                    // 2) if so, we can run GraphicsMagick from here to remove the IPTC profile,
                    //    creating a temporary file, and then try reading that image's metadata
                    //    again to get the original dimensions.  (This doesn't need to preserve
                    //    quality but does need to preserve the size.)  The command would be
                    //
                    //    gm convert {palasoImage.OriginalFilePath} +profile iptc {tempFile.Path}
                    //
                    // 3) if that works, we need to flag somehow that the image needs to remove
                    //    the IPTC profile when it gets saved and optionally resized.  Removing
                    //    the profile has to be done even if the image does not need to be resized.
                    //
                    // This seems like a lot of work for a rare problem, so we'll just return false
                    // here and wait for a rainy day with nothing else to do to think about whether
                    // to implement all of this.
                    // Meanwhile, a pull request has been posted to TagLib# to fix the problem, but
                    // I don't know if it will ever be merged.
                    return false;
                }
                var widthInfo = taglibMetadata.GetType().GetField("width", bindFlags);
                var heightInfo = taglibMetadata.GetType().GetField("height", bindFlags);
                var widthObj = widthInfo.GetValue(taglibMetadata);
                if (widthObj is short || widthObj is ushort || widthObj is int || widthObj is uint)
                    width = Convert.ToInt32(widthObj);
                else
                    return false;
                var heightObj = heightInfo.GetValue(taglibMetadata);
                if (
                    heightObj is short
                    || heightObj is ushort
                    || heightObj is int
                    || heightObj is uint
                )
                    height = Convert.ToInt32(heightObj);
                else
                    return false;
                return true;
            }
            catch
            {
                // We tried and failed... (could be BMP or TIFF file)
                return false;
            }
        }

        void ReportFailureToLoadImage(string path, Exception ex)
        {
            ex.Data["ProblemImagePath"] = path;
            ex.Data["ProblemReportShortMessage"] = LocalizationManager.GetString(
                "EditTab.Image.ImageFailed",
                "Failed to load image"
            );
            String msg;
            if (ex is OutOfMemoryException)
            {
                // It should be very unusual not to get imageSize...LibPalaso is not supposed to send OOM
                // exceptions for this function without providing the information. Conceivably Bloom
                // ran out of memory at some other point than trying to load the image? Anyway I don't
                // think it's worth another version of the message for this case.
                Tuple<int, int> imageSize = ex.Data["imageSize"] as Tuple<int, int>;
                var width = imageSize == null ? "unknown" : imageSize.Item1.ToString();
                var height = imageSize == null ? "unknown" : imageSize.Item2.ToString();
                ;
                var msgFmt = LocalizationManager.GetString(
                    "EditTab.Image.TooLarge",
                    "Bloom ran out of memory loading this image ({0}), which is quite large ({1} by {2} pixels). Click Help for suggestions."
                );
                msg = String.Format(msgFmt, path, width, height);
                var help = LocalizationManager.GetString("Common.Help", "Help");
                msg =
                    msg
                    + "<br/></br><a href='https://community.bloomlibrary.org/t/running-out-of-memory-loading-images/3956'>"
                    + help
                    + "</a>";
            }
            else
            {
                var msgFmt = LocalizationManager.GetString(
                    "EditTab.Image.Corrupt",
                    "Bloom was not able to load {0}. The file may be corrupted. Please try another image. Here are some technical details: "
                );
                msg = String.Format(msgFmt, path) + "<br/><br/>" + ex.Message;
                var help = LocalizationManager.GetString("Common.Help", "Help");
                msg =
                    msg
                    + $"<br/><br/><a href='https://docs.bloomlibrary.org/image-import-failure'>{help}</a>";
            }
            ErrorReport.NotifyUserOfProblem(
                msg,
                ex,
                new NotifyUserOfProblemSettings(AllowSendReport.Disallow),
                new ShowAlwaysPolicy()
            );
        }

        public void SaveChangedImage(
            string imageId,
            UrlPathString priorImageSrc,
            PalasoImage imageInfo,
            string exceptionMsg
        )
        {
            var imageChanged = false;
            try
            {
                if (ShouldBailOutBecauseUserAgreedNotToUseJpeg(imageInfo))
                    return;
                _model.ChangePicture(imageId, priorImageSrc, imageInfo);
                imageChanged = true;
            }
            catch (System.IO.IOException error)
            {
                error.Data["ProblemImagePath"] = imageInfo.OriginalFilePath;
                ErrorReport.NotifyUserOfProblem(error, error.Message);
            }
            catch (ApplicationException error)
            {
                error.Data["ProblemImagePath"] = imageInfo.OriginalFilePath;
                ErrorReport.NotifyUserOfProblem(error, error.Message);
            }
            catch (Exception error)
            {
                error.Data["ProblemImagePath"] = imageInfo.OriginalFilePath;
                ErrorReport.NotifyUserOfProblem(error, exceptionMsg);
            }
            finally
            {
                if (!imageChanged)
                {
                    // remove imageId from the element since it's no longer needed
                    RemoveUnneededImageId(imageId);
                }
            }
        }

        public void RemoveUnneededImageId(string imageId)
        {
            Model
                .GetEditingBrowser()
                .RunJavascriptFireAndForget(
                    $"editTabBundle.getEditablePageBundleExports().removeImageId('{imageId}')"
                );
        }

        private bool ShouldBailOutBecauseUserAgreedNotToUseJpeg(PalasoImage imageInfo)
        {
            if (
                ImageUtils.AppearsToBeJpeg(imageInfo)
                && JpegWarningDialog.ShouldWarnAboutJpeg(imageInfo.Image)
            )
            {
                using (var jpegDialog = new JpegWarningDialog())
                {
                    return jpegDialog.ShowDialog() == DialogResult.Cancel;
                }
            }
            else
            {
                return false;
            }
        }

        public void UpdateThumbnailAsync(IPage page)
        {
            _pageListView.UpdateThumbnailAsync(page);
        }

        public void UpdateAllThumbnails()
        {
            _pageListView.UpdateAllThumbnails();
        }

        public void OnPaste(object sender, EventArgs e)
        {
            ExecuteCommandSafely(_pasteCommand);
        }

        /// <summary>
        /// Make a menu item for a dropdown button and return it.  Avoid creating a ToolStripSeparator instead of a
        /// ToolStripMenuItem even for a hyphen.
        /// </summary>
        /// <returns>the dropdown menu item</returns>
        /// <remarks>See https://silbloom.myjetbrains.com/youtrack/issue/BL-3796.</remarks>
        private ToolStripMenuItem AddDropdownItemSafely(string text)
        {
            // A single hyphen triggers a ToolStripSeparator instead of a ToolStripMenuItem, so change it minimally.
            // (Surely localizers wouldn't do this to us, but it has happened to a user.)
            if (text == "-")
                text = "- ";
            return new ToolStripMenuItem(text);
        }

        /// <summary>
        /// Send info to javascript on how the Dropdown Menu Buttons should appear both on page change and when
        /// requested through the Api.
        /// </summary>
        /// <returns>dropdown button info</returns>
        public dynamic UpdateDropdownButtons()
        {
            dynamic eventBundle = new DynamicJson();
            bool contentLanguagesEnabled =
                _model.CurrentPage != null
                && TranslationGroupManager.IsPageAffectedByLanguageMenu(
                    _model.CurrentPage.GetDivNodeForThisPage(),
                    _model.CurrentBook.BookInfo.AppearanceSettings.UsingLegacy
                );
            eventBundle.message = new
            {
                contentLanguagesEnabled,
                contentLanguagesNumber = _model.NumberOfDisplayedLanguages,
                layoutChoicesText = _model.GetCurrentLayout().DisplayName,
            };
            _webSocketServer.SendBundle("editTopBarControls", "updateDropdowns", eventBundle);
            return eventBundle.message;
        }

        public void ContentLanguagesDropdownClicked()
        {
            // Suppress reopening if just closed
            if (IsRecentTopBarMenuClose())
            {
                _lastTopBarMenuClosedTime = null;
                return;
            }

            _contentLanguagesDropdown.Items.Clear();

            var nSelected = _model.ContentLanguages.Count(l => l.Selected);
            foreach (var item in _model.ContentLanguages)
            {
                var language = item;
                var text = language.ToString();
                var menuItem = AddDropdownItemSafely(item.Name);
                menuItem.Tag = language;
                menuItem.Enabled = !language.Selected || nSelected > 1;
                menuItem.Checked = language.Selected;
                menuItem.CheckOnClick = true;
                menuItem.ImageScaling = ToolStripItemImageScaling.None;
                // Any language which is not selected may be turned on.
                // A language which is turned on may only be turned off if more than one is selected.
                menuItem.CheckedChanged += new EventHandler(
                    OnContentLanguageDropdownItem_CheckedChanged
                );
                _contentLanguagesDropdown.Items.Add(menuItem);
            }

            Browser.OnBrowserClick += Browser_Click;
            CommonApi.WorkspaceView.TopBarReactControl.OnBrowserClick += Browser_Click;

            ShowContextMenu(_contentLanguagesDropdown);
        }

        private void ShowContextMenu(ContextMenuStrip menu)
        {
            // Let the menu appear slightly below where the mouse is since it might be
            // hard to find exactly where the bottom left of the Dropdown button is
            // We should just be able to say menu.Show(mouseX, mouseY). But there is some sort
            // of race condition that happens if the menu is activated by the very first click
            // after the program is started and switched to edit mode. AI recommended using
            // BeginInvoke to make sure the click completes (so that it won't re-hide the menu).
            // That wasn't enough. Also setting the position of the menu before we show it
            // seemed to help, but it still wasn't right 100% of the time. Hopefully a 10ms
            // delay is not noticeable but enough to make it reliable.
            var mouseX = MousePosition.X;
            var mouseY = MousePosition.Y + 8;
            menu.Left = mouseX;
            menu.Top = mouseY;
            var timer = new Timer();
            timer.Interval = 10; // very soon, but after the click is over and done with.
            timer.Tick += (s, a) =>
            {
                menu.Left = mouseX;
                menu.Top = mouseY;
                menu.Show(mouseX, mouseY);
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }

        public void LayoutChoicesDropdownClicked()
        {
            // Suppress reopening if just closed
            if (IsRecentTopBarMenuClose())
            {
                _lastTopBarMenuClosedTime = null;
                return;
            }

            _layoutChoicesDropdown.Items.Clear();

            var layout = _model.GetCurrentLayout();
            var sizeAndOrientationChoices = _model.GetSizeAndOrientationChoices();

            foreach (var item in sizeAndOrientationChoices)
            {
                var choice = item;
                var text = choice.DisplayName;
                var menuItem = AddDropdownItemSafely(text);
                menuItem.Tag = choice;
                menuItem.Click += new EventHandler(OnPaperSizeAndOrientationMenuClick);

                _layoutChoicesDropdown.Items.Add(menuItem);
            }

            if (sizeAndOrientationChoices.Count() < 2)
            {
                var text = LocalizationManager.GetString(
                    "EditTab.NoOtherLayouts",
                    "There are no other options for this template.",
                    "Show in the size/orientation chooser dropdown of the edit tab, if there was only a single choice"
                );
                var menuItem = AddDropdownItemSafely(text);
                menuItem.Tag = null;
                menuItem.Enabled = false;
                _layoutChoicesDropdown.Items.Add(menuItem);
            }

            Browser.OnBrowserClick += Browser_Click;
            CommonApi.WorkspaceView.TopBarReactControl.OnBrowserClick += Browser_Click;

            ShowContextMenu(_layoutChoicesDropdown);
        }

        private bool IsRecentTopBarMenuClose()
        {
            if (_lastTopBarMenuClosedTime.HasValue)
            {
                var timeSinceLastClose = DateTime.UtcNow - _lastTopBarMenuClosedTime.Value;
                return timeSinceLastClose.TotalMilliseconds < 300;
            }
            return false;
        }

        void OnPaperSizeAndOrientationMenuClick(object sender, EventArgs e)
        {
            var item = (ToolStripMenuItem)sender;
            _model.SetLayout((Layout)item.Tag);
        }

        void OnContentLanguageDropdownItem_CheckedChanged(object sender, EventArgs e)
        {
            var item = (ToolStripMenuItem)sender;
            ((EditingModel.ContentLanguage)item.Tag).Selected = item.Checked;

            if (_sendingContentLanguagesSelectionChanged)
                _model.ContentLanguagesSelectionChanged();
        }

        private bool _sendingContentLanguagesSelectionChanged = true;

        public void SetActiveLanguages(bool L1, bool L2, bool L3)
        {
            var contentLanguages = _model.ContentLanguages.ToList();
            bool changed = false;
            try
            {
                // Send it once at the end. This reduces flicker and avoids problems where temporarily
                // all are off, since using the new book settings dialog it is possible to change more
                // than one in a single call to this method.
                _sendingContentLanguagesSelectionChanged = false;

                if (contentLanguages[0].Selected != L1)
                {
                    contentLanguages[0].Selected = L1;
                    changed = true;
                }

                if (contentLanguages.Count > 1 && contentLanguages[1].Selected != L2)
                {
                    contentLanguages[1].Selected = L2;
                    changed = true;
                }

                if (contentLanguages.Count > 2 && contentLanguages[2].Selected != L3)
                {
                    contentLanguages[2].Selected = L3;
                    changed = true;
                }
            }
            finally
            {
                _sendingContentLanguagesSelectionChanged = true;
            }

            if (changed)
                _model.ContentLanguagesSelectionChanged();
        }

        public void UpdateEditButtons()
        {
            // Checking whether to enable these buttons is done by javascript code.  This check
            // will fail with a javascript error while pages are being changed.  The code for
            // changing pages makes the browser invisible while the change is happening, so
            // that's what we have to check to prevent spurious javascript errors.
            // Note that this method is called by a timer (probably about 110msec cycle).
            if (!_browser1.Visible)
                return;
            _browser1.UpdateEditButtonsAsync();

            // update javascript with the new information
            dynamic eventBundle = new DynamicJson();
            eventBundle.enabled = new
            {
                copy = _copyCommand?.Enabled ?? false,
                cut = _cutCommand?.Enabled ?? false,
                paste = _pasteCommand?.Enabled ?? false,
                undo = _undoCommand?.Enabled ?? false,
            };
            _webSocketServer.SendBundle("editTopBarControls", "updateEditButtons", eventBundle);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            _editButtonsUpdateTimer.Enabled = Parent != null;
        }

        private void _editButtonsUpdateTimer_Tick(object sender, EventArgs e)
        {
            UpdateEditButtons();
        }

        private void ExecuteCommandSafely(Command cmdObject)
        {
            try
            {
                cmdObject.Execute();
            }
            catch (Exception error)
            {
                ErrorReport.NotifyUserOfProblem(
                    error,
                    LocalizationManager.GetString(
                        "Errors.SomethingWentWrong",
                        "Sorry, something went wrong."
                    )
                );
            }
        }

        public void ClearOutDisplay()
        {
            _pageListView.Clear();
            _browser1.Navigate("about:blank", false);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            //Why the check for null? In bl-283, user had been in settings dialog, which caused a closing down, but something
            //then did a callback to this view, such that ParentForm was null, and this died
            //This assert was driving me crazy (which is a short trip). I'd hit it every time I quit Bloom, but this things are fine. Debug.Assert(ParentForm != null);
            if (ParentForm != null)
            {
                ParentForm.Activated += new EventHandler(ParentForm_Activated);
                ParentForm.Deactivate += (sender, e1) =>
                {
                    _editButtonsUpdateTimer.Enabled = false;
                    // Save when we leave the main window, even just switching to the epub a11y check window.
                    // See https://silbloom.myjetbrains.com/youtrack/issue/BL-6228. This control can lose/regain
                    // focus erratically on Linux, so we don't want this save on its LostFocus event.
                    // But we do NOT want to Save right this instant. Deactivate happens a lot, especially while
                    // debugging. There's some evidence (BL-6303, BL-6296) that COM message pumping can
                    // cause the Deactivate event to be handled at apparently random moments, in the middle
                    // of doing something else. We might be trying to Save when the system isn't in a valid
                    // state, e.g., in the middle of refreshing everything because the user edited the title
                    // and the HTML file and containing folder changed name. Instead, arrange to Save when
                    // next idle.
                    // However, saving while not active runs into issues like BL-6299. Apparently running
                    // Javascript (which is also done elsewhere in SaveNow()) while the main window is
                    // inactive is quite disastrous in GeckoFx45, to the point of access violations that
                    // stop the program with a green screen. Pending decisions about possible UI changes or
                    // other ways of fixing this, we're just disabling it. One hope is that GeckoFx60,
                    // which is supposed to have a "headless" capability, may fix this.
                    //Application.Idle += SaveWhenIdle;
                };
            }
            // This prevents the built-in ctrl-mousewheel zooming as well as ctrl-+ and ctrl--.
            // They are a problem because the toolbox zooms too, which causes various problems including
            // BL-13846 (can't drag new canvas elements onto the page).
            // We enable these mouse actions for the main document iframe by handling the events
            // in Javascript and calling our zoom functions through an API.
            var settings = (Browser as WebView2Browser)?.InternalBrowser?.CoreWebView2?.Settings;
            if (settings != null)
                settings.IsZoomControlEnabled = false;
        }

        public string HelpTopicUrl => "/Tasks/Edit_tasks/Edit_tasks_overview.htm";

        /// <summary>
        /// Prevent navigation, e.g. while a dialog box is showing in the browser control
        /// </summary>
        internal void SetModalState(bool isModal)
        {
            _pageListView.Enabled = !isModal;
        }

        /// <summary>
        /// BL-2153: This is to provide visual feedback to the user that the program has received their
        ///          page change click and is actively processing the request.
        /// </summary>
        public void HidePageAndShowWaitCursor(bool hidePage)
        {
            if (_browser1.Visible != hidePage)
                return;

            _browser1.Visible = !hidePage;
            _pageListView.Enabled = !hidePage;
            Cursor = hidePage ? Cursors.WaitCursor : Cursors.Default;
            // _pageListView.Cursor = Cursor;
        }

        public void ShowAddPageDialog()
        {
            PageTemplatesApi.ForPageLayout = false;
            //if the dialog is already showing, it is up to this method we're calling to detect that and ignore our request
            RunJavascriptAsync("editTabBundle.showPageChooserDialog(false);");
        }

        internal void ShowChangeLayoutDialog()
        {
            PageTemplatesApi.ForPageLayout = true;
            //if the dialog is already showing, it is up to this method we're calling to detect that and ignore our request
            RunJavascriptAsync("editTabBundle.showPageChooserDialog(true);");
        }

        internal void ShowRegistrationDialog()
        {
            var command = $"editTabBundle.showRegistrationDialogInEditTab();";
            RunJavascriptAsync(command);
        }

        internal void ShowAboutDialog()
        {
            RunJavascriptAsync($"editTabBundle.showAboutDialogInEditTab();");
        }

        public int Zoom => EditingView.ZoomSetting;

        // The zoom factor that is shown in the top right of the toolbar (a percent).
        public static int ZoomSetting
        {
            // Whatever the user may have saved (e.g., from earlier use of ctrl-wheel), we'll make this an expected multiple-of-10 percent.
            get
            {
                // we used to store floating point numbers, but we now store integer percentages (30%-300% or more?).
                var zoomString = Settings.Default.PageZoom;
                if (String.IsNullOrWhiteSpace(zoomString))
                    return 100;
                int zoomInt;
                // This value may have been stored as floating point with the invariant culture, not the current culture.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-5579.
                if (
                    zoomString.Contains(
                        CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator
                    )
                )
                {
                    float zoomFloat;
                    if (
                        float.TryParse(
                            zoomString,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out zoomFloat
                        )
                    )
                    {
                        zoomInt = (int)Math.Round(zoomFloat * 10F) * 10;
                        if (zoomInt < ZoomModel.kMinimumZoom || zoomInt > ZoomModel.kMaximumZoom)
                            return 100; // bad antique value - normalize to real size.
                        return zoomInt;
                    }
                    else
                    {
                        return 100;
                    }
                }

                if (
                    int.TryParse(
                        zoomString,
                        System.Globalization.NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out zoomInt
                    )
                )
                {
                    // we can't go below 30 (30%), so those must be old floating point values that rounded to an integer.
                    if (zoomInt < ZoomModel.kMinimumZoom)
                        zoomInt = zoomInt * 100;
                    if (zoomInt > ZoomModel.kMaximumZoom)
                        return ZoomModel.kMaximumZoom;
                    return zoomInt;
                }
                else
                {
                    return 100;
                }
            }
        }

        public void SetZoom(int zoom)
        {
            // If we just put zoom/100.0 in the setZoom call, the implicit toString()
            // uses the regional settings and can produce e.g. 1,3 when we need 1.3
            var zoomFactor = (zoom / 100.0).ToString(CultureInfo.InvariantCulture);
            RunJavascriptAsync($"editTabBundle.setZoom({zoomFactor});");
            Settings.Default.PageZoom = zoom.ToString(CultureInfo.InvariantCulture);
            Settings.Default.Save();
            // Note: July 29 2025 we removed code that handled zoom by reloading the page,
            // with some complicated mess involving timers to make sure one reload for zoom
            // didn't interfere with another. I eventually tracked this down to when we made
            // canvas elements draggable (6/28/2017). I think the old JQuery draggable code was
            // messed up by scaling and had to be adjusted somehow. Now we're not using that,
            // so updating in place is much cleaner (and faster!).
        }

        public void AdjustPageZoom(int delta)
        {
            var currentZoom = _zoomModel.Zoom;
            if (
                delta < 0 && currentZoom <= ZoomModel.kMinimumZoom
                || delta > 0 && currentZoom >= ZoomModel.kMaximumZoom
            )
            {
                return;
            }
            _zoomModel.Zoom = currentZoom + delta;
        }

        internal void SetZoomModel(ZoomModel zoomModel)
        {
            _zoomModel = zoomModel;
        }

        // intended for use only by the EditingModel
        internal Browser Browser => _browser1;

        public void SaveAndOpenBookSettingsDialog()
        {
            _model.SaveThen(
                () =>
                {
                    // Open the book settings dialog to the context-specific group.
                    var groupIndex = _model.CurrentPage.IsCoverPage ? 0 : 1;
                    RunJavascriptAsync(
                        $"editTabBundle.showEditViewBookSettingsDialog({groupIndex});"
                    );
                    return _model.CurrentPage.Id;
                },
                () => { } // wrong state, do nothing
            );
        }

        public async Task AddImageFromUrlAsync(string desiredFileNameWithoutExtension, string url)
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                try
                {
                    var expandedUrl = url.StartsWith("/") ? BloomServer.ServerUrl + url : url;
                    var bytes = await client.GetByteArrayAsync(expandedUrl);

                    using (var memoryStream = new MemoryStream(bytes))
                    using (var image = System.Drawing.Image.FromStream(memoryStream))
                    {
                        string extension = ".png";
                        // Don't be tempted to close this 'using' after we're done with the
                        // palasoImage. Disposing it will also dispose the image, so keep
                        // it until we're done with that.
                        using (var palasoImage = new PalasoImage(image))
                        {
                            // Determine output format (keep original JPEG vs default to PNG)
                            var isJpeg = ImageUtils.AppearsToBeJpeg(palasoImage);
                            if (isJpeg)
                                extension = ".jpg";

                            var fileName = desiredFileNameWithoutExtension + extension;
                            var filePath = Path.Combine(_model.CurrentBook.FolderPath, fileName);

                            // Scale down large images.
                            // - Prefer scaling so the largest dimension is at most 256px.
                            // - But avoid making the smaller dimension too small; aim for it to be at least 128px.
                            // We choose the scale factor that shrinks the least while satisfying both constraints,
                            // and never upscale.
                            const int kMaxDim = 256;
                            const int kMinDim = 128;
                            using var fs = RobustFile.Create(filePath);
                            var format = isJpeg
                                ? System.Drawing.Imaging.ImageFormat.Jpeg
                                : System.Drawing.Imaging.ImageFormat.Png;

                            using (
                                var resized = ImageUtils.TryShrinkImageToApproxSize(
                                    image,
                                    kMaxDim,
                                    kMinDim
                                )
                            )
                            {
                                if (resized != null)
                                    resized.Save(fs, format);
                                else
                                    image.Save(fs, format);
                            }

                            // tell the caller that we have added the image.
                            // Why is most of the file name both part of the client context and
                            // sent as the message? For the context, we need something that
                            // doesn't depend on whether we make a jpg or png, because we don't
                            // find that out until the code just above here executes, and long
                            // before that, JS code needs to set up the listener with a clientContext
                            // based on which image we're expecting. OTOH, the code that receives
                            // the message needs to know exactly what file was created.
                            // We could just pass the extension, but it doesn't cost much to
                            // include the whole filename, and one day we might use this for
                            // something that would need to de-duplicate file names.
                            dynamic eventBundle = new DynamicJson();
                            eventBundle.message = fileName;
                            _webSocketServer.SendBundle(
                                "makeThumbnailFile-" + desiredFileNameWithoutExtension,
                                "success",
                                eventBundle
                            );
                            Debug.WriteLine("Added image for: " + fileName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    BloomWebSocketServer.Instance.SendEvent(
                        "makeThumbnailFile-" + desiredFileNameWithoutExtension,
                        "error: " + ex.Message
                    );
                    Debug.WriteLine("Failed to download image: " + ex.Message);
                }
            }
        }

        private void Browser_Click(object sender, EventArgs e)
        {
            Browser.OnBrowserClick -= Browser_Click;
            CommonApi.WorkspaceView.TopBarReactControl.OnBrowserClick -= Browser_Click;

            if (_contentLanguagesDropdown.Visible || _layoutChoicesDropdown.Visible)
            {
                _contentLanguagesDropdown?.Hide();
                _layoutChoicesDropdown?.Hide();
                _lastTopBarMenuClosedTime = DateTime.UtcNow;
            }
        }
    }
}
