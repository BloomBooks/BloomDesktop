using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices.ComTypes;
using System.IO;

namespace Skybound.Gecko
{
	/// <summary>
	/// Provides access to Gecko preferences.
	/// </summary>
	public class GeckoPreferences
	{
		static GeckoPreferences()
		{
			// ensure we're initialized
			Xpcom.Initialize();
			
			PrefService = Xpcom.GetService<nsIPrefService>("@mozilla.org/preferences-service;1");
		}
		
		static nsIPrefService PrefService;
		
		/// <summary>
		/// Gets the preferences defined for the current user.
		/// </summary>
		static public GeckoPreferences User
		{
			get { return _User ?? (_User = new GeckoPreferences(false)); }
		}
		static GeckoPreferences _User;
		
		/// <summary>
		/// Gets the set of preferences used as defaults when no user preference is set.
		/// </summary>
		static public GeckoPreferences Default
		{
			get { return _Default ?? (_Default = new GeckoPreferences(true)); }
		}
		static GeckoPreferences _Default;
		
		/// <summary>
		/// Reads all User preferences from the specified file.
		/// </summary>
		/// <param name="filename">Required. The name of the file from which preferences are read.  May not be null.</param>
		static public void Load(string filename)
		{
			if (string.IsNullOrEmpty(filename))
				throw new ArgumentNullException("filename");
			else if (!File.Exists(filename))
				throw new FileNotFoundException();
			
			PrefService.ReadUserPrefs((nsIFile)Xpcom.NewNativeLocalFile(filename));
		}
		
		/// <summary>
		/// Saves all User preferences to the specified file.
		/// </summary>
		/// <param name="filename">Required. The name of the file to which preferences are saved.  May not be null.</param>
		static public void Save(string filename)
		{
			if (string.IsNullOrEmpty(filename))
				throw new ArgumentNullException("filename");
			
			PrefService.SavePrefFile((nsIFile)Xpcom.NewNativeLocalFile(filename));
		}
		
		private GeckoPreferences(bool defaultBranch)
		{
			IsDefaultBranch = defaultBranch;
			if (defaultBranch)
				Branch = PrefService.GetDefaultBranch("");
			else
				Branch = PrefService.GetBranch("");
		}
		
		nsIPrefBranch Branch;
		bool IsDefaultBranch;
		
		/// <summary>
		/// Resets all preferences to their default values.
		/// </summary>
		public void Reset()
		{
			if (IsDefaultBranch)
				PrefService.ResetPrefs();
			else
				PrefService.ResetUserPrefs();
		}
		
		const int PREF_INVALID = 0;
		const int PREF_STRING = 32;
		const int PREF_INT = 64;
		const int PREF_BOOL = 128;
		
		/// <summary>
		/// Gets or sets the preference with the given name.
		/// </summary>
		/// <param name="prefName">Required. The name of the preference to get or set.</param>
		/// <returns></returns>
		public object this[string prefName]
		{
			get
			{
				int type = Branch.GetPrefType(prefName);
				switch (type)
				{
					case PREF_INVALID: return null;
					case PREF_STRING: return Branch.GetCharPref(prefName);
					case PREF_INT: return Branch.GetIntPref(prefName);
					case PREF_BOOL: return Branch.GetBoolPref(prefName);
				}
				throw new ArgumentException("prefName");
			}
			set
			{
				if (string.IsNullOrEmpty(prefName))
					throw new ArgumentException("prefName");
				else if (value == null)
					throw new ArgumentNullException("value");
				
				int existingType = Branch.GetPrefType(prefName);
				int assignedType = GetValueType(value);
				
				if (existingType != 0 && existingType != assignedType)
					throw new InvalidCastException("A " + value.GetType().Name + " value may not be assigned to '" + prefName + "' because it is already defined as " + GetPreferenceType(prefName).Name + ".");
				
				switch (assignedType)
				{
					case PREF_STRING: Branch.SetCharPref(prefName, (string)value); break;
					case PREF_INT: Branch.SetIntPref(prefName, (int)value); break;
					case PREF_BOOL: Branch.SetBoolPref(prefName, (bool)value ? -1 : 0); break;
				}
			}
		}
		
		int GetValueType(object value)
		{
			if (value is int)
				return PREF_INT;
			else if (value is string)
				return PREF_STRING;
			else if (value is bool)
				return PREF_BOOL;
			
			throw new ArgumentException("Gecko preferences must be either a String, Int32, or Boolean value.", "prefName");
		}
		
		/// <summary>
		/// Gets the <see cref="Type"/> of the specified preference.
		/// </summary>
		/// <param name="name">Required. The name of the preference whose type is returned.</param>
		/// <returns></returns>
		public Type GetPreferenceType(string name)
		{
			if (string.IsNullOrEmpty(name))
				throw new ArgumentException("name");
			
			switch (Branch.GetPrefType(name))
			{
				case PREF_STRING: return typeof(string);
				case PREF_INT: return typeof(int);
				case PREF_BOOL: return typeof(bool);
			}
			return null;
		}
		
		/// <summary>
		/// Sets whether the specified preference is locked. Locking a preference will cause the preference service to always return the default value regardless of whether there is a user set value or not.
		/// </summary>
		/// <param name="name">Required. The preference to lock or unlock.</param>
		/// <param name="locked">True if the preference should be locked; otherwise, false, and the preference is unlocked.</param>
		public void SetLocked(string name, bool locked)
		{
			if (string.IsNullOrEmpty(name))
				throw new ArgumentException("name");
			
			if (locked)
				Branch.LockPref(name);
			else
				Branch.UnlockPref(name);
		}
		
		/// <summary>
		/// Gets whether the specified preference is locked.
		/// </summary>
		/// <param name="name">Required. The preference whose lock status is returned.</param>
		/// <returns></returns>
		public bool GetLocked(string name)
		{
			if (string.IsNullOrEmpty(name))
				throw new ArgumentException("name");
			
			return Branch.PrefIsLocked(name);
		}
	}
}
