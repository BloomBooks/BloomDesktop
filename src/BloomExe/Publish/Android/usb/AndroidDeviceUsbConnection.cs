#if !__MonoCS__
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PodcastUtilities.PortableDevices;

namespace Bloom.Publish.Android.usb
{
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

	/// <summary>
	/// Handles non-UI functions of connecting to an Android device via USB and working with the file system there
	/// </summary>
	class AndroidDeviceUsbConnection
	{
		public Action<Book.Book> OneReadyDeviceFound;
		public Action<DeviceNotFoundReportType, List<string>> OneReadyDeviceNotFound;

		private const string kBloomFolderOnDevice = "Bloom";
		private IDevice _device;
		private string _bloomFolderPath;
		private bool _stopLookingForDevice;

		/// <summary>
		/// Attempt to establish a connection with a device which has the Bloom Reader app
		/// </summary>
		public void FindADeviceThatIsReadyToReceiveBook(Book.Book book)
		{
			_stopLookingForDevice = false;

			while (!_stopLookingForDevice && _device == null)
			{
				var devices = EnumerateAllDevices();
				if(!FindADeviceThatIsReadyToReceiveBookInternal(devices, book))
				{
					Thread.Sleep(1000);
				}
			}
		}

		public void StopFindingDevice()
		{
			_stopLookingForDevice = true;
		}

		public bool BookExists(string fileName)
		{
			if (_device == null || _bloomFolderPath == null)
				throw new InvalidOperationException("Must connect before calling BookExists");

			return _device.GetObjectFromPath(Path.Combine(_bloomFolderPath, fileName)) != null;
		}

		public void SendBook(string bloomdPath)
		{
			if (bloomdPath == null)
				throw new ArgumentNullException(nameof(bloomdPath));

			if (_device == null || _bloomFolderPath == null)
				throw new InvalidOperationException("Must connect before calling SendBookAsync");

			var sourceStream = File.OpenRead(bloomdPath);
			var targetStream = _device.OpenWrite(Path.Combine(_bloomFolderPath, Path.GetFileName(bloomdPath)), new FileInfo(bloomdPath).Length, true);
			Copy(sourceStream, targetStream);
		}

		public string GetDeviceName()
		{
			if (_device == null)
				throw new InvalidOperationException("Must connect before calling GetDeviceName");

			return _device.Name;
		}

		private void Copy(Stream source, Stream destination)
		{
			const int kCopyBufferSize = 262144;

			var buffer = new byte[kCopyBufferSize];

			while (true)
			{
				var readSize = source.Read(buffer, 0, kCopyBufferSize);
				if (readSize == 0)
				{
					break;
				}

				destination.Write(buffer, 0, readSize);
			}

			destination.Flush();
		}

		private static IEnumerable<IDevice> EnumerateAllDevices()
		{
			IDeviceManager manager = new DeviceManager();
			return manager.GetAllDevices();
		}

		/// <summary>
		/// Find the one with Bloom Reader installed (indicated by the presence of the Bloom directory
		/// as a direct child of a root storage object)
		/// </summary>
		/// <param name="devices"></param>
		/// <returns>true if it found a ready device</returns>
		private bool FindADeviceThatIsReadyToReceiveBookInternal(IEnumerable<IDevice> devices, Book.Book book)
		{
			List<IDevice> applicableDevices = new List<IDevice>();
			int totalDevicesFound = 0;
			foreach (var device in devices)
			{
				_bloomFolderPath = GetBloomFolderPath(device);
				if (_bloomFolderPath != null)
					applicableDevices.Add(device);
				totalDevicesFound++;
			}

			if (applicableDevices.Count == 1)
			{
				_device = applicableDevices[0];
				OneReadyDeviceFound(book);
				return true;
			}

			_bloomFolderPath = null;

			if (totalDevicesFound > 0 && applicableDevices.Count == 0)
			{
				OneReadyDeviceNotFound(DeviceNotFoundReportType.NoBloomDirectory,
					devices.Select(d => d.Name).ToList());
				return false;
			}

			DeviceNotFoundReportType deviceNotFoundReportType = DeviceNotFoundReportType.NoDeviceFound;
			if (applicableDevices.Count > 1)
			{
				deviceNotFoundReportType = DeviceNotFoundReportType.MoreThanOneReadyDevice;
			}
			OneReadyDeviceNotFound?.Invoke(DeviceNotFoundReportType.NoBloomDirectory,
				devices.Select(d => d.Name).ToList());

			return false;
		}

		private string GetBloomFolderPath(IDevice device)
		{
			foreach (var rso in device.GetDeviceRootStorageObjects())
			{
				var possiblePath = Path.Combine(rso.Name, kBloomFolderOnDevice);
				if (device.GetObjectFromPath(possiblePath) != null)
					return possiblePath;
			}
			return null;
		}
	}
}
#endif