using System;

namespace Bloom.Communication
{
	/// <summary>
	/// A dummy implementation which lets the code compile for Linux
	/// </summary>
	class UnimplementedAndroidDeviceUsbConnection : IAndroidDeviceUsbConnection
	{
		public event EventHandler OneApplicableDeviceFound;
		public event EventHandler<MoreThanOneApplicableDeviceFoundEventArgs> MoreThanOneApplicableDeviceFound;
		public void FindDevice()
		{
			throw new NotImplementedException();
		}

		public void StopFindingDevice()
		{
			throw new NotImplementedException();
		}

		public bool BookExists(string fileName)
		{
			throw new NotImplementedException();
		}

		public void SendBook(string bloomdPath)
		{
			throw new NotImplementedException();
		}

		public string GetDeviceName()
		{
			throw new NotImplementedException();
		}
	}
}
