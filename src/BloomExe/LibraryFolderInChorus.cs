using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chorus.sync;

namespace Bloom_ChorusPlugin
{
	public class LibraryFolderInChorus
	{
		public static void AddFileInfoToFolderConfiguration(ProjectFolderConfiguration config)
		{
			config.ExcludePatterns.Add(Path.Combine("**", "cache"));
			config.ExcludePatterns.Add(Path.Combine("**", "Cache"));
			config.ExcludePatterns.Add("autoFonts.css");
			config.ExcludePatterns.Add("autoLayout.css");
			config.ExcludePatterns.Add("defaultDictionary.css");
			config.ExcludePatterns.Add("*.old");
			config.ExcludePatterns.Add("*.WeSayUserMemory");
			config.ExcludePatterns.Add("*.tmp");
			config.ExcludePatterns.Add("*.bak");
			config.ExcludePatterns.Add(Path.Combine("export", "*.lift"));
			config.ExcludePatterns.Add("*.plift");//normally in /export
			config.ExcludePatterns.Add("*.pdf");//normally in /export
			config.ExcludePatterns.Add("*.html");//normally in /export
			config.ExcludePatterns.Add("*.odt");//normally in /export
			config.ExcludePatterns.Add("*.ldml"); // Supposed to be in 'WritingSystems' folder now.

			ProjectFolderConfiguration.AddExcludedVideoExtensions(config); // For now at least.

			config.IncludePatterns.Add("*.lift");
			config.IncludePatterns.Add("*.lift-ranges");
			config.IncludePatterns.Add(Path.Combine("audio", "**.*")); // Including nested folders/files
			config.IncludePatterns.Add(Path.Combine("pictures", "**.*")); // Including nested folders/files
			config.IncludePatterns.Add(Path.Combine("others", "**.*")); // Including nested folders/files
			config.IncludePatterns.Add(Path.Combine("WritingSystems", "*.ldml"));
			config.IncludePatterns.Add("**.xml"); //hopefully the days of files ending in "xml" are numbered
			config.IncludePatterns.Add(".hgIgnore");

			config.IncludePatterns.Add(Path.Combine("export", "*.lpconfig"));//lexique pro
			config.IncludePatterns.Add(Path.Combine("export", "custom*.css")); //stylesheets
			config.IncludePatterns.Add(Path.Combine("export", "multigraphs.txt")); //list of multigraphs

			//review (jh,jh): should these only be added when WeSay is the client?  Dunno.
			config.IncludePatterns.Add("**.WeSayConfig");
			config.IncludePatterns.Add("**.WeSayUserConfig");
		}
	}
}
