using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Autofac;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Edit;
using Bloom.ImageProcessing;
//using Bloom.SendReceive;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using Bloom.Api;
using Bloom.TeamCollection;
using Bloom.Publish.AccessibilityChecker;
using Bloom.Publish.Android;
using Bloom.Publish.Epub;
using Bloom.web;
using Bloom.web.controllers;
using BloomTests.web.controllers;
//using Chorus;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;

namespace Bloom
{
	public class ProjectContext : IDisposable
	{
		/// <summary>
		/// Any resources which belong only to this project will be tracked by this,
		/// and disposed of along with this ProjectContext class
		/// </summary>
		private ILifetimeScope _scope;

//		private CommandAvailabilityPublisher _commandAvailabilityPublisher;
		public Form ProjectWindow { get; private set; }

		public string SettingsPath { get; private set; }

		public ProjectContext(string projectSettingsPath, IContainer parentContainer)
		{
			SettingsPath = projectSettingsPath;
			// BL-8019: A couple lines down, BuildSubContainerForThisProject() starts BloomServer with the new project.
			// While we are starting (or restarting, in the case of switching collections) BloomServer we need to use
			// the WinFormsExceptionHandler mechanism, which doesn't use a browser.
			// The ProblemReportApi, which uses the browser (and therefore BloomServer) isn't available to us
			// while BloomServer is starting up. By the time WorkspaceView comes online and sets the error reporting
			// to the ProblemReportApi mechanism, BloomServer will be up and running again.
			ErrorReport.OnShowDetails = null;
			FatalExceptionHandler.UseFallback = true;

			BuildSubContainerForThisProject(projectSettingsPath, parentContainer);

			_scope.Resolve<CollectionSettings>().CheckAndFixDependencies(_scope.Resolve<BloomFileLocator>());

			ProjectWindow = _scope.Resolve <Shell>();

			string collectionDirectory = Path.GetDirectoryName(projectSettingsPath);

			//should we save a link to this in the list of collections?
			var collectionSettings = _scope.Resolve<CollectionSettings>();
			if(collectionSettings.IsSourceCollection)
			{
				AddShortCutInComputersBloomCollections(collectionDirectory);
			}

			ToolboxView.SetupToolboxForCollection(Settings);
		}

		/// ------------------------------------------------------------------------------------
		protected void BuildSubContainerForThisProject(string projectSettingsPath, IContainer parentContainer)
		{
			var commandTypes = new[]
							{
								typeof (DuplicatePageCommand),
								typeof (DeletePageCommand),
								typeof(CutCommand),
								typeof(CopyCommand),
								typeof(PasteCommand),
								typeof(UndoCommand)
							};

			var editableCollectionDirectory = Path.GetDirectoryName(projectSettingsPath);
			try
			{
				_scope = parentContainer.BeginLifetimeScope(builder =>
				{
					//BloomEvents are by nature, singletons (InstancePerLifetimeScope)
					builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
						.InstancePerLifetimeScope()
						// Didn't work .Where(t => t.GetInterfaces().Contains(typeof(Bloom.Event<>)));
						.Where(t => t is IEvent);

					//Other classes which are also  singletons
					builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
						.InstancePerLifetimeScope()
						.Where(t => new[]
						{
							typeof (TemplateInsertionCommand),
							typeof (EditBookCommand),
							typeof (SendReceiveCommand),
							typeof (SelectedTabAboutToChangeEvent),
							typeof (SelectedTabChangedEvent),
							typeof (LibraryClosing),
							typeof (PageListChangedEvent), // REMOVE+++++++++++++++++++++++++++
							typeof (BookRefreshEvent),
							typeof (BookSavedEvent),
							typeof (PageRefreshEvent),
							typeof (BookDownloadStartingEvent),
							typeof (BookSelection),
							typeof (CurrentEditableCollectionSelection),
							typeof (RelocatePageEvent),
							typeof (QueueRenameOfCollection),
							typeof (PageSelection),
							typeof (LocalizationChangedEvent),
							typeof (ControlKeyEvent),
							typeof (EditingModel),
							typeof (AudioRecording),
							typeof(BookSettingsApi),
							typeof(BookMetadataApi),
							typeof(PublishToAndroidApi),
							typeof(PublishEpubApi),
							typeof(AccessibilityCheckApi),
							typeof(CollectionSettingsApi),
							typeof(PageControlsApi),
							typeof(ReadersApi),
							typeof(PageTemplatesApi),
							typeof(AddOrChangePageApi),
							typeof(BloomWebSocketServer),
							typeof(KeyboardingConfigApi),
							typeof(ImageApi),
							typeof(MusicApi),
							typeof(PageListApi),
							typeof(TalkingBookApi),
							typeof(ToolboxApi),
							typeof(CommonApi),
							typeof(TeamCollectionApi),
							typeof(BrandingSettings),
							typeof(FeatureControlApi),
							typeof(I18NApi),
							typeof(SignLanguageApi),
							typeof(AudioSegmentationApi),
							typeof(FileIOApi),
							typeof(EditingViewApi),
							typeof(BrowserDialogApi),
							typeof(ProblemReportApi)
						}.Contains(t));

					builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
						.InstancePerLifetimeScope()
						.Where(commandTypes.Contains).As<ICommand>();

					var bookRenameEvent = new BookRenamedEvent();
					builder.Register(c => bookRenameEvent).AsSelf().InstancePerLifetimeScope();

					try
					{
#if Chorus                      //nb: we split out the ChorusSystem.Init() so that this won't ever fail, so we have something registered even if we aren't
						//going to be able to do HG for some reason.
						var chorusSystem = new ChorusSystem(Path.GetDirectoryName(projectSettingsPath));
						builder.Register<ChorusSystem>(c => chorusSystem).InstancePerLifetimeScope();
						builder.Register<SendReceiver>(c => new SendReceiver(chorusSystem, () => ProjectWindow))
							.InstancePerLifetimeScope();

					chorusSystem.Init(string.Empty/*user name*/);
#endif
					}
					catch (Exception error)
					{
#if USING_CHORUS
#if !DEBUG
					SIL.Reporting.ErrorReport.NotifyUserOfProblem(error,
						"There was a problem loading the Chorus Send/Receive system for this collection. Bloom will try to limp along, but you'll need technical help to resolve this. If you have no other choice, find this folder: {0}, move it somewhere safe, and restart Bloom.", Path.GetDirectoryName(projectSettingsPath).CombineForPath(".hg"));
#endif
					//swallow for develoeprs, because this happens if you don't have the Mercurial and "Mercurial Extensions" folders in the root, and our
					//getdependencies doesn't yet do that.
#endif
					}


					//This deserves some explanation:
					//*every* collection has a "*.BloomCollection" settings file. But the one we make the most use of is the one editable collection
					//That's why we're registering it... it gets used all over. At the moment (May 2012), we don't ever read the
					//settings file of the collections we're using for sources.
					try
					{
						builder.Register<CollectionSettings>(c => new CollectionSettings(projectSettingsPath)).InstancePerLifetimeScope();
					}
					catch (Exception)
					{
						return;
					}


					builder.Register<LibraryModel>(
						c =>
							new LibraryModel(editableCollectionDirectory, c.Resolve<CollectionSettings>(),
							#if Chorus
								c.Resolve<SendReceiver>(),
							#endif
								c.Resolve<BookSelection>(), c.Resolve<SourceCollectionsList>(), c.Resolve<BookCollection.Factory>(),
								c.Resolve<EditBookCommand>(), c.Resolve<CreateFromSourceBookCommand>(), c.Resolve<BookServer>(),
								c.Resolve<CurrentEditableCollectionSelection>(), c.Resolve<BookThumbNailer>())).InstancePerLifetimeScope();

					// Keep in sync with OptimizedFileLocator: it wants to return the object created here.
					builder.Register<IChangeableFileLocator>(
						c =>
							new BloomFileLocator(c.Resolve<CollectionSettings>(), c.Resolve<XMatterPackFinder>(), GetFactoryFileLocations(),
								GetFoundFileLocations(), GetAfterXMatterFileLocations())).InstancePerLifetimeScope();

					builder.Register<LanguageSettings>(c =>
					{
						var librarySettings = c.Resolve<CollectionSettings>();
						var preferredSourceLanguagesInOrder = new List<string>();
						preferredSourceLanguagesInOrder.Add(librarySettings.Language2.Iso639Code);
						if (!String.IsNullOrEmpty(librarySettings.Language3.Iso639Code)
							&& librarySettings.Language3.Iso639Code != librarySettings.Language2.Iso639Code)
							preferredSourceLanguagesInOrder.Add(librarySettings.Language3.Iso639Code);

						return new LanguageSettings(librarySettings.Language1.Iso639Code, preferredSourceLanguagesInOrder);
					});
					builder.Register<XMatterPackFinder>(c =>
					{
						var locations = new List<string>();
						locations.Add(BloomFileLocator.GetInstalledXMatterDirectory());
						locations.Add(XMatterAppDataFolder);
						locations.Add(XMatterCommonDataFolder);
						return new XMatterPackFinder(locations);
					});

					builder.Register<SourceCollectionsList>(c =>
					{
						var l = new SourceCollectionsList(c.Resolve<Book.Book.Factory>(), c.Resolve<BookStorage.Factory>(),
							 editableCollectionDirectory, new string[] {BloomFileLocator.FactoryCollectionsDirectory, GetInstalledCollectionsDirectory()});
						return l;
					}).InstancePerLifetimeScope();

					builder.Register<ITemplateFinder>(c =>
					{
						return c.Resolve<SourceCollectionsList>();
					}).InstancePerLifetimeScope();

					builder.RegisterType<BloomParseClient>().AsSelf().SingleInstance();

					// Enhance: may need some way to test a release build in the sandbox.
					builder.Register(c => CreateBloomS3Client()).AsSelf().SingleInstance();
					builder.RegisterType<BookTransfer>().AsSelf().SingleInstance();

					//TODO: this gave a stackoverflow exception
//				builder.Register<WorkspaceModel>(c => c.Resolve<WorkspaceModel.Factory>()(rootDirectoryPath)).InstancePerLifetimeScope();
					//so we're doing this
					builder.Register(c => editableCollectionDirectory).InstancePerLifetimeScope();

					builder.RegisterType<CreateFromSourceBookCommand>().InstancePerLifetimeScope();

					// See related comment below for BL-688
//				string collectionDirectory = Path.GetDirectoryName(projectSettingsPath);
//				if (Path.GetFileNameWithoutExtension(projectSettingsPath).ToLower().Contains("web"))
//				{
//					// REVIEW: This seems to be used only for testing purposes
//					BookCollection editableCollection = _scope.Resolve<BookCollection.Factory>()(collectionDirectory, BookCollection.CollectionType.TheOneEditableCollection);
//					var sourceCollectionsList = _scope.Resolve<SourceCollectionsList>();
//					_httpServer = new BloomServer(_scope.Resolve<CollectionSettings>(), editableCollection, sourceCollectionsList, parentContainer.Resolve<HtmlThumbNailer>());
//				}
//				else
//				{
					builder.Register<BloomServer>(
						c =>
						new BloomServer(new RuntimeImageProcessor(bookRenameEvent), c.Resolve<BookSelection>(), c.Resolve<CollectionSettings>() )).SingleInstance();

					builder.Register<Func<WorkspaceView>>(c => () =>
					{
						var factory = c.Resolve<WorkspaceView.Factory>();

						// Removing this check because finding "web" anywhere in the path is problematic.
						// This was discovered by a user whose username included "web" (https://jira.sil.org/browse/BL-688)
						// It appears this code block was for some experimental development but no longer works anyway.
//					if (projectSettingsPath.ToLower().Contains("web"))
//					{
//						return factory(c.Resolve<WebLibraryView>());
//					}
//					else
//					{
						return factory(c.Resolve<LibraryView>());
//					}
					});

					builder.RegisterType<AccessibilityCheckWindow>();

				});

				/*
				this is from spike, which worked, but we aren't using (yet)
				var allCommands = from c in commandTypes select _scope.Resolve(c) as ICommand;
				_commandAvailabilityPublisher = new CommandAvailabilityPublisher(allCommands);
				*/
			}
			catch (FileNotFoundException error)
			{
				MessageBox.Show("Bloom was not able to find all its bits. This sometimes happens when upgrading to a newer version. To fix it, please run the installer again and choose 'Repair', or uninstall and reinstall. We truly value your time and apologize for wasting it. The error was:"+Environment.NewLine+Environment.NewLine+error.Message,"Bloom Installation Problem",MessageBoxButtons.OK,MessageBoxIcon.Error);
				Application.Exit();
			}

			var server = _scope.Resolve<BloomServer>();
			server.StartListening();
			_scope.Resolve<AudioRecording>().RegisterWithApiHandler(server.ApiHandler);

			_scope.Resolve<BloomWebSocketServer>().Init((BloomServer.portForHttp + 1).ToString(CultureInfo.InvariantCulture));
			HelpLauncher.RegisterWithApiHandler(server.ApiHandler);
			ExternalLinkController.RegisterWithApiHandler(server.ApiHandler);
			ToolboxView.RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<PageTemplatesApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<AddOrChangePageApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<PublishToAndroidApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<PublishEpubApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<AccessibilityCheckApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<CollectionSettingsApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<PageControlsApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<KeyboardingConfigApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<BookSettingsApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<BookMetadataApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<ImageApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<ReadersApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<MusicApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<PageListApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<TalkingBookApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<ToolboxApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<CommonApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<TeamCollectionApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<FeatureControlApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<SignLanguageApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<AudioSegmentationApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<BrowserDialogApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<ProblemReportApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<I18NApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<FileIOApi>().RegisterWithApiHandler(server.ApiHandler);
			_scope.Resolve<EditingViewApi>().RegisterWithApiHandler(server.ApiHandler);
		}


		internal static BloomS3Client CreateBloomS3Client()
		{
			return new BloomS3Client(BookTransfer.UploadBucketNameForCurrentEnvironment);
		}


		/// <summary>
		/// Give the locations of the bedrock files/folders that come with Bloom. These will have priority.
		/// (But compare GetAfterXMatterFileLocations, for further paths that are searched after factory XMatter).
		/// </summary>
		public static IEnumerable<string> GetFactoryFileLocations()
		{
			//bookLayout has basepage.css. We have it first because it will find its way to many other folders, but this is the authoritative one
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot, "bookLayout");
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot);

			//hack to get the distfiles folder itself
			yield return Path.GetDirectoryName(FileLocationUtilities.GetDirectoryDistributedWithApplication("localization"));

			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(BloomFileLocator.BrowserRoot);
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/js"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/js/toolbar"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/css"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/html"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/html/font-awesome/css"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/img"));
			foreach (var dir in ToolboxView.GetToolboxServerDirectories())
				yield return dir;
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/StyleEditor"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/TopicChooser"));

			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookPreview"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"collection"));

			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"themes/bloom-jqueryui-theme"));

			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"lib"));
			// not needed: yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"lib/localizationManager"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"lib/long-press"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"lib/split-pane"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"lib/ckeditor/skins/icy_orange"));
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot,"bookEdit/toolbox/talkingBook"));

			yield return BloomFileLocator.GetInstalledXMatterDirectory();
			yield return FileLocationUtilities.GetDirectoryDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot, "publish/ePUBPublish"));

		}

		/// <summary>
		/// Per BL-785, we want to search xMatter/*-XMatter folders (handled by the XMatterPackFinder) before we search
		/// the template folders.
		/// </summary>
		/// <returns></returns>
		public static IEnumerable<string> GetAfterXMatterFileLocations()
		{
			var templatesDir = BloomFileLocator.FactoryTemplateBookDirectory;

			yield return templatesDir;

			foreach (var templateDir in SafeGetDirectories(templatesDir))
			{
				yield return templateDir;
			}

			yield return BloomFileLocator.FactoryCollectionsDirectory;
		}

		/// <summary>
		/// Give the locations of files/folders that the user has installed (plus sample shells)
		/// </summary>
		public static IEnumerable<string> GetFoundFileLocations()
		{
			if (Directory.Exists(BloomFileLocator.SampleShellsDirectory))
			{
				foreach (var dir in SafeGetDirectories(BloomFileLocator.SampleShellsDirectory))
				{
					yield return dir;
				}
			}

			foreach (var p in GetUserInstalledDirectories()) yield return p;

//			ENHANCE: Add, in the list of places we look, this library's "regional library" (when such a concept comes into being)
//			so that things like IndonesiaA5Portrait.css work just the same as the Factory "A5Portrait.css"
//			var templateCollectionList = parentContainer.Resolve<SourceCollectionsList>();
//			foreach (var repo in templateCollectionList.RepositoryFolders)
//			{
//				foreach (var directory in Directory.GetDirectories(repo))
//				{
//					yield return directory;
//				}
//			}
		}

		static Dictionary<string, bool> _mapPathToIsTemplateFolder = new Dictionary<string, bool>();
		static bool IsTemplateBookFolder(string path)
		{
			// Whether a particular book is a template can only change once it's created if a user
			// messes with the files. If they do that and this causes a problem they should be savvy
			// enough to restart Bloom. We possibly do this test on a lot of books, so caching the
			// results is a significant help.
			bool result;
			if (_mapPathToIsTemplateFolder.TryGetValue(path, out result))
				return result;
			var info = new BookInfo(path, false);
			result = info.IsSuitableForMakingShells || info.IsSuitableForMakingTemplates;
			_mapPathToIsTemplateFolder[path] = result;
			return result;
		}

		// BL-8893 Sometimes users can get into a state where a template directory Bloom thinks it should
		// look in is closed to Bloom by system permissions. In that case, skip that directory.
		internal static IEnumerable<string> SafeGetDirectories(string pathToSearch)
		{
			try
			{
				return Directory.GetDirectories(pathToSearch);
			}
			catch (UnauthorizedAccessException uaex)
			{
				Logger.WriteError("Bloom folder access problem: ", uaex);
				return new List<string>();
			}
			catch (DirectoryNotFoundException dnfex)
			{
				Logger.WriteError("Bloom couldn't find a folder: ", dnfex);
				return new List<string>();
			}
		}

		private static List<string> _userInstalledDirectories;

		public static void ClearUserInstalledDirectoriesCache()
		{
			_userInstalledDirectories = null;
		}

		public static IEnumerable<string> GetUserInstalledDirectories()
		{
			if (_userInstalledDirectories == null)
			{
				_userInstalledDirectories = GetUserInstalledDirectoriesInternal().ToList();
			}

			// We do this rather than just returning the list so that if something makes it necessary
			// to clear the cache, we can give a different answer the next time the enumeration
			// is evaluated.
			foreach (var d in _userInstalledDirectories)
				yield return d;
		}

		private static IEnumerable<string> GetUserInstalledDirectoriesInternal()
		{
//Note: This is ordering may no be sufficient. The intent is to use the versino of a css from
			//the template directory, to aid the template developer (he/she will want to make tweaks in the
			//original, not the copies with sample data). But this is very blunt; we're throwing in every
			//template we can find; so the code which uses this big pot could easily link to the wrong thing
			//if 2 templates used the same name ("styles.css") or if there were different versions of the
			//template on the machine ("superprimer1/superprimer.css" and "superprimer2/superprimer.css").
			//Tangentially related is the problem of a stylesheet of a template changing and messing up
			//a users's existing just-fine document. We have to somehow address that, too.
			if (Directory.Exists(GetInstalledCollectionsDirectory()))
			{
				
				foreach (var dir in SafeGetDirectories(GetInstalledCollectionsDirectory()))
				{
					yield return dir;

					//more likely, what we're looking for will be found in the book folders of the collection
					foreach (var templateDirectory in SafeGetDirectories(dir))
					{
						// Per discussion in BL-6031, we only want to search template books.
						// For example, if the user downloads a new version of Story Primer,
						// with a new Story Primer.css, and opens a book that uses that
						// style sheet, we want them to get the updated style sheet. But if
						// they just download a new book made from Story Primer, we don't want
						// them to get an updated (or obsolete) style sheet from there.
						if (ProjectContext.IsTemplateBookFolder(templateDirectory))
							yield return templateDirectory;
					}
				}

				// add those directories from collections which are just pointed to by shortcuts
				foreach (var shortcut in Directory.GetFiles(GetInstalledCollectionsDirectory(), "*.lnk", SearchOption.TopDirectoryOnly))
				{
					var collectionDirectory = ResolveShortcut.Resolve(shortcut);
					if (Directory.Exists(collectionDirectory))
					{
						foreach (var templateDirectory in SafeGetDirectories(collectionDirectory))
						{
							if (ProjectContext.IsTemplateBookFolder(templateDirectory))
								yield return templateDirectory;
						}
					}
				}
			}
		}


		public static string GetInstalledCollectionsDirectory()
		{
			//we want this path of directories sitting there, waiting for the user
			var d = GetBloomAppDataFolder();
			var collections = d.CombineForPath("Collections");

			if (!Directory.Exists(collections))
				Directory.CreateDirectory(collections);
			return collections;
		}

		public static string XMatterAppDataFolder
		{
			get
			{
				//we want this path of directories sitting there, waiting for the user
				var d = GetBloomAppDataFolder();
				if (!Directory.Exists(d))
					Directory.CreateDirectory(d);
				d = d.CombineForPath("XMatter");
				if (!Directory.Exists(d))
					Directory.CreateDirectory(d);
				return d;
			}
		}

		public CollectionSettings Settings
		{
			get { return _scope.Resolve<CollectionSettings>(); }
		}

		/// <summary>
		/// The folder in common data (e.g., ProgramData/SIL/Bloom/XMatter) where Bloom looks for shared XMatter files.
		/// This is also where older versions of Bloom installed XMatter.
		/// This folder might not exist! And may not be writeable!
		/// </summary>
		public static string XMatterCommonDataFolder
		{
			get
			{
				 return GetBloomCommonDataFolder().CombineForPath("XMatter");
			}
		}

#if CHORUS
		public SendReceiver SendReceiver
		{
			get { return _scope.Resolve<SendReceiver>(); }
		}
#endif

		internal BloomFileLocator OptimizedFileLocator
		{
			get { return (BloomFileLocator)_scope.Resolve<IChangeableFileLocator>(); }
		}

		public BookServer BookServer
		{
			get { return _scope.Resolve<BookServer>(); }
		}

		public BookThumbNailer ThumbNailer => _scope.Resolve<BookThumbNailer>();


		public static string GetBloomAppDataFolder()
		{
			var d = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).CombineForPath("SIL");
			if (!Directory.Exists(d))
				Directory.CreateDirectory(d);
			d = d.CombineForPath("Bloom");
			if (!Directory.Exists(d))
				Directory.CreateDirectory(d);
			return d;
		}

		/// <summary>
		/// Get the directory where common application data is stored for Bloom. Note that this may not exist,
		/// and should not be assumed to be writeable. (We don't ensure it exists because Bloom typically
		/// does not have permissions to create folders in CommonApplicationData.)
		/// </summary>
		/// <returns></returns>
		public static string GetBloomCommonDataFolder()
		{
			return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData).CombineForPath("SIL").CombineForPath("Bloom");
		}

		private static void ResetToFallbackHandler()
		{
			ErrorReport.OnShowDetails = null;
			FatalExceptionHandler.UseFallback = true;
		}

		/// ------------------------------------------------------------------------------------
		public void Dispose()
		{
			// Disposing ProjectContext disables api functionality and disposes WorkspaceModel/View, BloomServer, et al.,
			// so we need to resort to our fallback error handler.
			ResetToFallbackHandler();
			_scope.Dispose();
			_scope = null;

			//REVIEW: by debugging, I see that _httpServer is already (and properly) disposed of by the
			//_scope.Dispose() above.
			//
			//			if (_httpServer != null)
			//				_httpServer.Dispose();
			//			_httpServer = null;

//			if(_commandAvailabilityPublisher != null)
//				_commandAvailabilityPublisher.Dispose();
//			_commandAvailabilityPublisher = null;

			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// The idea here is that if someone is editing a shell collection, then next thing they are likely to do is
		/// open a vernacular library and try it out.  By adding this link, well they'll see this collection like
		/// they probably expect.
		/// </summary>
		private void AddShortCutInComputersBloomCollections(string vernacularCollectionDirectory)
		{
			if (!Directory.Exists(ProjectContext.GetInstalledCollectionsDirectory()))
				return;//well, that would be a bug, I suppose...

			try
			{
				ShortcutMaker.CreateDirectoryShortcut(vernacularCollectionDirectory, ProjectContext.GetInstalledCollectionsDirectory());
			}
			catch (ApplicationException e)
			{
				ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(), e.Message);
			}
			catch (Exception e)
			{
				ErrorReport.NotifyUserOfProblem(e,
					"Could not add a link for this shell library in the user collections directory");
			}

		}
	}
}
