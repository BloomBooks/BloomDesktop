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
    /// Class that represents options for SpreadsheetExporter in a form suitable
    /// for serializing to Json
    /// </summary>
    public class SpreadsheetExportParams
    {
        [JsonProperty("retainMarkup")]
        public bool RetainMarkup;

        public static SpreadsheetExportParams FromFile(string path)
        {
            return JsonConvert.DeserializeObject<SpreadsheetExportParams>(
                RobustFile.ReadAllText(path, Encoding.UTF8)
            );
        }
    }
}
