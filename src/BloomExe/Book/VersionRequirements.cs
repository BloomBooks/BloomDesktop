using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Bloom.Book
{
	public struct VersionRequirement
	{
		public string BloomDesktopMinVersion { get; set; }
		public string BloomReaderMinVersion { get; set; }
		public string FeatureId { get; set; }
		public string FeaturePhrase { get; set; }
	}
}
