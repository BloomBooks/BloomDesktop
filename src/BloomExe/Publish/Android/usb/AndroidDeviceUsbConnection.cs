#if !__MonoCS__
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
		public Action<Book.Book, Color, AndroidPublishSettings> OneReadyDeviceFound;
		public Action<DeviceNotFoundReportType, List<string>> OneReadyDeviceNotFound;

		private const string kBloomFolderOnDevice = "Bloom";
		private IDevice _device;
		private string _bloomFolderPath;
		private bool _stopLookingForDevice;

		/// <summary>
		/// Attempt to establish a connection with a device which has the Bloom Reader app,
		/// then send it the book.
		/// </summary>
		public void ConnectAndSendToOneDevice(Book.Book book, Color backColor, AndroidPublishSettings settings = null)
		{
			_stopLookingForDevice = false;
			_device = null;

			//The UX here is to only allow one device plugged in a time.
			while (!_stopLookingForDevice && _device == null)
			{
				var devices = EnumerateAllDevices();
				if(!ConnectAndSendToOneDeviceInternal(devices, book, backColor, settings))
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

			try
			{
				return _device.GetObjectFromPath(Path.Combine(_bloomFolderPath, fileName)) != null;
			}
			catch
			{
				// At least with our current uses of this method, it is safe to return false
				// if we can't make a successful check. This prevents nasty messages for the user. (BL-5548)
				return false;
			}
		}

		public void SendBook(string bloomdPath)
		{
			if (bloomdPath == null)
				throw new ArgumentNullException(nameof(bloomdPath));

			if (_device == null || _bloomFolderPath == null)
				throw new InvalidOperationException("Must connect before calling SendBookAsync");

			using (var sourceStream = File.OpenRead(bloomdPath))
			using (var targetStream = _device.OpenWrite(Path.Combine(_bloomFolderPath, Path.GetFileName(bloomdPath)),
				new FileInfo(bloomdPath).Length, true))
			{
				Copy(sourceStream, targetStream);
			}

			// Also send a little marker file. BloomReader can tell "something has been updated" by just checking the modify time on this.
			var buffer = Encoding.UTF8.GetBytes("Just for change detection");
			using (var targetStream = _device.OpenWrite(Path.Combine(_bloomFolderPath, "something.modified"), buffer.Length, true))
			{
				targetStream.Write(buffer, 0, buffer.Length);
				targetStream.Flush();
			}

		}

		public string GetDeviceName()
		{
			if (_device == null)
				throw new InvalidOperationException("Must connect before calling GetDeviceName");

			return _device.Name;
		}

		private static void Copy(Stream source, Stream destination)
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
		private bool ConnectAndSendToOneDeviceInternal(IEnumerable<IDevice> devices, Book.Book book, Color backColor, AndroidPublishSettings settings = null)
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
				// Without this, we're depending on the LAST device we tried being the applicable one.
				_bloomFolderPath = GetBloomFolderPath(_device);
				OneReadyDeviceFound(book, backColor, settings);
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
			OneReadyDeviceNotFound?.Invoke(deviceNotFoundReportType,
				devices.Select(d => d.Name).ToList());

			return false;
		}

		private string GetBloomFolderPath(IDevice device)
		{
			try
			{
				foreach (var rso in device.GetDeviceRootStorageObjects())
				{
					var possiblePath = Path.Combine(rso.Name, kBloomFolderOnDevice);
					if (device.GetObjectFromPath(possiblePath) != null)
						return possiblePath;
				}
			}
			catch (COMException e)
			{
				// This can happen when the device is unplugged at just the wrong moment after we enumerated it.
				// Just treat it as not a device that has Bloom.
				SIL.Reporting.Logger.WriteError("Unable to check device for Bloom folder", e);
			}
			return null;
		}
	}
}
#endif
