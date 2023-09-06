using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Spreadsheet
{
	/// <summary>
	/// Class that represents options for SpreadsheetImporter in a form suitable
	/// for serializing to Json
	/// </summary>
	public class SpreadsheetImportParams
	{
		[JsonProperty("removeOtherLanguages")]
		public bool RemoveOtherLanguages;

		public static SpreadsheetImportParams FromFile(string path)
		{
			return JsonConvert.DeserializeObject<SpreadsheetImportParams>(RobustFile.ReadAllText(path, Encoding.UTF8));
		}
	}
}
