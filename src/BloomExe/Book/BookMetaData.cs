using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Bloom.Book
{
	/// <summary>
	/// This class exists to serialize and deserialize the meta.json file using Newtonsoft.Json.
	/// Try to keep the public properties limited to what we want in the file.
	/// This is intended to produce Json that is a subset of the google Book Json format, https://developers.google.com/books/docs/v1/reference/volumes#resource,
	/// except for the additions in the BloomAdditions class accessed through the bloom property.
	/// Note that the exact names used here are important...please don't change them to standard .NET capitalization.
	/// </summary>
	public class BookMetaData
	{
		private BloomAdditions _bloom;
		private VolumeInfo _volumeInfo;

		/// <summary>
		/// This would naturally be a public string property of the class, but then it becomes one of the properties Json tries to serialize.
		/// </summary>
		/// <param name="input"></param>
		/// <returns></returns>
		public static string Serialize(BookMetaData input)
		{
			return JsonConvert.SerializeObject(input);
		}

		public static BookMetaData Deserialize(string input)
		{
			return JsonConvert.DeserializeObject<BookMetaData>(input); // Enhance: what do do if this fails?
		}

		// A guid that uniquely identifies this book.
		public string id { get; set; }

		public BloomAdditions bloom
		{
			get { return _bloom ?? (_bloom = new BloomAdditions()); }
			set { _bloom  = value; }
		}

		public VolumeInfo volumeInfo
		{
			get
			{
				if (_volumeInfo == null)
					_volumeInfo = new VolumeInfo();
				return _volumeInfo;
			}
			set { _volumeInfo = value; }
		}
	}

	/// <summary>
	/// Fields not part of google spec
	/// </summary>
	public class BloomAdditions
	{
		public string bookLineage { get; set; }
	}

	public class VolumeInfo
	{
		public string title { get; set; }
	}
}
