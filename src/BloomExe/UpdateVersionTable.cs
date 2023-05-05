using System;
using System.Net;
using System.Reflection;
using SIL.Reporting;

namespace Bloom
{
	/// <summary>
	///
	/// This could maybe eventually go to https://github.com/hatton/NetSparkle. My hesitation is that it's kind of specific to our way of using TeamCity and our build scripts
	///
	/// There are two levels of indirection here to give us maximum forward compatibility and control over what upgrades happen in what channels.
	/// First, we go use a url based on our channel ("http://bloomlibrary.org/channels/UpgradeTable{channel}.txt) to download a file.
	/// Then, in that file, we search for a row that matches our version number to decide which upgrades folder to use.
	/// </summary>
	public class UpdateVersionTable
	{
		//unit tests can change this
		public string URLOfTable = "http://bloomlibrary.org/channels/UpgradeTable{0}.txt";
		//unit tests can pre-set this
		public string TextContentsOfTable
		{ get; set; }

		//unit tests can pre-set this
		public Version RunningVersion
		{ get; set; }

		public class UpdateTableLookupResult
		{
			public string URL;
			public WebException Error;

			public bool IsConnectivityError
			{
				get
				{
					return Error != null &&
						(Error.Status == WebExceptionStatus.Timeout || Error.Status == WebExceptionStatus.NameResolutionFailure);
				}
			}
		}

		/// <summary>
		/// Note! This will propagate network exceptions, so client can catch them and warn or not warn the user.
		/// </summary>
		/// <returns></returns>
		public UpdateTableLookupResult LookupURLOfUpdate(bool forceReload = false)
		{
			if(string.IsNullOrEmpty(TextContentsOfTable) || forceReload)
			{
				Logger.WriteEvent("Enter LookupURLOfUpdate()");
				var client = new BloomWebClient();
				{
					UpdateTableLookupResult errorResult;
					if (!CanGetVersionTableFromWeb(client, out errorResult))
					{
						return errorResult;
					}
				}
			}
			if(RunningVersion == default(Version))
			{
				RunningVersion = Assembly.GetExecutingAssembly().GetName().Version;
			}

			var parsingErrorMsg = String.Empty;
			try
			{
				//NB Programmers: don't change this to some OS-specific line ending, this is  file read by both OS's. '\n' is common to files edited on linux and windows.
				foreach(var line in TextContentsOfTable.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if(line.TrimStart().StartsWith("#"))
						continue; //comment

					var parts = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
					if(parts.Length != 3)
					{
						Logger.WriteEvent("***Error: UpdateVersionTable could not parse line {0} of this updateTableContent:", line);
						LogTableContents();
						throw new ApplicationException(line);
					}
					var lower = Version.Parse(parts[0]);
					var upper = Version.Parse(parts[1]);
					if(lower <= RunningVersion && upper >= RunningVersion)
						return new UpdateTableLookupResult { URL = parts[2].Trim() };
				}
				parsingErrorMsg = string.Format("{0} contains no record for this version of Bloom", GetUrlOfTable());
			}
			catch(ApplicationException e)
			{
				// BL-2654 Failure when reading upgrade table should not give a crash
				// In this case, a line of the UpdateVersionTable was not parseable
				// Put a message in the log and don't upgrade (and return a message that will get into a 'toast')
				parsingErrorMsg = "Could not parse a line of the UpdateVersionTable" + e.Message;
				Logger.WriteEvent(parsingErrorMsg);
			}
			catch(ArgumentException e)
			{
				// BL-2654 Failure when reading upgrade table should not give a crash
				// In this case, a version number in the UpdateVersionTable was not parseable
				// Put a message in the log and don't upgrade (and return a message that will get into a 'toast')
				parsingErrorMsg = "Could not parse a version number in the UpdateVersionTable" + e.Message;
				Logger.WriteEvent(parsingErrorMsg);
			}
			return new UpdateTableLookupResult { URL = String.Empty, Error = new WebException(parsingErrorMsg) };
		}

		/// <summary>
		/// If we can successfully download the update version table for the current channel,
		/// we return true and set the TextContentsOfTable property. Otherwise we return false and
		/// an UpdateTableLookupResult with an embedded error as an 'out' param.
		/// This internal method enables testing of captive portal situations
		/// </summary>
		/// <param name="client"></param>
		/// <param name="errorResult"></param>
		/// <returns></returns>
		internal bool CanGetVersionTableFromWeb(IBloomWebClient client, out UpdateTableLookupResult errorResult)
		{
			errorResult = null;
			try
			{
				Logger.WriteEvent("Channel is '" + ApplicationUpdateSupport.ChannelName + "'");
				Logger.WriteEvent("UpdateVersionTable looking for UpdateVersionTable URL: " + GetUrlOfTable());
				TextContentsOfTable = client.DownloadString(GetUrlOfTable());

				//things like captive portals will return an html page rather than the text file what we asked for, if the user isn't
				//logged in.
				if (TextContentsOfTable.ToLower().Contains("<html"))
				{
					string msg;
					if (ApplicationUpdateSupport.ChannelName == "Developer/Debug")
					{
						msg = "Just debugging during development...";
					}
					else
					{
						LogTableContents();
						msg = "Internet connection did not allow check for update.";
						NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, msg); // hopefully this will just 'toast'
					}
					errorResult = new UpdateTableLookupResult {URL = string.Empty, Error = new WebException(msg)};
					return false;
				}
			}
			catch (WebException e)
			{
				Logger.WriteEvent("***Error (WebException) in CanGetVersionTableFromWeb: " + e.Message);
				if (e.Status == WebExceptionStatus.ProtocolError)
				{
					if (e.Response is HttpWebResponse resp && resp.StatusCode == HttpStatusCode.NotFound)
					{
						Logger.WriteEvent(
							$"***Error: UpdateVersionTable failed to find a file at {GetUrlOfTable()} (channel='{ApplicationUpdateSupport.ChannelName}'");
					}
				}
				else if (IsConnectionError(e))
				{
					Logger.WriteEvent("***Error: UpdateVersionTable could not connect to the server");
				}
				errorResult = new UpdateTableLookupResult() {Error = e};
				return false;
			}
			catch (Exception e)
			{
				// We're getting some kind of exception thrown on some alpha users' machines, but we don't know why (BL-9211).
				// It isn't a WebException. I'm adding this catch block at least long enough to diagnose the problem,
				// but it is probably better to be here, anyway, since without it, Bloom crashes silently.
				Logger.WriteError("***Error (Exception) in CanGetVersionTableFromWeb:", e);

				errorResult = new UpdateTableLookupResult() { Error = new WebException(e.Message) };
				return false;
			}
			return true; // no error yet anyway!
		}

		private void LogTableContents()
		{
			TextContentsOfTable = TextContentsOfTable ?? "<html></html>"; // safety net
			//html may have javascript which has braces which will kill the string.format in WriteEvent
			var safeContents = TextContentsOfTable.Replace("{", "{{").Replace("}", "}}");
			Logger.WriteEvent("***UpdateVersionTable contents are " + Environment.NewLine + safeContents);
		}

		private string GetUrlOfTable()
		{
			return String.Format(URLOfTable, ApplicationUpdateSupport.ChannelName);
		}

		private bool IsConnectionError(WebException ex)
		{
			return
				ex.Status == WebExceptionStatus.Timeout ||
				ex.Status == WebExceptionStatus.NameResolutionFailure;
			//I'm not sure if you'd ever get one of these?
			//				ex.Status == WebExceptionStatus.ReceiveFailure ||
			//			ex.Status == WebExceptionStatus.ConnectFailure;
		}
	}
}
