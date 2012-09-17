using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.SendReceive;
using Chorus;
using Chorus.UI.Sync;
using Chorus.VcsDrivers.Mercurial;
using Chorus.sync;
using Palaso.Reporting;

namespace Bloom
{
	public class SendReceiver : IDisposable
	{

		private ChorusSystem _chorusSystem;
		private string _collectionPath;

		public SendReceiver(string collectionPath)
		{
			_collectionPath = collectionPath;
			_chorusSystem = new ChorusSystem(collectionPath);
		}

		public void CheckInNow(string message)
		{
			if (_chorusSystem == null)
				return;

			//nb: we're not really using the message yet, at least, not showing it to the user
			if (!string.IsNullOrEmpty(HgRepository.GetEnvironmentReadinessMessage("en")))
			{
				Palaso.Reporting.Logger.WriteEvent("Chorus Checkin not possible: {0}", HgRepository.GetEnvironmentReadinessMessage("en"));
			}

			try
			{
				var configuration = new ProjectFolderConfiguration(_collectionPath);
				LibraryFolderInChorus.AddFileInfoToFolderConfiguration(configuration);


				using (var dlg = new SyncDialog(configuration,
					   SyncUIDialogBehaviors.StartImmediatelyAndCloseWhenFinished,
					   SyncUIFeatures.Minimal))
				{
					dlg.Text = "AUTO " + message;
					dlg.SyncOptions.DoMergeWithOthers = false;
					dlg.SyncOptions.DoPullFromOthers = false;
					dlg.SyncOptions.DoSendToOthers = true;
					dlg.SyncOptions.RepositorySourcesToTry.Clear();
					dlg.SyncOptions.CheckinDescription = string.Format("[{0}:{1}] auto", Application.ProductName, Application.ProductVersion);
					dlg.UseTargetsAsSpecifiedInSyncOptions = true;

					dlg.ShowDialog();

					if (dlg.FinalStatus.WarningEncountered ||  //not finding the backup media only counts as a warning
						dlg.FinalStatus.ErrorEncountered)
					{
						ErrorReport.NotifyUserOfProblem(new ShowOncePerSessionBasedOnExactMessagePolicy(),
														"There was a problem during auto backup. Chorus said:\r\n\r\n" +
														dlg.FinalStatus.LastWarning + "\r\n" +
														dlg.FinalStatus.LastError);
					}
				}
			}
			catch (Exception error)
			{
				Palaso.Reporting.Logger.WriteEvent("Error during Backup: {0}", error.Message);
				//TODO we need some passive way indicating the health of the backup system
			}
		}

		public void Dispose()
		{
			if(_chorusSystem!=null)
				_chorusSystem.Dispose();
			_chorusSystem = null;
		}
	}
}
