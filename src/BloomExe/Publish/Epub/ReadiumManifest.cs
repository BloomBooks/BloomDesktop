using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
// ReSharper disable InconsistentNaming

namespace Bloom.Publish.Epub
{
	// Object that can be serialized into required manifest.json file for Readium 2.
	public class ReadiumManifest
	{
		[JsonProperty("@type")]
		public string Type;

		public string title;

		public ReadiumLink[] links;

		public ReadiumItem[] readingOrder;

		public ReadiumTocItem[] toc;

		public ReadiumMetadata metadata;
	}

	public class ReadiumMetadata
	{
		public ReadiumRendition rendition;

		[JsonProperty("media-overlay")]
		public ReadiumMediaProps MediaOverlay;
	}

	public class ReadiumLink
	{
		public string type;
		public string rel;
		public string href;
	}

	public class ReadiumItem
	{
		public string type;
		public string href;
		public string duration;
		public ReadiumProperty properties;
	}

	public class ReadiumProperty
	{
		[JsonProperty("media-overlay")]
		public string MediaOverlay;
	}

	public class ReadiumRendition
	{
		public string layout;
	}

	public class ReadiumMediaProps
	{
		[JsonProperty("active-class")] public string ActiveClass;
	}

	public class ReadiumTocItem
	{
		public string title;
		public string href;
	}

	/// <summary>
	/// The root item for a Readium media-overlay.json file.
	/// </summary>
	public class ReadiumMediaOverlay
	{
		public string role;
		public ReadiumOuterNarrationBlock[] narration;
	}

	public class ReadiumOuterNarrationBlock
	{
		public string text;
		public string[] role;
		public ReadiumInnerNarrationBlock[] narration;
	}

	public class ReadiumInnerNarrationBlock
	{
		public string text;
		public string audio;
	}
}
