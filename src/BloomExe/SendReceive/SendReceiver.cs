using System;
using System.Windows.Forms;
using Bloom.Properties;
using Chorus;
using Chorus.UI.Sync;
using Palaso.Code;
using Palaso.Reporting;

namespace Bloom.SendReceive
{
//    public interface SendReceiver
//    {
//        void CheckInNow(string message);
//        void CheckPointWithDialog(string dialogTitle);
//    }
//
//    public class NullSendReceiver : SendReceiver
//    {
//        public void CheckInNow(string message)
//        {
//
//        }
//
//        public void CheckPointWithDialog(string dialogTitle)
//        {
//
//        }
//    }

	public class SendReceiver : IDisposable
	{
		private readonly Func<Form> _getFormWithContextForInvokingErrorDialogs;
		private ChorusSystem _chorusSystem;
		//private readonly Form _formWithContextForInvokingErrorDialogs;
		public static bool SendReceiveDisabled;

		/// <summary>
		///
		/// </summary>
		/// <param name="chorusSystem"></param>
		/// <param name="getFormWithContextForInvokingErrorDialogs">using a function here rather than a value
		/// becuase this class is created by the dependency injection system while building up this for (a
		/// project window), so that value isn't available yet.</param>
		public SendReceiver(ChorusSystem chorusSystem, Func<Form> getFormWithContextForInvokingErrorDialogs )//, Form formWithContextForInvokingErrorDialogs)
		{
			_getFormWithContextForInvokingErrorDialogs = getFormWithContextForInvokingErrorDialogs;
			//Guard.AgainstNull(formWithContextForInvokingErrorDialogs, "formWithContextForInvokingErrorDialogs");
			//_formWithContextForInvokingErrorDialogs = formWithContextForInvokingErrorDialogs;

			//we don't do chorus on our source tree
			SendReceiveDisabled = !Settings.Default.ShowSendReceive || !chorusSystem.DidLoadUpCorrectly || chorusSystem.ProjectFolderConfiguration.FolderPath.ToLower().Contains("distfiles");

			if (!SendReceiveDisabled)
			{
				_chorusSystem = chorusSystem;
				BloomChorusRules.AddFileInfoToFolderConfiguration(_chorusSystem.ProjectFolderConfiguration);
			}
		}


		public void CheckInNow(string message)
		{
			Guard.AgainstNull(_getFormWithContextForInvokingErrorDialogs(),"_getFormWithContextForInvokingErrorDialogs()");
			if (SendReceiveDisabled)
				return; //we don't do chorus on our source tree

			_chorusSystem.AsyncLocalCheckIn(BloomLabelForCheckins + " "+ message,
											(result) =>
												{
													if (result.ErrorEncountered != null)
													{
														// TODO: there is an error here somewhere... BL-284, where hg complains that .../my book.htm isn't there.
														// Maybe it got renamed on another thread or something....?
														_getFormWithContextForInvokingErrorDialogs().BeginInvoke(new Action(() =>
																													   Palaso.Reporting.ErrorReport.NotifyUserOfProblem
																														(result.ErrorEncountered,
																														 "Error while creating a milestone in the local Send/Receive repository")))
															;
													}
												});
		}

		private static string BloomLabelForCheckins
		{
			get { return "[Bloom:"+Application.ProductVersion+"]"; }
		}


		public void CheckPointWithDialog(string dialogTitle)
		{
			if (SendReceiveDisabled)
				return; //we don't do chorus on our source tree

			try
			{
				using (var dlg = new SyncDialog(_chorusSystem.ProjectFolderConfiguration,
												SyncUIDialogBehaviors.StartImmediatelyAndCloseWhenFinished,
												SyncUIFeatures.Minimal))
				{
					dlg.Text = dialogTitle;
					dlg.SyncOptions.DoMergeWithOthers = false;
					dlg.SyncOptions.DoPullFromOthers = false;
					dlg.SyncOptions.DoSendToOthers = false;
					dlg.SyncOptions.RepositorySourcesToTry.Clear();
					dlg.SyncOptions.CheckinDescription = string.Format(BloomLabelForCheckins+" auto");
					dlg.UseTargetsAsSpecifiedInSyncOptions = true;

					dlg.ShowDialog();

					if (dlg.FinalStatus.WarningEncountered || //not finding the backup media only counts as a warning
						dlg.FinalStatus.ErrorEncountered)
					{
						ErrorReport.NotifyUserOfProblem(dlg.FinalStatus.LastException,
														"There was a problem  while storing history in repository. Chorus said:\r\n\r\n" +
														dlg.FinalStatus.LastWarning + "\r\n" +
														dlg.FinalStatus.LastError);
					}
				}
			}
			catch (Exception error)
			{
				Palaso.Reporting.Logger.WriteEvent("Error while storing history in repository: {0}", error.Message);
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
