using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.FontProcessing
{
	/// <summary>
	/// This class supports adding fonts that ship with Bloom without needing to
	/// install them for BloomDesktop to use them.  They may need to be embedded
	/// for ePUB and BloomPub books, and the embedding will find them in the
	/// fonts folder as well as being served from there while BloomDesktop is
	/// running.
	/// </summary>
	public class FontServe
	{
		private static FontServe _instance;
		public static FontServe GetInstance()
		{
			if (_instance == null)
				_instance = new FontServe();
			return _instance;
		}

		public List<FontServeInfo> FontsServed = new List<FontServeInfo>();

		/// <summary>
		/// Private constructor so that the only way to get one of these is to call the static method GetInstance.
		/// This class contains the information loaded from the distributed fonts/*/fontinfo.json files.
		/// </summary>
		FontServe()
		{
			var fontsFolder = FileLocationUtilities.GetDirectoryDistributedWithApplication("fonts");
			foreach (var subfolder in Directory.GetDirectories(fontsFolder))
			{
				var filePath = Path.Combine(subfolder, "font-info.json");
				if (RobustFile.Exists(filePath))
				{
					try
					{
						var json = RobustFile.ReadAllText(filePath);
						FontServeInfo info = JsonConvert.DeserializeObject<FontServeInfo>(json);
						FontsServed.Add(info);
					}
					catch (Exception ex)
					{
						Console.WriteLine("ERROR: could not deserialize {0}: {1}", filePath, ex);
					}
				}
			}
		}

		public bool HasFamily(string familyName)
		{
			return FontsServed.Any(info => info.family == familyName);
		}

		public FontServeInfo GetFontInformationForFamily(string familyName)
		{
			return FontsServed.Find(info => info.family == familyName);
		}

		public string GetAllFontFaceDeclarations()
		{
			var facesBldr = new StringBuilder();
			foreach (var fontInfo in FontsServed)
			{
				foreach (var face in fontInfo.faces)
					facesBldr.AppendLine(face);
			}
			return facesBldr.ToString();
		}
	}

	[SuppressMessage("ReSharper", "InconsistentNaming")]
	public class FontServeInfo
	{
		public string family;
		public FontServeFiles files;
		public List<string> faces;
		public FontServeMetadata metadata;
	}

	[SuppressMessage("ReSharper", "InconsistentNaming")]
	public class FontServeFiles
	{
		public string normal;
		public string bold;
		public string italic;
		public string bolditalic;
	}

	[SuppressMessage("ReSharper", "InconsistentNaming")]
	public class FontServeMetadata
	{
		public string copyright;
		public string designer;
		public string designerURL;
		public string fsType;
		public string license;
		public string licenseURL;
		public string manufacturer;
		public string manufacturerURL;
		public string trademark;
		public string version;
	}
}
