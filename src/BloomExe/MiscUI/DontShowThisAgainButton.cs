using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Configuration;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace Bloom.MiscUI
{
	public partial class DontShowThisAgainButton : CheckBox
	{
		private ApplicationSettingsBase _settings;
		private string _key;

		public DontShowThisAgainButton()
		{
			InitializeComponent();
			Click += DontShowThisAgainButton_Click;
		}

		/// <summary>
		/// Call this to decide whether to show the dialog
		/// </summary>
		/// <param name="settings">enter your Settings.Default. Make sure you have a string property named DontShowThisAgain</param>
		/// <param name="key">A key to use for the list of things not-to-show. You're welcome to use a big long string (like a tip message itself); a hash will be used</param>
		/// <returns>true if you should show it</returns>
		public bool GetOKToShow(ApplicationSettingsBase settings, string key = null )
		{
			if (string.IsNullOrEmpty(key))
			{
				_key = FindForm().Name;
			}
			else
			{
				_key = key.GetHashCode().ToString();
			}
			_settings = settings;
			if (settings == null || DesignMode)
				return true;

			return !DialogsToHide.Contains(_key + ",");
		}



		/// <summary>
		/// This one uses the form's name as an id
		/// </summary>
		/// <param name="settings">Your Settings.Default</param>
		/// <param name="customID">Some string which can be used as a key into the don't-show index</param>
		public void CloseIfShouldNotShow(ApplicationSettingsBase settings, string customID = null)
		{
			if (!GetOKToShow(settings, customID))
				FindForm().Close();
		}

		/// <summary>
		/// Start showing all dialogs again
		/// </summary>
		public void ResetDontShowMemory(ApplicationSettingsBase settings)
		{
			_settings = settings;
			DialogsToHide = "";
		}

		private void DontShowThisAgainButton_Click(object sender, System.EventArgs e)
		{
			if (_settings == null)
			{
				Debug.Fail("You need to call GetOKToShow() on the DontShowThisAgainButton.");
			}
			var key = DialogsToHide + _key + ",";
			DialogsToHide = DialogsToHide.Replace(key, "");
			if (this.Checked)
			{
				DialogsToHide = key;
			}
		}

		private string DialogsToHide
		{
			get
			{
				var propertyInfo = _settings.GetType().GetProperty("DontShowThisAgain");
				Debug.Assert(propertyInfo != null, "You need to have a string property named DontShowThisAgain in your Settings");
				return (string)propertyInfo.GetValue(_settings, null);
			}
			set
			{
				PropertyInfo propertyInfo = _settings.GetType().GetProperty("DontShowThisAgain");
				Debug.Assert(propertyInfo != null, "You need to have a string property named DontShowThisAgain in your Settings");
				propertyInfo.SetValue(_settings, Convert.ChangeType(value, propertyInfo.PropertyType), null);
			}
		}

		private new bool DesignMode
		{
			get
			{
				return (base.DesignMode || GetService(typeof(IDesignerHost)) != null) ||
					(LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			}
		}

	}
}
