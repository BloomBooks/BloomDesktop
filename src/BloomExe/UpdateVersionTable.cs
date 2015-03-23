using System;
using System.Net;
using System.Reflection;

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
		public  string  URLOfTable = "http://bloomlibrary.org/channels/UpgradeTable{0}.txt";
		//unit tests can pre-set this
		public  string TextContentsOfTable { get; set; }

		//unit tests can pre-set this
		public  Version RunningVersion { get; set; }


		/// <summary>
		/// Note! This will propogate network exceptions, so client can catch them and warn or not warn the user.
		/// </summary>
		/// <returns></returns>
		public  string GetAppcastUrl()
		{
			if (string.IsNullOrEmpty(TextContentsOfTable))
			{
				var client = new WebClient();
				{
					TextContentsOfTable =  client.DownloadString(GetUrlOfTable());
				}
			}
			if (RunningVersion == default(Version))
			{
				RunningVersion = Assembly.GetExecutingAssembly().GetName().Version;
			}

			//NB Programmers: don't change this to some OS-specific line ending, this is  file read by both OS's. '\n' is common to files edited on linux and windows.
			foreach (var line in TextContentsOfTable.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (line.TrimStart().StartsWith("#"))
					continue; //comment

				var parts = line.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
				if(parts.Length!=3)
					throw new ApplicationException("Could not parse a line of the UpdateVersionTable on "+URLOfTable+" '"+line+"'");
				var lower = Version.Parse(parts[0]);
				var upper = Version.Parse(parts[1]);
				if (lower <= RunningVersion && upper >= RunningVersion)
					return parts[2].Trim();
			}
			return string.Empty;
		}

		private string GetUrlOfTable()
		{
			// assemblyName is usually something like "BloomAlpha. In a developer debug build (or main stable release)
			// it will be simply "Bloom." This allows each channel to have its own update table.
			var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
			if (assemblyName.StartsWith("Bloom"))
				assemblyName = assemblyName.Substring("Bloom".Length);
			return String.Format(URLOfTable, assemblyName);
		}
	}
}
