using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace Bloom.ReaderTools
{
	public class ReaderStage
	{
		/// <summary>
		/// A space-delimited list of the letters and multi-graphs for this stage.
		/// </summary>
		[JsonProperty("letters", DefaultValueHandling = DefaultValueHandling.Populate)]
		[DefaultValue("")]
		public string Letters { get; set; }

		/// <summary>
		/// A space-delimited list of the sight words for this stage.
		/// </summary>
		[JsonProperty("sightWords", DefaultValueHandling = DefaultValueHandling.Populate)]
		[DefaultValue("")]
		public string SightWords { get; set; }

		/// <summary>
		/// This value is only used for exporting stage word lists in ReadersHandler.MakeLetterAndWordList(),
		/// it is not saved to the DecodableLevelData.json file
		/// </summary>
		[JsonProperty("words", DefaultValueHandling = DefaultValueHandling.Ignore)]
		[DefaultValue(null)]
		public string[] Words { get; set; }
	}

	public class ReaderLevel
	{
		public ReaderLevel()
		{
			ThingsToRemember = new List<string>();
		}

		[JsonProperty("maxWordsPerSentence")]
		public int MaxWordsPerSentence { get; set; }

		[JsonProperty("maxWordsPerPage")]
		public int MaxWordsPerPage { get; set; }

		[JsonProperty("maxWordsPerBook")]
		public int MaxWordsPerBook { get; set; }

		[JsonProperty("maxUniqueWordsPerBook")]
		public int MaxUniqueWordsPerBook { get; set; }

		[JsonProperty("thingsToRemember")]
		public List<string> ThingsToRemember { get; set; }
	}

	/// <summary>
	/// This class is used to create the default DecodableLevelData.json file
	/// </summary>
	public class ReaderToolsSettings
	{
		public ReaderToolsSettings() : this(false)
		{
			// default constructor
		}

		public ReaderToolsSettings(bool createDefaultData)
		{
			Stages = new List<ReaderStage>();
			Levels = new List<ReaderLevel>();

			if (!createDefaultData) return;

			Letters = "a b c d e f g h i j k l m n o p q r s t u v w x y z";
			MoreWords = string.Empty;

			// add default stages
			Stages.Add(new ReaderStage
			{
				Letters = "",
				SightWords = ""
			});

			// add default levels
			Levels.Add(new ReaderLevel
			{
				MaxWordsPerSentence = 5,
				MaxWordsPerPage = 5,
				MaxWordsPerBook = 23,
				MaxUniqueWordsPerBook = 8
			});

			Levels.Add(new ReaderLevel
			{
				MaxWordsPerSentence = 7,
				MaxWordsPerPage = 10,
				MaxWordsPerBook = 72,
				MaxUniqueWordsPerBook = 16
			});

			Levels.Add(new ReaderLevel
			{
				MaxWordsPerSentence = 8,
				MaxWordsPerPage = 18,
				MaxWordsPerBook = 206,
				MaxUniqueWordsPerBook = 32
			});

			Levels.Add(new ReaderLevel
			{
				MaxWordsPerSentence = 9,
				MaxWordsPerPage = 22,
				MaxWordsPerBook = 294,
				MaxUniqueWordsPerBook = 50
			});

			Levels.Add(new ReaderLevel
			{
				MaxWordsPerSentence = 10,
				MaxWordsPerPage = 25,
				MaxWordsPerBook = 500,
				MaxUniqueWordsPerBook = 64
			});
		}

		/// <summary>
		/// A space-delimited list of the letters and multi-graphs for the language
		/// </summary>
		[JsonProperty("letters", DefaultValueHandling = DefaultValueHandling.Populate)]
		[DefaultValue("a b c d e f g h i j k l m n o p q r s t u v w x y z")]
		public string Letters { get; set; }

		/// <summary>
		/// A space-delimited list of the words to include in addition to the word lists in the Sample Texts directory
		/// </summary>
		[JsonProperty("moreWords", DefaultValueHandling = DefaultValueHandling.Populate)]
		[DefaultValue("")]
		public string MoreWords { get; set; }

		[JsonProperty("stages")]
		public List<ReaderStage> Stages { get; set; }

		[JsonProperty("levels")]
		public List<ReaderLevel> Levels { get; set; }

		[JsonIgnore]
		public string Json
		{
			get { return JsonConvert.SerializeObject(this); }
		}
	}
}
