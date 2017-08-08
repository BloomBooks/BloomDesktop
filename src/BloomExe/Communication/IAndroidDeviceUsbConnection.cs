using System;
using System.Collections.Generic;

namespace Bloom.Communication
{
	internal interface IAndroidDeviceUsbConnection
	{
		event EventHandler OneReadyDeviceFound;
		event EventHandler<OneReadyDeviceNotFoundEventArgs> OneReadyDeviceNotFound;

		/// <summary>
		/// Attempt to establish a connection with a device which has the Bloom Reader app
		/// </summary>
		void FindDevice();

		void StopFindingDevice();
		bool BookExists(string fileName);
		void SendBook(string bloomdPath);
		string GetDeviceName();
	}

	enum DeviceNotFoundReportType
	{
		Unknown,
		NoDeviceFound,
		NoBloomDirectory,
		MoreThanOneReadyDevice
	}

	class OneReadyDeviceNotFoundEventArgs
	{
		public OneReadyDeviceNotFoundEventArgs(DeviceNotFoundReportType reportType, List<string> deviceNames)
		{
			ReportType = reportType;
			DeviceNames = deviceNames;
		}

		public DeviceNotFoundReportType ReportType { get; }

		public List<string> DeviceNames { get; }
	}
}