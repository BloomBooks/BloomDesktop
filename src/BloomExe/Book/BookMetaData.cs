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
		public BloomAdditions()
		{
			suitableForMakingVernacularBooks = true; // default if not specified in file.
		}
		public string bookLineage { get; set; }
		public bool suitableForMakingVernacularBooks { get; set; }
		public bool suitableForMakingShells { get; set; }
		public bool folio { get; set; }
		public bool experimental { get; set; }
		// Todo: this needs to be set to some suitable person/organization, possibly based on our parse.com login, when something is actually uploaed.
		// As yet it is not used.
		public string uploadedBy { get; set; }
		// This indicates the kind of license in use. For Creative Commons licenses, it is the Abbreviation of the CreativeCommonsLicense
		// object, the second-last (before version number) element of the licenseUrl. Other known values are 'ask' (no license granted,
		// ask the copyright holder for permission to use) 'custom' (rights presumably specified in licenseNotes)
		// Review: would it help with filtering if this field contained some indication of whether licenseNotes contains anything
		// (e.g., so we can search for CC licenses with no non-standard encumbrance)?
		public string license { get; set; }
		// When license is 'custom' this contains the license information. For other types in may contain additional permissions
		// (or possibly restrictions).
		// Review: do we need this, or just a field indicating whether there ARE additional notes, or just some modifier in license indicating that?
		public string licenseNotes { get; set; }
		public string formatVersion { get; set; }
	}

	public class VolumeInfo
	{
		private IndustryIdentifier[] _industryIdentifiers;
		private ImageLinkGroup _imageLinks;
		public string title { get; set; }

		public IndustryIdentifier[] industryIdentifiers
		{
			get { return _industryIdentifiers ?? (_industryIdentifiers = new IndustryIdentifier[0]); }
			set { _industryIdentifiers = value; }
		}

		public ImageLinkGroup imageLinks
		{
			get { return _imageLinks ?? (_imageLinks = new ImageLinkGroup()); }
			set { _imageLinks = value; }
		}
	}

	public class IndustryIdentifier
	{
		public string identifier { get; set; }
		public string type { get; set; } // currently only ISBN_13 is used (ISBN_10 is only for older books, pre-2007)
	}

	// A value for the imageLinks field. The full google system has several other sizes.
	public class ImageLinkGroup
	{
		// This is currently only used to fill in when we upload the json. Locally the thumbnail is always called just thumbnail.png.
		public string thumbnail;
	}
}
