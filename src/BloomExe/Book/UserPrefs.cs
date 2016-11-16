using System;
using System.Diagnostics;
using Newtonsoft.Json;
using System.IO;
using SIL.IO;
using SIL.Reporting;


namespace Bloom.Book
{
	public class UserPrefs
	{
		private bool _loading = true;
		private string _filePath;
		private int _mostRecentPage;
		private bool _reducePdfMemory;

		private UserPrefs() {}

		public static UserPrefs LoadOrMakeNew(string fileName)
		{
			if (string.IsNullOrEmpty(fileName))
				return null;

			UserPrefs userPrefs = null;
			if(RobustFile.Exists(fileName))
			{
				try
				{
					userPrefs = JsonConvert.DeserializeObject<UserPrefs>(RobustFile.ReadAllText(fileName));
					if (userPrefs == null)
						throw new ApplicationException("JsonConvert.DeserializeObject() returned null");
				}
				catch (Exception e)
				{
					Logger.WriteEvent("error reading UserPrefs at "+fileName+"  "+e.Message);
					//otherwise, just give them a new user prefs
					userPrefs = null;
				}
				
			}
			if(userPrefs == null)
				userPrefs = new UserPrefs();
			userPrefs._filePath = fileName;
			userPrefs._loading = false;
			return userPrefs;
		}
		
		/// <summary>
		/// Used when the directory is changed because the book has been renamed
		/// </summary>
		/// <param name="newDirectoryName"></param>
		public void UpdateFileLocation(string newDirectoryName)
		{
			_filePath = Path.Combine(newDirectoryName, Path.GetFileName(_filePath));
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

		[JsonProperty("reducePdfMemory")]
		public bool ReducePdfMemoryUse
		{
			get { return _reducePdfMemory; }
			set
			{
				_reducePdfMemory = value;
				Save();
			}
		}

		private void Save()
		{
			// We're checking this because the deserialization routine calls the property setters which triggers a save. We don't
			// want to save while loading.
			if(_loading)
				return;
			var prefs = JsonConvert.SerializeObject(this);

			Debug.Assert(!string.IsNullOrWhiteSpace(prefs));

			try
			{
				if(!string.IsNullOrWhiteSpace(prefs))
				{
					var temp = new TempFileForSafeWriting(_filePath);
					RobustFile.WriteAllText(temp.TempFilePath, prefs);
					temp.WriteWasSuccessful();
				}
			}
			catch(Exception error)
			{
				//For https://silbloom.myjetbrains.com/youtrack/issue/BL-3222  we did a real fix for 3.6.
				//But this will cover us for future errors here, which are not worth stopping the user from doing work.
				NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, "Problem saving book preferences", "book.userprefs could not be saved to " + _filePath, error);
			}
		}
	}
}
