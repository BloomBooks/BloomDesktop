using System;

namespace Bloom.Api
{
	/// <summary>
	/// Helper class to hold the data we got from the Android, for the NewMessageReceived event of UDPListener
	/// TODO Make this into json, so that we can put in things like the version of BR that we're talking to
	/// and a human-readable name of the android we're sending to.
	/// </summary>
	class AndroidMessageArgs : EventArgs
	{
		public byte[] data { get; set; }

		public AndroidMessageArgs(byte[] newData)
		{
			data = newData;
		}
	}
}