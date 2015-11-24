using System;
using Newtonsoft.Json;
using SIL.Extensions;

namespace Bloom.Edit
{
	public class AccordionTool
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("enabled")]
		public bool Enabled { get; set; }

		/// <summary>
		/// Different tools may use this arbitrarily. Currently decodable and leveled readers use it to store
		/// the stage or level a book belongs to (at least the one last active when editing it).
		/// </summary>
		[JsonProperty("state")]
		public string State { get; set; }
	}
}
