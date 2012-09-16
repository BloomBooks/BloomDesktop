using Chorus.sync;

namespace Bloom.SendReceive
{
	public class LibraryFolderInChorus
	{
		public static void AddFileInfoToFolderConfiguration(ProjectFolderConfiguration config)
		{
			config.ExcludePatterns.Add("*.back");
			ProjectFolderConfiguration.AddExcludedVideoExtensions(config); // For now at least.

			config.IncludePatterns.Add("*.html");
			config.IncludePatterns.Add("*.htm");
			config.IncludePatterns.Add("*.png");
			config.IncludePatterns.Add("*.jpg");
			config.IncludePatterns.Add("*.css");
			config.IncludePatterns.Add("*.js");
			config.IncludePatterns.Add("*.txt");
			config.IncludePatterns.Add("*.bloomCollection");
		}
	}
}
