using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Ionic;

namespace Bloom
{
	/// <summary>
	/// A BloomPack is just a zipped collection folder (a folder full of book folders).
	/// </summary>
	class BloomPack
	{
		public static void Install(string path)
		{
			if(!File.Exists(path))
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem("{0} does not exist", path);
				return;
			}
			try
			{
				using (var zip = new Ionic.Zip.ZipFile(path))
				{
					var roots = zip.Entries.Where(f =>
													{
														var parts = f.FileName.Split(new[] {'/', '\\'});
														if (parts.Length == 2 && parts[1].Length == 0)
															return true;
														return false;
													});

					if (roots.Count() != 1)
					{
						Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
							"Bloom Packs should have only a single collection folder at the top level of the zip file.", path);
					}
					var folderEntry = roots.First();
					string destinationFolder = Path.Combine(ProjectContext.InstalledCollectionsDirectory, folderEntry.FileName);
					if (Directory.Exists(destinationFolder))
					{
						var msg =
							string.Format(
								"This computer already has a Bloom collection named '{0}'. Do you wan to replace it with the one from this BloomPack?", folderEntry.FileName);
						if (DialogResult.OK != MessageBox.Show(msg, "BloomPack Installer", MessageBoxButtons.OKCancel))
							return;
						try
						{
							Directory.Delete(destinationFolder, true);
						}
						catch (Exception e)
						{
							Palaso.Reporting.ErrorReport.NotifyUserOfProblem("Bloom was not able to remove the exiting copy of '{0}'. Try again after restarting your computer.", destinationFolder);
							return;
						}
					}
					folderEntry.Extract(ProjectContext.InstalledCollectionsDirectory);
					MessageBox.Show(string.Format("The {0} Collection is now ready to use on this computer.",folderEntry.FileName), "BloomPack Installer", MessageBoxButtons.OK);
				}

			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e, "Bloom was not able to install that Bloom Pack");
			}
		}
	}
}
