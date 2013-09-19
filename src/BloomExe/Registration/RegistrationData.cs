using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bloom.Registration
{
	/// <summary>
	/// make this one of the rows in your apps Settings so that you can get at it via Settings.Default.RegistrationData
	/// </summary>
	[Serializable]
	class RegistrationData
	{	public string FirstName;
		public string SirName;
		public string Email;
		public string Organization;
		public string HowUsing;
		public int LaunchCount;

		public RegistrationData()
		{
			FirstName = SirName= Email= Organization= HowUsing="";
			LaunchCount = 0;
		}

		/// <summary>
		/// it's up to you to save this
		/// </summary>
		public void IncrementLaunchCount()
		{
			++LaunchCount;
		}
	}
}
