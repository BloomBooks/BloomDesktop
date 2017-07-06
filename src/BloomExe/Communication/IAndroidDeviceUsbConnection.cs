using System;
using System.Collections.Generic;

namespace Bloom.Communication
{
	internal interface IAndroidDeviceUsbConnection
	{
		event EventHandler OneApplicableDeviceFound;
		event EventHandler<MoreThanOneApplicableDeviceFoundEventArgs> MoreThanOneApplicableDeviceFound;

		/// <summary>
		/// Attempt to establish a connection with a device which has the Bloom Reader app
		/// </summary>
		void FindDevice();

		void StopFindingDevice();
		bool BookExists(string fileName);
		void SendBook(string bloomdPath);
		string GetDeviceName();
	}

	class MoreThanOneApplicableDeviceFoundEventArgs
	{
		public MoreThanOneApplicableDeviceFoundEventArgs(List<string> deviceNames)
		{
			DeviceNames = deviceNames;
		}

		public List<string> DeviceNames { get; }
	}
}