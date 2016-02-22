using System.Diagnostics;
using Newtonsoft.Json;
using System.IO;

namespace Bloom.Book
{
	public class UserPrefs
	{
		private bool _loading = true;
		private string _fileName;
		private int _mostRecentPage;

		private UserPrefs() {}

		public static UserPrefs Load(string fileName)
		{
			if (string.IsNullOrEmpty(fileName))
				return null;

			var userPrefs = File.Exists(fileName) ? JsonConvert.DeserializeObject<UserPrefs>(File.ReadAllText(fileName)) : new UserPrefs();
			userPrefs._fileName = fileName;
			userPrefs._loading = false;
			return userPrefs;
		}
		
		/// <summary>
		/// Used when the directory is changed because the book has been renamed
		/// </summary>
		/// <param name="newDirectoryName"></param>
		public void UpdateFileLocation(string newDirectoryName)
		{
			_fileName = Path.Combine(newDirectoryName, Path.GetFileName(_fileName));
		}

		[JsonProperty("mostRecentPage")]
		public int MostRecentPage
		{
			get
			{
				return _mostRecentPage;
			}
			set
			{
				_mostRecentPage = value;
				Save();
			}
		}

		private void Save()
		{
			// We're checking this because the deserialization routine calls the property setters which triggers a save. We don't
			// want to save while loading.
			if (_loading)
				return;
			var prefs = JsonConvert.SerializeObject(this);
			Debug.Assert(!string.IsNullOrWhiteSpace(prefs));

			if (!string.IsNullOrWhiteSpace(prefs))
			{
				var temp = new SIL.IO.TempFileForSafeWriting(_fileName);
				File.WriteAllText(temp.TempFilePath, prefs);
				temp.WriteWasSuccessful();
			}
		}
	}


}
