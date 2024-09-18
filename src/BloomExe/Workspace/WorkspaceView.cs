using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Publish;
using Bloom.Registration;
using Bloom.TeamCollection;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web;
using Bloom.web.controllers;
using L10NSharp;
using Messir.Windows.Forms;
using Newtonsoft.Json;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
using SIL.Unicode;
using SIL.Windows.Forms.Miscellaneous;
using SIL.Windows.Forms.ReleaseNotes;
using SIL.Windows.Forms.SettingProtection;
using SIL.WritingSystems;

namespace Bloom.Workspace
{
    public partial class WorkspaceView : UserControl
    {
        private readonly WorkspaceModel _model;
        private readonly CollectionSettingsDialog.Factory _settingsDialogFactory;
        private readonly SelectedTabAboutToChangeEvent _selectedTabAboutToChangeEvent;
        private readonly SelectedTabChangedEvent _selectedTabChangedEvent;
        private readonly LocalizationChangedEvent _localizationChangedEvent;
        private readonly CollectionSettings _collectionSettings;
        private bool _viewInitialized;
        private int _originalToolStripPanelWidth;
        private int _originalToolSpecificPanelHorizPos;
        private int _originalUiMenuWidth;
        private int _stage1SpaceSaved;
        private int _stage2SpaceSaved;
        private string _originalHelpText;
        private Image _originalHelpImage;
        private string _originalUiLanguageSelection;
        private EditingView _editingView;
        private PublishView _publishView;
        private CollectionTabView _collectionTabView;
        private Control _previouslySelectedControl;
        public event EventHandler ReopenCurrentProject;
        public static float DPIOfThisAccount;
        private ZoomControl _zoomControl;

        public delegate WorkspaceView Factory();

        private TeamCollectionManager _tcManager;
        private BookSelection _bookSelection;
        private ToastNotifier _returnToCollectionTabNotifier;
        private BloomWebSocketServer _webSocketServer;
        private BookServer _bookServer;
        private WorkspaceTabSelection _tabSelection;
        private CollectionApi _collectionApi;
        private AudioRecording _audioRecording;
        private CollectionSettingsApi _collectionSettingsApi;

        //autofac uses this

        public WorkspaceView(
            WorkspaceModel model,
            CollectionTabView.Factory reactCollectionsTabsViewFactory,
            EditingView.Factory editingViewFactory,
            PublishView.Factory pdfViewFactory,
            CollectionSettingsDialog.Factory settingsDialogFactory,
            EditBookCommand editBookCommand,
            SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent,
            SelectedTabChangedEvent selectedTabChangedEvent,
            LocalizationChangedEvent localizationChangedEvent,
            CollectionSettings collectionSettings,
            CommonApi commonApi,
            BookSelection bookSelection,
            BookStatusChangeEvent bookStatusChangeEvent,
            TeamCollectionManager tcManager,
            BloomWebSocketServer webSocketServer,
            AppApi appApi,
            BookServer bookServer,
            CollectionApi collectionApi,
            WorkspaceApi workspaceApi,
            WorkspaceTabSelection tabSelection,
            AudioRecording audioRecording,
            CollectionSettingsApi collectionSettingsApi,
            TeamCollectionApi teamCollectionApi
        )
        {
            _model = model;
            _settingsDialogFactory = settingsDialogFactory;
            _selectedTabAboutToChangeEvent = selectedTabAboutToChangeEvent;
            _selectedTabChangedEvent = selectedTabChangedEvent;
            _bookSelection = bookSelection;
            _localizationChangedEvent = localizationChangedEvent;
            _tcManager = tcManager;
            _webSocketServer = webSocketServer;
            _bookServer = bookServer;
            _tabSelection = tabSelection;
            _audioRecording = audioRecording;
            collectionApi.WorkspaceView = this; // avoids an Autofac exception that appears if collectionApi constructor takes a WorkspaceView
            _collectionApi = collectionApi;
            appApi.WorkspaceView = this; // it needs to know, and there's some circularity involved in having factory pass it in
            workspaceApi.WorkspaceView = this; // and yet one more
            teamCollectionApi.WorkspaceView = this;
            _collectionSettingsApi = collectionSettingsApi;

            _collectionSettings = collectionSettings;
            // This provides the common API with a hook it can use to reload
            // the project. Another option would be to make Autofac pass a WorkspaceView
            // to the CommonApi constructor so it could raise the event more
            // directly. But I'm concerned about circularity: something that needs
            // the CommonApi may be needed itself by the WorkspaceView.
            commonApi.ReloadProjectAction = () =>
            {
                Invoke(ReopenCurrentProject);
            };

            //_chorusSystem = chorusSystem;
            _model.UpdateDisplay += new EventHandler(OnUpdateDisplay);

            // By this point, BloomServer is up and listening and our web controllers are registered,
            // so our new ProblemReportApi will function. These next two lines activate it.
            ErrorReport.OnShowDetails = ProblemReportApi.ShowProblemDialogForNonFatalException;
            FatalExceptionHandler.UseFallback = false;

            InitializeComponent();

            _checkForNewVersionMenuItem.Visible = Platform.IsWindows;

            _toolStrip.Renderer = new NoBorderToolStripRenderer();

            //we have a number of buttons which don't make sense for the remote (therefore vulnerable) low-end user
            //_settingsLauncherHelper.CustomSettingsControl = _toolStrip;
            //NB: these aren't really settings, but we're using that feature to simplify this menu down to what makes sense for the easily-confused user
            _settingsLauncherHelper.ManageComponent(_requestAFeatureMenuItem);
            _settingsLauncherHelper.ManageComponent(_webSiteMenuItem);
            _settingsLauncherHelper.ManageComponent(_releaseNotesMenuItem);
            _settingsLauncherHelper.ManageComponent(_divider2);

            OnSettingsProtectionChanged(this, null); //initial setup
            SettingsProtectionSettings.Default.PropertyChanged += new PropertyChangedEventHandler(
                OnSettingsProtectionChanged
            );

            _uiLanguageMenu.Visible = true;
            _settingsLauncherHelper.ManageComponent(_uiLanguageMenu);

            editBookCommand.Subscribe(OnEditBook);

            Application.Idle += new EventHandler(Application_Idle);
            Text = _model.ProjectName;

            //
            // _editingView needs to be created before we select a tab, since the model
            // gets notified about tab selection, and expects its view to be non-null,
            // and that is done by the EditingView constructor.
            //
            this._editingView = editingViewFactory();
            this._editingView.Dock = DockStyle.Fill;
            this._editingView.Model.EnableSwitchingTabs = (enabled) => _tabStrip.Enabled = enabled;

            _collectionTabView = reactCollectionsTabsViewFactory();
            _collectionTabView.ManageSettings(_settingsLauncherHelper);
            _collectionTabView.Dock = DockStyle.Fill;
            _collectionTabView.BackColor = System.Drawing.Color.FromArgb(
                ((int)(((byte)(87)))),
                ((int)(((byte)(87)))),
                ((int)(((byte)(87))))
            );
            _reactCollectionTab.Tag = _collectionTabView;
            _tabStrip.SelectedTab = _reactCollectionTab;

            //
            // _pdfView
            //
            this._publishView = pdfViewFactory();
            this._publishView.Dock = DockStyle.Fill;

            _publishTab.Tag = _publishView;
            _editTab.Tag = _editingView;

            SetTabVisibility(_publishTab, false);
            SetTabVisibility(_editTab, false);

            this._reactCollectionTab.Text = _collectionTabView.CollectionTabLabel;
            _tabStrip.SelectedTab = _reactCollectionTab;
            SelectPage(_collectionTabView);

            if (Platform.IsMono)
            {
                // Without this adjustment, we lose some controls on smaller resolutions.
                AdjustToolPanelLocation(true);
                // in mono auto-size causes the height of the tab strip to be too short
                _tabStrip.AutoSize = false;
            }

            _toolStrip.SizeChanged += ToolStripOnSizeChanged;

            _uiLanguageMenu.DropDownOpening += (sender, args) =>
            {
                SetupUiLanguageMenu();
            };
            SetupUiLanguageMenu(true);
            SetupZoomControl();
            AdjustButtonTextsForLocale();
            _viewInitialized = false;
            CommonApi.WorkspaceView = this;

            // We put this on the high priority list because the notification it sends
            // updates the highlighting of the selected button. Other subscribers include
            // the code that updates the preview, which is quite slow. We need the button
            // to respond quickly.
            // We'll need to do something even trickier if there start to be slow things that
            // happen in response to the book selection changed websocket message.
            bookSelection.SelectionChangedHighPriority += HandleBookSelectionChanged;
            bookStatusChangeEvent.Subscribe(args =>
            {
                HandleBookStatusChange(args);
            });
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // If we're loading a team collection, we need to do that...with its progress dialog...
            // before anything else, and we'll need to close the splash screen to make room for
            // that dialog.
            // Note, this not put into _startupActions...it should never be disabled.
            if (_tcManager?.CurrentCollectionEvenIfDisconnected == null)
            {
                ReadyToShowCollections();
            }
            else
            {
                StartupScreenManager.AddStartupAction(
                    () =>
                    {
                        // Don't do anything else after this as part of this idle task.
                        // See the comment near the end of HandleTeamStuffBeforeGetBookCollections.
                        _model.HandleTeamStuffBeforeGetBookCollections(ReadyToShowCollections);
                    },
                    shouldHideSplashScreen: true
                );
            }

            // Must not do this until we've done TC sync. Among various potential confusions,
            // if the book has been renamed remotely but not yet here, we may not be able to tell that it
            // needs to be checked out before BringBookUpToDate renames it here.
            StartupScreenManager.AddStartupAction(
                () => SelectBookAtStartup(),
                // We want to delay this until the buttons get drawn,
                // since it ties up the UI thread for a while.
                // Enhance: the code in CollectionsApi that raises this event is crude; it just
                // looks for the first two button thumbnails to be requested. It would be better if
                // we had some way of knowing when the collection panes were fully rendered.
                // It would be better still if most of the work of SelectPreviouslySelectedBook could
                // be done on a background thread so it could make progress as quickly as possible
                // without holding up drawing the collection panes.
                waitForMilestone: "collectionButtonsDrawn",
                shouldHideSplashScreen: true
            ); // possibility of error message boxes (BL-12155)
        }

        private void ReadyToShowCollections()
        {
            _collectionTabView.ReadyToShowCollections();
        }

        /// <summary>
        /// Restore the the selection of the previously selected book, but only if the book is in either
        /// the same collection (waiting to be edited) or in a source collection (waiting to be created
        /// in the current collection).
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-10225.
        /// </remarks>
        private void SelectBookAtStartup()
        {
            try
            {
                var selBookPath =
                    Program.PathToBookDownloadedAtStartup ?? Settings.Default.CurrentBookPath;
                if (string.IsNullOrEmpty(selBookPath) || !Directory.Exists(selBookPath))
                    return;
                var selBookCollectionFolder = Path.GetDirectoryName(selBookPath);
                var inCurrentCollection = selBookCollectionFolder == _collectionSettings.FolderPath;
                var inSourceFolder =
                    !inCurrentCollection
                    && _model
                        .GetSourceCollectionFolders()
                        .ToList()
                        .Exists(folder => selBookCollectionFolder == folder);
                if (inCurrentCollection || inSourceFolder)
                {
                    // We used to just create a BookInfo here. But there was a race condition where
                    // an API call on another thread looking for the books of the collection would cause
                    // the collection to independently create a bookInfo for the same book while
                    // the book object was being created but before it is registered as the selected book.
                    // Then the book and the collection go on having independent bookInfo objects, and
                    // the bookInfo in the collection doesn't get some important initialization that
                    // we do to (at least) the AppearanceSettings of the selected book.
                    // So now we wait, if necessary, until we can get the bookInfo from the
                    // appropriate collection.
                    // Note: I think the checks above for inCurrentCollection and inSourceFolder can now
                    // be dropped in favor of just checking that we got a bookInfo here. If it's not in
                    // one of the current collections, we won't get one.
                    var info = _collectionTabView.GetBookInfoByFolderPath(selBookPath);
                    //var info = new BookInfo(selBookPath, inCurrentCollection, _tcManager.CurrentCollectionEvenIfDisconnected ?? new AlwaysEditSaveContext() as ISaveContext);
                    // Fully updating book files ensures that the proper branding files are found for
                    // previewing when the collection settings change but the book selection does not.
                    var book = _bookServer.GetBookFromBookInfo(info);
                    _bookSelection.SelectBook(book);
                }
            }
            // I think we would ideally catch ApplicationException here, but, at least for BL-11678,
            // what we actually get is an Autofac.Core.DependencyResolutionException.
            catch (Exception e)
            {
                // All we are trying to do here is select a book.
                // We certainly don't want to crash because we had a problem doing so.
                // One scenario we know of which causes this is if the book at
                // Settings.Default.CurrentBookPath gets corrupted, such as having no .htm file.
                // See BL-11678.
                Settings.Default.CurrentBookPath = null;

                MiscUtils.SuppressUnusedExceptionVarWarning(e);
            }
        }

        private void HandleBookSelectionChanged(object sender, BookSelectionChangedEventArgs e)
        {
            SendBookSelectionChanged(false);
        }

        private void SendBookSelectionChanged(bool forceNotSaveable)
        {
            var result = GetCurrentSelectedBookInfo(forceNotSaveable);
            // Important for at least the TeamCollectionBookStatusPanel and the CollectionsTabBookPanel.
            _webSocketServer.SendString("book-selection", "changed", result);
        }

        string _tempBookInfoHtmlPath;

        public string GetCurrentSelectedBookInfo(bool forceNotSaveable = false)
        {
            var book = _bookSelection.CurrentSelection;
            var collectionKind = Book.Book.CollectionKind(book);

            string aboutBookInfoUrl = null;
            if (book != null && book.HasAboutBookInformationToShow)
            {
                if (RobustFile.Exists(book.AboutBookHtmlPath))
                {
                    aboutBookInfoUrl = book.AboutBookHtmlPath.ToLocalhost();
                }
                else if (RobustFile.Exists(book.AboutBookMdPath))
                {
                    var mdContent = RobustFile.ReadAllText(book.AboutBookMdPath);
                    var htmlContent = string.Format(
                        "<html><head><meta charset=\"utf-8\"/></head><body>{0}</body></html>",
                        Markdig.Markdown.ToHtml(mdContent)
                    );
                    if (_tempBookInfoHtmlPath != null && RobustFile.Exists(_tempBookInfoHtmlPath))
                        RobustFile.Delete(_tempBookInfoHtmlPath);
                    _tempBookInfoHtmlPath = Path.Combine(
                        Path.GetTempPath(),
                        Path.GetFileNameWithoutExtension(book.AboutBookMdPath) + ".html"
                    );
                    RobustFile.WriteAllText(_tempBookInfoHtmlPath, htmlContent);
                    aboutBookInfoUrl = _tempBookInfoHtmlPath.ToLocalhost();
                }
            }

            var saveable = _bookSelection.CurrentSelection?.IsSaveable ?? false;
            if (forceNotSaveable)
                saveable = false;
            // notify browser components that are listening to this event
            var result = JsonConvert.SerializeObject(
                new
                {
                    id = book?.ID,
                    saveable,
                    collectionKind,
                    aboutBookInfoUrl,
                    isTemplate = book?.IsTemplateBook
                }
            );
            return result;
        }

        public static string MustBeAdminMessage(CollectionSettings collectionSettings)
        {
            return LocalizationManager.GetString(
                    "TeamCollection.MustBeAdmin",
                    "You must be an administrator to change collection settings"
                )
                + "<br><br>"
                + LocalizationManager.GetString(
                    "TeamCollection.AdministratorEmails",
                    "Administrator Emails:"
                )
                + " "
                + collectionSettings.AdministratorsDisplayString;
        }

        private void HandleBookStatusChange(BookStatusChangeEventArgs args)
        {
            var bookName = args.BookName;
            if (_bookSelection.CurrentSelection == null)
                return;
            if (this.IsDisposed)
                return; // We can't need the notification, and Invoke will fail.
            // Notify anything on the Javascript side that might care about the status change.
            // (This includes buttons that are not selected.)
            SafeInvoke.Invoke(
                "sending reload status",
                this,
                false,
                true,
                () =>
                {
                    _webSocketServer.SendEvent("bookTeamCollectionStatus", "reload");
                }
            );
            if (bookName != Path.GetFileName(_bookSelection.CurrentSelection?.FolderPath))
                return; // change is not to the book we're interested in.
            if (_tabStrip.SelectedTab == _reactCollectionTab)
                return; // this toast is all about returning to the collection tab
            if (_returnToCollectionTabNotifier != null)
                return; // notification already up
            if (_tcManager.CurrentCollection == null)
                return;
            if (_tcManager.CurrentCollection.HasClobberProblem(bookName))
            {
                SafeInvoke.Invoke(
                    "sending reload status",
                    this,
                    false,
                    true,
                    () =>
                    {
                        var msg = LocalizationManager.GetString(
                            "TeamCollection.ClobberProblem",
                            "The Team Collection has a newer version of this book. Return to the Collection Tab for more information."
                        );
                        _returnToCollectionTabNotifier = new ToastNotifier();
                        _returnToCollectionTabNotifier.Image.Image = Resources.Error32x32;
                        _returnToCollectionTabNotifier.ToastClicked += (sender, _) =>
                        {
                            _returnToCollectionTabNotifier.CloseSafely();
                            _tabStrip.SelectedTab = _reactCollectionTab;
                        };
                        _returnToCollectionTabNotifier.Show(msg, "", -1);
                    }
                );
            }
        }

        private void SetupZoomControl()
        {
            _zoomControl = new ZoomControl();
            _zoomWrapper = new ToolStripControlHost(_zoomControl);
            // We're using a ToolStrip to display these three controls in the top right, and it does a nice job
            // of stretching the width to match localization. But height and spacing we must control exactly,
            // or it goes into an overflow mode that is very ugly.
            _zoomWrapper.Margin = Padding.Empty;
            // Provide access for javascript to adjust this control via the EditingView and EditingModel.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-5584.
            _editingView.SetZoomControl(_zoomControl);
        }

        private int TabButtonSectionWidth
        {
            get { return _tabStrip.Items.Cast<TabStripButton>().Sum(tab => tab.Width) + 10; }
        }

        /// <summary>
        /// Adjusts the tool panel location to allow more (or optionally less) space
        /// for the tab buttons.
        /// </summary>
        void AdjustToolPanelLocation(bool allowNarrowing)
        {
            var widthOfTabButtons = TabButtonSectionWidth;
            var location = _toolSpecificPanel.Location;
            if (widthOfTabButtons > location.X || allowNarrowing)
            {
                location.X = widthOfTabButtons;
                _toolSpecificPanel.Location = location;
            }
        }

        /// <summary>
        /// Adjust the tool panel location when the chosen localization changes.
        /// See https://jira.sil.org/browse/BL-1212 for what can happen if we
        /// don't adjust.  At the moment, we only widen, we never narrow the
        /// overall area allotted to the tab buttons.  Button widths adjust
        /// themselves automatically to their Text width.  There doesn't seem
        /// to be a built-in mechanism to limit the width to a given maximum
        /// so we implement such an operation ourselves.
        /// </summary>
        void HandleTabTextChanged(object sender, EventArgs e)
        {
            var btn = sender as TabStripButton;
            if (btn != null)
            {
                const string kEllipsis = "\u2026";
                // Preserve the original string as the tooltip.
                if (!btn.Text.EndsWith(kEllipsis))
                    btn.ToolTipText = btn.Text;
                // Ensure the button width is no more than 110 pixels.
                if (btn.Width > 110)
                {
                    using (Graphics g = btn.Owner.CreateGraphics())
                    {
                        btn.Text = ShortenStringToFit(btn.Text, 110, btn.Width, btn.Font, g);
                    }
                }
            }
            AdjustToolPanelLocation(false);
        }

        /// <summary>
        /// Ensure that the TabStripItem or Control or Whatever is no wider than desired by
        /// truncating the Text as needed, with an ellipsis appended to show truncation has
        /// occurred.
        /// </summary>
        /// <returns>the possibly shortened string</returns>
        /// <param name="text">the string to shorten if necessary</param>
        /// <param name="maxWidth">the maximum item width allowed</param>
        /// <param name="originalWidth">the original item width (with the original string)</param>
        /// <param name="font">the font to use</param>
        /// <param name="g">the relevant Graphics object for drawing/measuring</param>
        /// <remarks>Would this be a good library method somewhere?  Where?</remarks>
        public static string ShortenStringToFit(
            string text,
            int maxWidth,
            int originalWidth,
            Font font,
            Graphics g
        )
        {
            const string kEllipsis = "\u2026";
            var txtWidth = TextRenderer.MeasureText(g, text, font).Width;
            var padding = originalWidth - txtWidth;
            while (txtWidth + padding > maxWidth)
            {
                var len = text.Length - 2;
                if (len <= 0)
                    break; // I can't conceive this happening, but I'm also paranoid.
                text = text.Substring(0, len) + kEllipsis; // trim, add ellipsis
                txtWidth = TextRenderer.MeasureText(g, text, font).Width;
            }
            return text;
        }

        private void _applicationUpdateCheckTimer_Tick(object sender, EventArgs e)
        {
            _applicationUpdateCheckTimer.Enabled = false;
            if (!Debugger.IsAttached && Platform.IsWindows)
            {
                ApplicationUpdateSupport.CheckForASquirrelUpdate(
                    ApplicationUpdateSupport.BloomUpdateMessageVerbosity.Quiet,
                    newInstallDir => RestartBloom(newInstallDir),
                    Settings.Default.AutoUpdate
                );
            }
        }

        void OnSettingsProtectionChanged(object sender, PropertyChangedEventArgs e)
        {
            //when we need to use Ctrl+Shift to display stuff, we don't want it also firing up the localization dialog (which shouldn't be done by a user under settings protection anyhow)

            // Commented out due to BL-5111
            //LocalizationManager.EnableClickingOnControlToBringUpLocalizationDialog = !SettingsProtectionSettings.Default.NormallyHidden;
        }

        ToolStripMenuItem _showAllTranslationsItem;

        private void SetupUiLanguageMenu(bool onlyActiveItem = false)
        {
            SetupUiLanguageMenuCommon(
                _uiLanguageMenu,
                FinishUiLanguageMenuItemClick,
                onlyActiveItem
            );

            // REVIEW: should this be part of SetupUiLanguageMenuCommon()?  should it be added only for alpha and beta?
            _uiLanguageMenu.DropDownItems.Add("-");
            _showAllTranslationsItem = new ToolStripMenuItem();
            _showAllTranslationsItem.Text = GetShowUnapprovedTranslationsMenuText();
            _showAllTranslationsItem.Checked = Settings.Default.ShowUnapprovedLocalizations;
            _showAllTranslationsItem.Click += (sender, args) =>
                ToggleShowingOnlyApprovedTranslations();
            _uiLanguageMenu.DropDownItems.Add(_showAllTranslationsItem);

            _uiLanguageMenu.DropDown.Closing += DropDown_Closing;
            _helpMenu.DropDown.Closing += DropDown_Closing;
            _uiLanguageMenu.DropDown.Opening += DropDown_Opening;
            _helpMenu.DropDown.Opening += DropDown_Opening;
            // one side-effect of the above is if the _uiLanguageMenu dropdown is open, a click on the _helpMenu won't close it
            // (and vice versa)
            _helpMenu.Click += (sender, args) =>
                _uiLanguageMenu.DropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            _uiLanguageMenu.Click += (sender, e) =>
                _helpMenu.DropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void ToggleShowingOnlyApprovedTranslations()
        {
            Settings.Default.ShowUnapprovedLocalizations = !Settings
                .Default
                .ShowUnapprovedLocalizations;
            LocalizationManager.ReturnOnlyApprovedStrings = !Settings
                .Default
                .ShowUnapprovedLocalizations;
            SetupUiLanguageMenu();
            FinishUiLanguageMenuItemClick(); // apply newly revealed/hidden localizations
            // until L10nSharp changes to allow dynamic response to setting change
            Settings.Default.Save();
            Program.RestartBloom(false);
        }

        private bool _ignoreNextAppFocusChange;

        /// <summary>
        /// Prevent undesirable closing of dropdown menus.  This is worth losing some desired
        /// closings, especially for Linux/Gnome in which the menus refuse to stay open at all
        /// without this fix.
        /// </summary>
        /// <remarks>
        /// See https://silbloom.myjetbrains.com/youtrack/issue/BL-5471.
        /// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6107.
        /// The exact behavior seems rather system dependent.
        /// </remarks>
        private void DropDown_Closing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (e.CloseReason)
            {
                case ToolStripDropDownCloseReason.AppFocusChange:
                    // this is usually just hovering over the help menu on Windows.
                    // there is always one spurious focus change on Linux/Gnome.
                    e.Cancel = _ignoreNextAppFocusChange;
                    break;
                case ToolStripDropDownCloseReason.AppClicked:
                // "reason" is AppClicked, but is it legit?
                // Every other time we get AppClicked even if we are just hovering over the help menu.
                case ToolStripDropDownCloseReason.Keyboard:
                    // If "reason" is Keyboard, that seems to be generated just by moving the mouse over the
                    // adjacent (visible) button on Linux.
                    var mousePos = _helpMenu.Owner.PointToClient(MousePosition);
                    var bounds =
                        (sender == _helpMenu.DropDown) ? _uiLanguageMenu.Bounds : _helpMenu.Bounds;
                    if (bounds.Contains(mousePos))
                    {
                        // probably a false positive
                        e.Cancel =
                            e.CloseReason == ToolStripDropDownCloseReason.AppClicked
                            || Platform.IsLinux;
                    }
                    break;
                default: // includes ItemClicked, CloseCalled
                    break;
            }
            _ignoreNextAppFocusChange = Platform.IsWindows; // preserve the fix for BL-5476.
            Debug.WriteLine(
                "DEBUG WorkspaceView.DropDown_Closing: reason={0}, cancel={1}",
                e.CloseReason.ToString(),
                e.Cancel
            );
        }

        void DropDown_Opening(object sender, CancelEventArgs e)
        {
            _ignoreNextAppFocusChange = true;
        }

        /// <summary>
        /// This is also called by CollectionChoosing.OpenCreateCloneControl
        /// </summary>
        public static void SetupUiLanguageMenuCommon(
            ToolStripDropDownButton uiMenuControl,
            Action finishClickAction = null,
            bool onlyActiveItem = false
        )
        {
            var items = new List<LanguageItem>();
            if (onlyActiveItem)
            {
                if (String.IsNullOrEmpty(Settings.Default.UserInterfaceLanguage))
                    Settings.Default.UserInterfaceLanguage = "en"; // See BL-13545.
                items.Add(CreateLanguageItem(Settings.Default.UserInterfaceLanguage));
            }
            else
            {
                foreach (var lang in LocalizationManager.GetAvailableLocalizedLanguages())
                {
                    // Require that at least 1% of the strings have been translated and approved for alphas,
                    // or 25% translated and approved for betas and release.
                    var approved = FractionApproved(lang);
                    if (Settings.Default.ShowUnapprovedLocalizations)
                        approved = FractionTranslated(lang);
                    var alpha = ApplicationUpdateSupport.IsDevOrAlpha;
                    if ((alpha && approved < 0.01F) || (!alpha && approved < 0.25F))
                        continue;
                    items.Add(CreateLanguageItem(lang));
                }
            }

            items.Sort(compareLangItems);

            var tooltipFormat = LocalizationManager.GetString(
                "CollectionTab.UILanguageMenu.ItemTooltip",
                "{0}% translated",
                "Shown when hovering over an item in the UI Language menu.  The {0} marker is filled in by a number between 1 and 100."
            );
            uiMenuControl.DropDownItems.Clear();
            foreach (var langItem in items)
            {
                var item = uiMenuControl.DropDownItems.Add(langItem.MenuText);
                item.Tag = langItem;
                var fraction = langItem.FractionApproved;
                if (Settings.Default.ShowUnapprovedLocalizations)
                    fraction = langItem.FractionTranslated;
                item.ToolTipText = String.Format(tooltipFormat, (int)(fraction * 100.0F));
                item.Click += (sender, args) =>
                    UiLanguageMenuItemClickHandler(
                        uiMenuControl,
                        sender as ToolStripItem,
                        finishClickAction
                    );
                if (langItem.LangTag == Settings.Default.UserInterfaceLanguage)
                    UpdateMenuTextToShorterNameOfSelection(uiMenuControl, langItem.MenuText);
            }
            uiMenuControl.DropDownItems.Add("-"); // adds ToolStripSeparator
            var message = LocalizationManager.GetString(
                "CollectionTab.UILanguageMenu.HelpTranslate",
                "Help us translate Bloom (web)",
                "The final item in the UI Language menu. When clicked, it opens Bloom's page in the Crowdin web-based translation system."
            );
            var helpItem = uiMenuControl.DropDownItems.Add(message);
            helpItem.Image = Resources.weblink;
            helpItem.Click += (sender, args) =>
                ProcessExtra.SafeStartInFront(UrlLookup.LookupUrl(UrlType.LocalizingSystem, null));
        }

        private static int compareLangItems(LanguageItem a, LanguageItem b)
        {
            var aText = a.MenuText;
            if (!CharacterUtils.IsLatinChar(aText[0]))
                aText = a.EnglishName;
            var bText = b.MenuText;
            if (!CharacterUtils.IsLatinChar(bText[0]))
                bText = b.EnglishName;
            return String.Compare(
                aText.ToLowerInvariant(),
                bText.ToLowerInvariant(),
                StringComparison.Ordinal
            );
        }

        private static void UiLanguageMenuItemClickHandler(
            ToolStripDropDownButton toolStripButton,
            ToolStripItem item,
            Action finishClickAction
        )
        {
            var tag = (LanguageItem)item.Tag;

            LocalizationManager.SetUILanguage(tag.LangTag, true);
            // TODO-WV2: Can we set the browser language in WV2?  Do we need to?
            Settings.Default.UserInterfaceLanguage = tag.LangTag;
            Settings.Default.UserInterfaceLanguageSetExplicitly = true;
            Settings.Default.Save();
            item.Select();
            UpdateMenuTextToShorterNameOfSelection(toolStripButton, item.Text);

            finishClickAction?.Invoke();
        }

        private void FinishUiLanguageMenuItemClick()
        {
            // these lines deal with having a smaller workspace window and minimizing the button texts for smaller windows
            SaveOriginalButtonTexts();
            AdjustButtonTextsForLocale();
            _showAllTranslationsItem.Text = GetShowUnapprovedTranslationsMenuText();
            _localizationChangedEvent.Raise(null);
        }

        private string GetShowUnapprovedTranslationsMenuText()
        {
            return LocalizationManager.GetString(
                "CollectionTab.LanguageMenu.ShowUnapprovedTranslations",
                "Show translations which have not been approved yet"
            );
        }

        public static LanguageItem CreateLanguageItem(string code)
        {
            // Get the language name in its own language if at all possible.
            // Add an English name suffix if it's not in a Latin script.
            var menuText = IetfLanguageTag.GetNativeLanguageNameWithEnglishSubtitle(code);
            var englishName = IetfLanguageTag.GetManuallyOverriddenEnglishNameIfNeeded(
                code,
                () => IetfLanguageTag.GetLocalizedLanguageName(code, "en")
            );
            return new LanguageItem
            {
                EnglishName = englishName,
                LangTag = code,
                MenuText = menuText,
                FractionApproved = FractionApproved(code),
                FractionTranslated = FractionTranslated(code)
            };
        }

        /// <summary>
        /// LocalizationManager.FractionApproved(code) divides by the number of English strings
        /// which is always larger because it includes strings from outside Bloom and Palaso
        /// that have been dynamically discovered.  There are some other things going on in
        /// the dynamic scanning that result in strings being duplicated in multiple .xlf files.
        /// So we calculate the number based solely on the particular language's counts.  Since
        /// Crowdin is supposed to pass through all strings to all localizations, this should be
        /// fairly accurate and give numbers similar to what Crowdin reports.  (Crowdin counts by
        /// word instead of by string.)
        /// </summary>
        public static float FractionApproved(string code)
        {
            var totalCount = LocalizationManager.StringCount(code);
            var approvedCount = LocalizationManager.NumberApproved(code);
            return (float)approvedCount / (float)totalCount;
        }

        /// <summary>
        /// LocalizationManager.FractionTranslated(code) divides by the number of English strings
        /// which is always larger because it includes strings from outside Bloom and Palaso
        /// that have been dynamically discovered.  So we use the language's counts directly to
        /// compute this fraction.
        /// </summary>
        public static float FractionTranslated(string code)
        {
            var totalCount = LocalizationManager.StringCount(code);
            var translatedCount = LocalizationManager.NumberTranslated(code);
            return (float)translatedCount / (float)totalCount;
        }

        public static void UpdateMenuTextToShorterNameOfSelection(
            ToolStripDropDownButton toolStripButton,
            string itemText
        )
        {
            var idxChinese = itemText.IndexOf(" (Chinese");
            if (idxChinese > 0)
            {
                toolStripButton.Text = itemText.Substring(0, idxChinese);
            }
            else
            {
                var idxCountry = itemText.IndexOf(" (");
                if (idxCountry > 0)
                    toolStripButton.Text = itemText.Substring(0, idxCountry);
                else
                {
                    toolStripButton.Text = itemText;
                }
            }
        }

        private void OnEditBook(Book.Book book)
        {
            _tabStrip.SelectedTab = _editTab;
        }

        public bool InEditMode => _tabStrip.SelectedTab == _editTab;

        public bool InCollectionTab => _tabStrip.SelectedTab == _reactCollectionTab;

        internal bool IsInTabStrip(Point pt)
        {
            return _tabStrip != null && _tabStrip.DisplayRectangle.Contains(pt);
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            Application.Idle -= Application_Idle;
        }

        private void OnUpdateDisplay(object sender, EventArgs e)
        {
            SetTabVisibility(_editTab, _model.ShowEditTab);
            SetTabVisibility(_publishTab, _model.ShowPublishTab);
            _editTab.Enabled = !_model.EditTabLocked;
        }

        // Called early in checkin, will be re-updated by OnUpdateDisplay later in checkin,
        // but we need to disable it right away when losing permission to edit.
        public void DisableEditTab()
        {
            _editTab.Enabled = false;
            // This disables the Edit button.
            SendBookSelectionChanged(forceNotSaveable: true);
        }

        private void SetTabVisibility(TabStripButton tab, bool visible)
        {
            tab.Visible = visible;
        }

        public void OpenCreateCollection()
        {
            _settingsLauncherHelper.LaunchSettingsIfAppropriate(() =>
            {
                _selectedTabAboutToChangeEvent.Raise(
                    new TabChangedDetails() { From = _previouslySelectedControl, To = null }
                );

                _selectedTabChangedEvent.Raise(
                    new TabChangedDetails() { From = _previouslySelectedControl, To = null }
                );

                _previouslySelectedControl = null;

                Invoke(
                    (Action)(
                        () => Program.ChooseACollection(Shell.GetShellOrOtherOpenForm() as Shell)
                    )
                );
                return DialogResult.OK;
            });
        }

        internal void OnLegacySettingsButton_Click(object sender, EventArgs e)
        {
            OpenLegacySettingsDialog();
        }

        private CollectionSettingsDialog _currentlyOpenSettingsDialog;

        public void OpenLegacySettingsDialog(
            string tab = null,
            bool forFixingEnterpriseSubscription = false
        )
        {
            if (InvokeRequired)
            {
                SafeInvoke.Invoke(
                    "OpenSettingsDialog",
                    this,
                    true,
                    false,
                    (() => OpenLegacySettingsDialog(tab))
                );
            }
            else
            {
                if (_currentlyOpenSettingsDialog != null)
                {
                    _currentlyOpenSettingsDialog.SetDesiredTab(tab);
                    return;
                }

                DialogResult result = _settingsLauncherHelper.LaunchSettingsIfAppropriate(() =>
                {
                    if (!_tcManager.OkToEditCollectionSettings)
                    {
                        BloomMessageBox.ShowInfo(MustBeAdminMessage(_collectionSettings));
                        return DialogResult.Cancel;
                    }
                    _collectionSettingsApi.PrepareToShowDialog();
                    using (var dlg = _settingsDialogFactory())
                    {
                        dlg.FixingEnterpriseSubscriptionCode = forFixingEnterpriseSubscription;
                        _currentlyOpenSettingsDialog = dlg;
                        dlg.SetDesiredTab(tab);
                        var temp = dlg.ShowDialog(this);
                        _currentlyOpenSettingsDialog = null;
                        CollectionSettingsApi.DialogClosed();
                        return temp;
                    }
                });
                if (result == DialogResult.Yes)
                {
                    Invoke(ReopenCurrentProject);
                }
            }
        }

        public void CheckForInvalidBranding()
        {
            if (_collectionSettings.InvalidBranding == null || _collectionSettings.IgnoreExpiration)
                return;
            // I'm not very happy with this, but the only place I could find to detect that we're opening a new project
            // is too soon to bring up a dialog; it comes up before the main window is fully initialized, which can
            // leave the main window in the wrong place. Waiting until idle gives a much better effect.
            StartupScreenManager.AddStartupAction(
                () =>
                {
                    OpenLegacySettingsDialog("enterprise", forFixingEnterpriseSubscription: true);
                },
                shouldHideSplashScreen: true,
                lowPriority: true
            );
        }

        private void SelectPage(Control view)
        {
            // Already on the desired page: nothing to do.  And possible problems if we do do something.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-8382.
            if (view == _previouslySelectedControl)
                return;

            CurrentTabView = view as IBloomTabArea;
            // Warn the user if we're starting to use too much memory.
            MemoryManagement.CheckMemory(false, "switched tab in workspace", true);

            if (_previouslySelectedControl != null)
            {
                _containerPanel.Controls.Remove(_previouslySelectedControl);
                if (_previouslySelectedControl is EditingView)
                {
                    // I wish this was unnecessary; ideally, we'd get the notification to
                    // stop monitoring from the stopMonitoring function in audioRecording.ts.
                    // We should be able to achieve that when the tabs are embedded in a single
                    // Browser control. For now, the shutdown of the EditingView seems to
                    // preempt it, so we handle it here.
                    _audioRecording.PauseMonitoringAudio(false);
                }
            }

            view.Dock = DockStyle.Fill;
            _containerPanel.Controls.Add(view);

            _toolSpecificPanel.Controls.Clear();

            _panelHoldingToolStrip.BackColor = CurrentTabView.TopBarControl.BackColor =
                _tabStrip.BackColor;

            if (Platform.IsMono)
            {
                BackgroundColorsForLinux(CurrentTabView);
            }

            if (CurrentTabView != null) //can remove when we get rid of info view
            {
                CurrentTabView.PlaceTopBarControl();
                _toolSpecificPanel.Controls.Add(CurrentTabView.TopBarControl);
                CurrentTabView.TopBarControl.Dock = DockStyle.Fill;
            }

            _selectedTabAboutToChangeEvent.Raise(
                new TabChangedDetails()
                {
                    From = _previouslySelectedControl,
                    To = view,
                    PostponedWork = () =>
                    {
                        _selectedTabChangedEvent.Raise(
                            new TabChangedDetails() { From = _previouslySelectedControl, To = view }
                        );

                        _previouslySelectedControl = view;
                        _collectionApi.ResetUpdatingList();

                        var zoomManager = CurrentTabView as IZoomManager;
                        if (zoomManager != null)
                        {
                            if (!_toolStrip.Items.Contains(_zoomWrapper))
                                _toolStrip.Items.Add(_zoomWrapper);
                            _zoomControl.Zoom = zoomManager.Zoom;
                            _zoomControl.ZoomChanged += (sender, args) =>
                                zoomManager.SetZoom(_zoomControl.Zoom);
                        }
                        else
                        {
                            if (_toolStrip.Items.Contains(_zoomWrapper))
                                _toolStrip.Items.Remove(_zoomWrapper);
                        }
                        // TODO-WV2: Can we clear the cache in WV2?  Do we need to?
                    }
                }
            );
        }

        private void BackgroundColorsForLinux(IBloomTabArea currentTabView)
        {
            if (currentTabView.ToolStripBackground == null)
            {
                var bmp = new Bitmap(_toolStrip.Width, _toolStrip.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    using (var b = new SolidBrush(_panelHoldingToolStrip.BackColor))
                    {
                        g.FillRectangle(b, 0, 0, bmp.Width, bmp.Height);
                    }
                }
                currentTabView.ToolStripBackground = bmp;
            }

            _toolStrip.BackgroundImage = currentTabView.ToolStripBackground;
        }

        protected IBloomTabArea CurrentTabView { get; set; }

        private void _tabStrip_SelectedTabChanged(object sender, SelectedTabChangedEventArgs e)
        {
            if (_tabStrip.SelectedTab == _editTab)
                _tabSelection.ActiveTab = WorkspaceTab.edit;
            else if (_tabStrip.SelectedTab == _publishTab)
                _tabSelection.ActiveTab = WorkspaceTab.publish;
            else
                _tabSelection.ActiveTab = WorkspaceTab.collection;
            if (
                _returnToCollectionTabNotifier != null
                && _tabStrip.SelectedTab == _reactCollectionTab
            )
            {
                _returnToCollectionTabNotifier.CloseSafely();
                _returnToCollectionTabNotifier = null;
            }
            TabStripButton btn = (TabStripButton)e.SelectedTab;
            _tabStrip.BackColor = btn.BarColor;
            _toolSpecificPanel.BackColor = _panelHoldingToolStrip.BackColor = _tabStrip.BackColor;
            Logger.WriteEvent("Selecting Tab Page: " + e.SelectedTab.Name);
            SelectPage((Control)e.SelectedTab.Tag);
            AdjustTabStripDisplayForScreenSize();
            if (_tabSelection.ActiveTab == WorkspaceTab.collection && _collectionTabView != null)
            {
                if (Publish.BloomLibrary.BloomLibraryPublishModel.BookUploaded)
                {
                    // update bloom library status for the either the selected book or the entire collection.
                    _collectionTabView.UpdateBloomLibraryStatus(
                        Publish.BloomLibrary.BloomLibraryPublishModel.BookUploadedId
                    );
                    Publish.BloomLibrary.BloomLibraryPublishModel.BookUploaded = false;
                    Publish.BloomLibrary.BloomLibraryPublishModel.BookUploadedId = null;
                }
            }
        }

        public void ChangeTab(WorkspaceTab newTab)
        {
            switch (newTab)
            {
                case WorkspaceTab.edit:
                    _tabStrip.SelectedTab = _editTab;
                    break;
                case WorkspaceTab.collection:
                    _tabStrip.SelectedTab = _reactCollectionTab;
                    break;
                case WorkspaceTab.publish:
                    _tabStrip.SelectedTab = _publishTab;
                    break;
            }
        }

        private void _tabStrip_BackColorChanged(object sender, EventArgs e)
        {
            //_topBarButtonTable.BackColor = _toolSpecificPanel.BackColor =  _tabStrip.BackColor;
        }

        private void OnAboutBoxClick(object sender, EventArgs e)
        {
            string path = BloomFileLocator.GetBrowserFile(
                true,
                "infoPages",
                "aboutBox-" + LocalizationManager.UILanguageId + ".htm"
            );
            if (String.IsNullOrEmpty(path))
            {
                path = BloomFileLocator.GetBrowserFile(false, "infoPages", "aboutBox.htm");
            }
            using (var dlg = new SILAboutBox(path))
            {
                dlg.ShowDialog();
            }
        }

        private void toolStripMenuItem3_Click(object sender, EventArgs e)
        {
            HelpLauncher.Show(this, CurrentTabView.HelpTopicUrl);
        }

        private void _bloom_docs_Click(object sender, EventArgs e)
        {
            SIL.Program.Process.SafeStart("https://docs.bloomlibrary.org");
        }

        private void _webSiteMenuItem_Click(object sender, EventArgs e)
        {
            ProcessExtra.SafeStartInFront(UrlLookup.LookupUrl(UrlType.LibrarySite, null));
        }

        private void _releaseNotesMenuItem_Click(object sender, EventArgs e)
        {
            SIL.Program.Process.SafeStart("https://docs.bloomlibrary.org/Release-Notes");
        }

        private void _requestAFeatureMenuItem_Click(object sender, EventArgs e)
        {
            ProcessExtra.SafeStartInFront(UrlLookup.LookupUrl(UrlType.UserSuggestions, null));
        }

        private void _askAQuestionMenuItem_Click(object sender, EventArgs e)
        {
            ProcessExtra.SafeStartInFront(UrlLookup.LookupUrl(UrlType.Support, null));
        }

        // Currently not used, but I'm leaving the method in case we want to put it
        // back in for debug or alpha builds, etc.
        private void _showLogMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                Logger.ShowUserTheLogFile();
            }
            catch (Exception) { }
        }

        private void WorkspaceView_Resize(object sender, EventArgs e)
        {
            if (this.ParentForm != null && this.ParentForm.WindowState != FormWindowState.Minimized)
            {
                AdjustTabStripDisplayForScreenSize();
            }
        }

        private void WorkspaceView_Load(object sender, EventArgs e)
        {
            CheckDPISettings();
            _originalToolStripPanelWidth = 0;
            _viewInitialized = true;
            ShowAutoUpdateDialogIfNeeded();
            ShowForumInvitationDialogIfNeeded();
            // Whether we showed the dialog or not we'll check for a new version in 1 minute.
            _applicationUpdateCheckTimer.Enabled = true;
        }

        private const int kCurrentAutoUpdateVersion = 1;

        private void ShowAutoUpdateDialogIfNeeded()
        {
            if (Platform.IsLinux)
                return;
            // If Bloom is newly installed or we only had old versions before, this should be 0.
            var isShown = Settings.Default.AutoUpdateDialogShown;
            if (isShown < kCurrentAutoUpdateVersion)
            {
                // It's tempting to make the whole process of calling this function a startup action,
                // but until we actually decide whether to show it, we don't know whether we need to
                // hide the splash screen. This is as much as we can postpone.
                StartupScreenManager.AddStartupAction(
                    () =>
                    {
                        using (
                            var dlg = new ReactDialog("autoUpdateSoftwareDlgBundle", "Auto Update")
                        )
                        {
                            dlg.Height = 250;
                            dlg.Width = 500;
                            dlg.ShowDialog(this);
                        }
                    },
                    shouldHideSplashScreen: true,
                    lowPriority: false
                );
            }
        }

        private void ShowForumInvitationDialogIfNeeded()
        {
            if (Settings.Default.ForumInvitationAcknowledged)
                return;
            var lastShown = Settings.Default.ForumInvitationLastShown;
            var today = DateTime.Now;
            if (lastShown == DateTime.MinValue)
            {
                // If Bloom is newly installed or we only had old versions before,
                // wait 2 weeks before showing this nagging dialog.
                Settings.Default.ForumInvitationLastShown = today;
                Settings.Default.Save();
                return;
            }
            // Show once every two weeks until the user gives up and acknowledges it.
            if (today.Subtract(lastShown).TotalDays > 13)
            {
                // It's tempting to make the whole process of calling this function a startup action,
                // but until we actually decide whether to show it, we don't know whether we need to
                // hide the splash screen. This is as much as we can postpone.
                AppApi.OpenDialogs.AddOrUpdate("ForumInvitationDialog", 0, (key, val) => val);
                StartupScreenManager.AddStartupAction(
                    () =>
                    {
                        AppApi.OpenDialogs.AddOrUpdate(
                            "ForumInvitationDialog",
                            1,
                            (key, val) => val + 1
                        );
                        if (
                            AppApi.OpenDialogs.TryGetValue("ForumInvitationDialog", out var count)
                            && count > 1
                        )
                            return;
                        Settings.Default.ForumInvitationLastShown = today;
                        Settings.Default.Save();
                        _webSocketServer.LaunchDialog("ForumInvitationDialog", new DynamicJson());
                    },
                    shouldHideSplashScreen: true,
                    lowPriority: true,
                    needsToRun: () =>
                    {
                        // The startup task is called repeatedly until this function returns false.
                        var shouldShow = AppApi.OpenDialogs.TryGetValue(
                            "ForumInvitationDialog",
                            out var count
                        );
                        return shouldShow && count < 2;
                    },
                    // We need to wait for the "collectionButtonsDrawn" milestone to ensure that the collection
                    // tab has rendered sufficiently to hook up the socket event handler for the dialog.
                    // See September 18-19 comments in https://issues.bloomlibrary.org/youtrack/issue/BL-12410.
                    // But we need to allow a number of ticks for the milestone to show up.  A delay of 100
                    // ticks seems to work well.  A debug build on an older development machine recorded 4654
                    // ticks in about a minute.
                    waitForMilestone: "collectionButtonsDrawn",
                    maxTickWaitForMilestone: 100
                );
            }
        }

        private void OnRegistrationMenuItem_Click(object sender, EventArgs e)
        {
            using (var dlg = new RegistrationDialog(true, _tcManager.UserMayChangeEmail))
            {
                dlg.ShowDialog();
            }
        }

        private void CheckDPISettings()
        {
            Graphics g = this.CreateGraphics();
            try
            {
                var dx = g.DpiX;
                DPIOfThisAccount = dx;
                var dy = g.DpiY;
                if (dx != 96 || dy != 96)
                {
                    ErrorReport.NotifyUserOfProblem(
                        new ShowOncePerSessionBasedOnExactMessagePolicy(),
                        "The \"text size (DPI)\" or \"Screen Magnification\" of the display on this computer is set to a special value, {0}. With that setting, some thing won't look right in Bloom. Possibly books won't lay out correctly. If this is a problem, change the DPI back to 96 (the default on most computers), using the 'Display' Control Panel.",
                        dx
                    );
                }
            }
            finally
            {
                g.Dispose();
            }
        }

        public void CheckForCollectionUpdates()
        {
            _collectionApi.CheckForCollectionUpdates();
        }

        public void CheckForUpdates()
        {
            Invoke((Action)(() => _checkForNewVersionMenuItem_Click(this, new EventArgs())));
        }

        private void _checkForNewVersionMenuItem_Click(object sender, EventArgs e)
        {
            if (ApplicationUpdateSupport.BloomUpdateInProgress)
            {
                //enhance: ideally, what this would do is show a toast of whatever it is squirrel is doing: checking, downloading, waiting for a restart.
                MessageBox.Show(
                    this,
                    LocalizationManager.GetString(
                        "CollectionTab.UpdateCheckInProgress",
                        "Bloom is already working on checking for updates."
                    )
                );
                return;
            }
            if (Debugger.IsAttached)
            {
                MessageBox.Show(this, "Sorry, you cannot check for updates from the debugger.");
            }
            else if (InstallerSupport.SharedByAllUsers())
            {
                MessageBox.Show(
                    this,
                    LocalizationManager.GetString(
                        "CollectionTab.AdminManagesUpdates",
                        "Your system administrator manages Bloom updates for this computer."
                    )
                );
            }
            else if (ApplicationUpdateSupport.IsDev)
            {
                MessageBox.Show(
                    this,
                    "Checking for updates is disabled on developer builds. No relevant channel."
                );
            }
            else
            {
                ApplicationUpdateSupport.CheckForASquirrelUpdate(
                    ApplicationUpdateSupport.BloomUpdateMessageVerbosity.Verbose,
                    newInstallDir => RestartBloom(newInstallDir),
                    Settings.Default.AutoUpdate
                );
            }
        }

        private void RestartBloom(string newInstallDir)
        {
            Control ancestor = Parent;
            while (ancestor != null && !(ancestor is Shell))
                ancestor = ancestor.Parent;
            if (ancestor == null)
                return;
            var shell = (Shell)ancestor;
            var pathToNewExe = Path.Combine(
                newInstallDir,
                Path.ChangeExtension(Application.ProductName, ".exe")
            );
            if (!RobustFile.Exists(pathToNewExe))
                return; // aargh!
            shell.QuitForVersionUpdate = true;
            Process.Start(pathToNewExe);
            Thread.Sleep(2000);
            shell.Close();
        }

        private static void OpenInfoFile(string fileName)
        {
            // These are PDF files, but stored under browser/infoPages.
            ProcessExtra.SafeStartInFront(
                BloomFileLocator.GetBrowserFile(false, "infoPages", fileName)
            );
        }

        private void buildingReaderTemplatesMenuItem_Click(object sender, EventArgs e)
        {
            OpenInfoFile("Building and Distributing Reader Templates in Bloom.pdf");
        }

        private void usingReaderTemplatesMenuItem_Click(object sender, EventArgs e)
        {
            OpenInfoFile("Using Bloom Reader Templates.pdf");
        }

        private void _reportAProblemMenuItem_Click(object sender, EventArgs e)
        {
            // Screen shots were showing the menu still open on Linux, so delay a bit by starting the
            // dialog on the next idle loop.  Also allow one repaint event to be handled immediately.
            // (This method has to return for the menu to fully hide itself on Linux.)
            // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3792.
            Application.DoEvents();
            Application.Idle += StartProblemReport;
        }

        private void StartProblemReport(object sender, EventArgs e)
        {
            Application.Idle -= StartProblemReport;

            // Try to ensure latest changes in book are included in report.  (BL-10480)
            try
            {
                if (_editTab.IsSelected)
                {
                    _editingView.Model.SaveThen(
                        () =>
                        {
                            // To test the Problem Dialog with a fatal error, uncomment this next line.
                            // throw new ApplicationException("I just felt like an error!");

                            // To test the Problem Dialog with a nonfatal error, uncomment this next line.
                            // NonFatalProblem.Report(ModalIf.All, PassiveIf.All, "My test 'yellow screen' error", "Any more details here?");
                            // To test clicking 'Report' in a toast, uncomment the line above, but use ModalIf.None.

                            // To test the old ErrorReport.NotifyUserOfProblem, uncomment this next line.
                            // ErrorReport.NotifyUserOfProblem(new ApplicationException("internal exception message"), "My main message");
                            ReportAndLogProblem();
                            return _editingView.Model.CurrentPage.Id;
                        },
                        () => { } // wrong state, do nothing
                    );
                }
                else
                {
                    ReportAndLogProblem();
                }
            }
            catch
            {
                // Ignore errors saving. (But this may miss problems while responding to getting the page content.)
                ReportAndLogProblem();
            }
        }

        private void ReportAndLogProblem()
        {
            Logger.WriteEvent("User clicked the 'Report a Problem' menu item");
            ProblemReportApi.ShowProblemDialog(this, null);
        }

        public void SetStateOfNonPublishTabs(bool enable)
        {
            if (_reactCollectionTab != null)
                _reactCollectionTab.Enabled = enable;
            _editTab.Enabled = enable;
        }

        private void _trainingVideosMenuItem_Click(object sender, EventArgs e)
        {
            //note: markdown processors pass raw html through unchanged.  Bloom's localization process
            // is designed to produce HTML files, not Markdown files.
            var path = BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(
                false,
                "infoPages",
                "TrainingVideos-en.htm"
            );
            using (var dlg = new ShowReleaseNotesDialog(Resources.BloomIcon, path))
            {
                dlg.ApplyMarkdown = false;
                dlg.Text = LocalizationManager.GetString(
                    "HelpMenu.trainingVideos",
                    "Training Videos"
                );
                dlg.ShowDialog();
            }
        }

        #region Responsive Toolbar

        enum Shrinkage
        {
            FullSize,
            Stage1,
            Stage2,
            Stage3
        }

        private Shrinkage _currentShrinkage = Shrinkage.FullSize;
        private ToolStripControlHost _zoomWrapper;

        private const int MinToolStripMargin = 3;

        // The width of the toolstrip panel in stage 1 is typically its original width, which leaves a bit of margin
        // left of the toolstrip. If a long language name requires more width than typical, make it at least wide
        // enough to hold the language name. In the latter case, stage 2 won't do anything, and we will move right
        // on to stage 3 if stage 1 isn't enough.
        private int Stage_1WidthOfToolStringPanel =>
            Math.Max(_originalToolStripPanelWidth, _toolStrip.Width + MinToolStripMargin);

        // The width at which we switch to stage 1: the actual space needed for the controls in the top panel,
        // when each is in its widest form and the preferred extra space is between the tab controls and the TopBarControl.
        // Since this is meant to be BEFORE we push the _toolSpecificPanel up against the tabs, we use its location
        // rather than the with of the tabs.
        private int STAGE_1 =>
            _originalToolSpecificPanelHorizPos
            + (CurrentTabView?.WidthToReserveForTopBarControl ?? 0)
            + Stage_1WidthOfToolStringPanel;

        private int STAGE_2
        {
            get { return STAGE_1 - _stage1SpaceSaved; }
        }
        private int STAGE_3
        {
            get { return STAGE_2 - _stage2SpaceSaved; }
        }

        // The tabstrip typically changes size when a different language is selected.

        private void ToolStripOnSizeChanged(object o, EventArgs eventArgs)
        {
            AdjustTabStripDisplayForScreenSize();
        }

        private void AdjustTabStripDisplayForScreenSize()
        {
            if (!_viewInitialized)
                return;

            if (_originalToolStripPanelWidth == 0)
            {
                SaveOriginalWidthValues();
                SaveOriginalButtonTexts();
            }

            // First, set the width of _panelHoldingToolstrip, the control holding the language menu,  help menu,
            // and possibly zoom control. It must be wide enough to display its content. In stages Full and 1,
            // it is also not less than original width.
            int desiredToolStripPanelWidth = Math.Max(
                _toolStrip.Width + MinToolStripMargin,
                _currentShrinkage <= Shrinkage.Stage1 ? _originalToolStripPanelWidth : 0
            );
            if (desiredToolStripPanelWidth != _panelHoldingToolStrip.Width)
            {
                _panelHoldingToolStrip.Width = desiredToolStripPanelWidth;
                AlignTopRightPanels();
            }

            switch (_currentShrinkage)
            {
                default:
                    // Shrinkage.FullSize
                    if (Width < STAGE_1)
                    {
                        // shrink to stage 1
                        _currentShrinkage = Shrinkage.Stage1;
                        ShrinkToStage1();

                        // It is possible that we are jumping from FullScreen to a 'remembered'
                        // smaller screen size, so test for all of them!
                        if (Width < STAGE_2)
                        {
                            _currentShrinkage = Shrinkage.Stage2;
                            ShrinkToStage2();

                            if (Width < STAGE_3)
                            {
                                _currentShrinkage = Shrinkage.Stage3;
                                ShrinkToStage3();
                            }
                        }
                    }
                    break;
                case Shrinkage.Stage1:
                    if (Width >= STAGE_1)
                    {
                        // grow back to unshrunk
                        _currentShrinkage = Shrinkage.FullSize;
                        GrowToFullSize();
                        break;
                    }
                    if (Width < STAGE_2)
                    {
                        // shrink to stage 2
                        _currentShrinkage = Shrinkage.Stage2;
                        ShrinkToStage2();
                    }
                    break;
                case Shrinkage.Stage2:
                    if (Width >= STAGE_2)
                    {
                        // grow back to stage 1
                        _currentShrinkage = Shrinkage.Stage1;
                        GrowToStage1();
                        break;
                    }
                    if (Width < STAGE_3)
                    {
                        // shrink to stage 3
                        _currentShrinkage = Shrinkage.Stage3;
                        ShrinkToStage3();
                    }
                    break;
                case Shrinkage.Stage3:
                    if (Width >= STAGE_3)
                    {
                        // grow back to stage 2
                        _currentShrinkage = Shrinkage.Stage2;
                        GrowToStage2();
                    }
                    break;
            }
        }

        private void SaveOriginalWidthValues()
        {
            _originalToolStripPanelWidth = _panelHoldingToolStrip.Width;
            _originalToolSpecificPanelHorizPos = _toolSpecificPanel.Location.X;
            _originalUiMenuWidth = _uiLanguageMenu.Width;
            _stage1SpaceSaved = 0;
            _stage2SpaceSaved = 0;
        }

        private void SaveOriginalButtonTexts()
        {
            _originalHelpText = _helpMenu.Text;
            _originalUiLanguageSelection = _uiLanguageMenu.Text;
        }

        // Stage 1 removes the space we initially leave in edit and publish views to the left of the
        // tool-specific buttons. (It has no visible effect in collection view, where the tool-specific buttons
        // are right-aligned.)
        private void ShrinkToStage1()
        {
            // Calculate right edge of tabs and move _toolSpecificPanel over to it
            var rightEdge = _publishTab.Bounds.Right + 5;
            if (_originalToolSpecificPanelHorizPos <= rightEdge)
                return;
            _stage1SpaceSaved = _originalToolSpecificPanelHorizPos - rightEdge;
            var currentToolPanelVert = _toolSpecificPanel.Location.Y;
            _toolSpecificPanel.Location = new Point(rightEdge, currentToolPanelVert);
            AlignTopRightPanels();
        }

        /// <summary>
        /// Keep the _panelHoldingToolStrip in the top right and the _toolSpecificPanel's right edge aligned with it.
        /// Normally during resize this happens automatically since both are anchored Right. But when we fiddle with
        /// the width or position of one of them we need to straighten things out.
        /// </summary>
        void AlignTopRightPanels()
        {
            _panelHoldingToolStrip.Left = this.Width - _panelHoldingToolStrip.Width; // align this panel on the right.
            _toolSpecificPanel.Width = _panelHoldingToolStrip.Left - _toolSpecificPanel.Left;
        }

        private void GrowToFullSize()
        {
            // revert _toolSpecificPanel to its original location
            _toolSpecificPanel.Location = new Point(
                _originalToolSpecificPanelHorizPos,
                _toolSpecificPanel.Location.Y
            );
            AlignTopRightPanels();
            _stage1SpaceSaved = 0;
        }

        /// <summary>
        /// Adjust buttons for the current Locale. In particular the Help button may be an icon or a translation.
        /// </summary>
        void AdjustButtonTextsForLocale()
        {
            if (_originalHelpImage == null)
                _originalHelpImage = _helpMenu.Image;
            var helpText = LocalizationManager.GetString("HelpMenu.Help Menu", "?");
            if (
                helpText == "?"
                || new[] { "en", "fr", "de", "es" }.Contains(LocalizationManager.UILanguageId)
            )
            {
                _helpMenu.Text = "";
                _helpMenu.Image = _originalHelpImage;
            }
            else
            {
                _helpMenu.Text = helpText;
                _helpMenu.Image = null;
            }
        }

        // Currently stage 2 removes the space between the right-hand toolstrip and the tool-specific controls,
        // by shrinking _panelHoldingToolStrip.
        private void ShrinkToStage2()
        {
            _panelHoldingToolStrip.Width = _toolStrip.Width + MinToolStripMargin;
            AlignTopRightPanels();
            _stage2SpaceSaved = _originalToolStripPanelWidth - _panelHoldingToolStrip.Width;
        }

        private void GrowToStage1()
        {
            _panelHoldingToolStrip.Width = _originalToolStripPanelWidth;
            AlignTopRightPanels();
            _stage2SpaceSaved = 0;
        }

        // Stage 3 hides the right-hand toolstrip altogether.
        private void ShrinkToStage3()
        {
            // Extreme measures for really small screens
            _panelHoldingToolStrip.Visible = false;
            _toolSpecificPanel.Width = Width - _toolSpecificPanel.Left;
        }

        private void GrowToStage2()
        {
            _panelHoldingToolStrip.Visible = true;
            AlignTopRightPanels();
        }

        #endregion
    }

    public class NoBorderToolStripRenderer : ToolStripProfessionalRenderer
    {
        public NoBorderToolStripRenderer()
            : base(new NoBorderToolStripColorTable()) { }

        public Color DisabledColor { get; set; }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // this is needed, especially on Linux
            e.SizeTextRectangleToText();
            AdjustToolStripLocationIfNecessary(e);
            if (e.Item.Enabled)
            {
                base.OnRenderItemText(e);
                return;
            }

            // We have to actually take over drawing it, because when disabled, e.TextColor
            // is ignored. There doesn't seem to be a property for disabled text color.
            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                e.TextRectangle,
                DisabledColor,
                e.TextFormat
            );
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (!e.Item.Enabled)
                e.ArrowColor = DisabledColor;

            base.OnRenderArrow(e);
        }

        /// <summary>
        /// A toolstrip with one item embedded in a panel embedded in a TableLayoutPanel does not display well
        /// on Linux/Mono.  The text display can be truncated and moves around the panel horizontally.  The
        /// sizing calculation is carried out properly, but the ensuing horizontal location seems almost random.
        /// Rather than try to fix possibly several layers of Mono libary code, we calculate the desired location
        /// here to prevent the text from being truncated if possible.
        /// </summary>
        /// <remarks>
        /// See http://issues.bloomlibrary.org/youtrack/issue/BL-4409.
        /// </remarks>
        private void AdjustToolStripLocationIfNecessary(ToolStripItemTextRenderEventArgs e)
        {
            if (
                SIL.PlatformUtilities.Platform.IsUnix
                && e.ToolStrip != null
                && e.ToolStrip.Items.Count == 1
                && e.ToolStrip.Parent != null
                && e.ToolStrip.Parent.Parent is TableLayoutPanel
            )
            {
                var delta = (e.ToolStrip.Location.X + e.ToolStrip.Width) - e.ToolStrip.Parent.Width;
                // Try to leave a pixel of margin.
                if (delta >= 0)
                {
                    e.ToolStrip.Location = new Point(
                        Math.Max(e.ToolStrip.Location.X - (delta + 1), 1),
                        e.ToolStrip.Location.Y
                    );
                }
            }
        }
    }

    public class NoBorderToolStripColorTable : ProfessionalColorTable
    {
        // BL-5071 Make the border the same color as the button when selected/hovered
        public override Color ButtonSelectedBorder => SystemColors.GradientActiveCaption;
    }

    /// <summary>
    /// Mono refuses to create CultureInfo items for languages it doesn't recognize.  And I'm not
    /// sure that .Net allows you to modify the dummy objects it creates.  So this class serves
    /// to store the language code and the most useful names associated with it, at least for the
    /// purposes of the drop-down menus that display the localization languages available.
    /// </summary>
    public class LanguageItem
    {
        public string LangTag;
        public string EnglishName;
        public string MenuText;
        public float FractionApproved;
        public float FractionTranslated;
    }

    /// <summary>
    /// This class follows a recommendation at
    /// https://support.microsoft.com/en-us/help/953934/deeply-nested-controls-do-not-resize-properly-when-their-parents-are-r
    /// It works around a bug that causes a "deeply nested" panel with a docked child to
    /// fail to adjust the position of the docked child when the parent resizes.
    /// </summary>
    public class NestedDockedChildPanel : Panel
    {
        // This fix is Windows/.Net specific.  It prevents Bloom from displaying the main window at all in Linux/Mono.
#if !__MonoCS__
        protected override void OnSizeChanged(EventArgs e)
        {
            if (this.Handle != null)
            {
                this.BeginInvoke(
                    (MethodInvoker)
                        delegate
                        {
                            base.OnSizeChanged(e);
                        }
                );
            }
        }
#endif
    }
}
