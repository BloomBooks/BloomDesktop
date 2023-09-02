using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Utils;
using Newtonsoft.Json;

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
			return JsonConvert.DeserializeObject<SpreadsheetImportParams>(PatientFile.ReadAllText(path, Encoding.UTF8));
		}
	}
}
