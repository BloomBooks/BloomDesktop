using System;
using System.IO;
using System.Windows.Forms;
using Gecko;
using SIL.IO;

namespace Bloom.Publish.Epub
{
	public class ElectronicPublishView
	{
		private Browser _epubPreviewBrowser;

		private PublishModel _model;

		public ElectronicPublishView(PublishModel publishModel)
		{
			_model = publishModel;
		}

		private void SetupEpubControlContent()
		{
			_model.EpubMaker.StageEpub();

			var fileLocator = _model.BookSelection.CurrentSelection.GetFileLocator();
			var root = fileLocator.LocateDirectoryWithThrow("Readium");
			var tempFolder = Path.GetDirectoryName(_model.StagingDirectory);
			// This is kludge. I hope it can be improved. To make a preview we currently need all the Readium
			// files in a folder that is a parent of the staging folder containing the book content.
			// This allows us to tell Readium about the book by passing the name of the folder using the ?ePUB=
			// URL parameter. It doesn't work to use the original Readium file and make the parameter a full path.
			// It's possible that there is some variation that would work, e.g., make the param a full file:/// url
			// to the book folder. It's also possible we could get away with only copying the HTML file itself,
			// if we modified it to have localhost: links to the JS and CSS. Haven't tried this yet. The current
			// approach at least works.
			DirectoryUtilities.CopyDirectoryContents(root, tempFolder);

			var englishTemplatePath = fileLocator.LocateFileWithThrow("ePUB" + Path.DirectorySeparatorChar + "bloomEpubPreview-en.html");
			var localizedTemplatePath = BloomFileLocator.GetBestLocalizedFile(englishTemplatePath);

			var audioSituationClass = "noAudioAvailable";
			if(_model.EpubMaker.PublishWithoutAudio)
				audioSituationClass = "haveAudioButNotMakingTalkingBook";
			else if(_model.BookHasAudio)
				audioSituationClass = "isTalkingBook";

			var htmlContents = RobustFile.ReadAllText(localizedTemplatePath)
				.Replace("{EPUBFOLDER}", Path.GetFileName(_model.StagingDirectory))
				.Replace("_AudioSituationClass_", audioSituationClass);

			var previewHtmlInstancePath = Path.Combine(tempFolder, "bloomEpubPreview.htm");
			RobustFile.WriteAllText(previewHtmlInstancePath, htmlContents);
			_epubPreviewBrowser.Navigate(previewHtmlInstancePath.ToLocalhost(), false);
		}

		public EpubView SetupEpubControl(EpubView view, NavigationIsolator _isolator, Action updateSaveButton)
		{
			if (view == null)
			{
				view = new EpubView();
				_epubPreviewBrowser = new Browser();
				// We rather mangled the Readium code in the process of cutting away its own navigation
				// and other controls. It produces all kinds of JavaScript errors, but it seems to do
				// what we want. So just suppress the toasts for all of them.
				_epubPreviewBrowser.SuppressJavaScriptErrors = true;
				_epubPreviewBrowser.Isolator = _isolator;
				_epubPreviewBrowser.Dock = DockStyle.Fill;
				view.Controls.Add(_epubPreviewBrowser);
				// Has to be in front of the panel docked top for Fill to work.
				_epubPreviewBrowser.BringToFront();
			}
			_model.PrepareToStageEpub();
			Action setupElectronicPublicationControlMethod = SetupEpubControlContent;
			HandleAudioSituation(setupElectronicPublicationControlMethod, _epubPreviewBrowser, updateSaveButton);
			updateSaveButton();
			return view;
		}

		private void HandleAudioSituation(Action SetupElectronicPublicationControlMethod, Browser electronicPublicationBrowser, Action updateSaveButton)
		{
			 _model.DoAnyNeededAudioCompression();
			SetupElectronicPublicationControlMethod();
			updateSaveButton();
		}
	}
}
