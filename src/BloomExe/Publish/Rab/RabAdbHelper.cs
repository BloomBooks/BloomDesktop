using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.web;
using SIL.IO;

namespace Bloom.Publish.Rab
{
    internal class RabAdbConnectedDevice
    {
        public string Serial { get; set; }
        public string Product { get; set; }
        public string Model { get; set; }
        public string Device { get; set; }

        public string DisplayName => MakeFriendlyDeviceName(Model, Device, Product, Serial);

        private static string MakeFriendlyDeviceName(
            string model,
            string device,
            string product,
            string serial
        )
        {
            var preferredValue = new[] { model, device, product, serial }.FirstOrDefault(value =>
                !string.IsNullOrWhiteSpace(value)
            );
            if (string.IsNullOrWhiteSpace(preferredValue))
                return "Android device";

            return preferredValue.Replace('_', ' ').Trim();
        }
    }

    internal static class RabAdbHelper
    {
        internal static string ResolveAdbPath(
            IReadOnlyDictionary<string, string> environmentVariables,
            Func<string, bool> fileExists = null
        )
        {
            // For the Bloom-managed RAB flow, only use Bloom's own Android SDK install so we don't
            // accidentally pick up some unrelated SDK from the machine.
            fileExists = fileExists ?? RobustFile.Exists;

            var localAppData = GetEnvironmentVariable(environmentVariables, "LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(localAppData))
                return null;

            var bloomManagedAdbPath = Path.Combine(
                localAppData,
                "SIL",
                "Bloom",
                "ReadingAppBuilder",
                "android-sdk",
                "platform-tools",
                "adb.exe"
            );

            return fileExists(bloomManagedAdbPath) ? bloomManagedAdbPath : null;
        }

        internal static string ResolveAdbPath()
        {
            return ResolveAdbPath(GetEnvironmentVariables());
        }

        internal static RabAdbConnectedDevice GetSingleConnectedDevice(
            string adbPath,
            string workingDirectory,
            IWebSocketProgress progress
        )
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo()
                {
                    FileName = adbPath,
                    Arguments = "devices -l",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                if (!process.Start())
                    throw new ApplicationException("Bloom could not start adb.");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(error))
                    progress?.MessageWithoutLocalizing(error, ProgressKind.Warning);
                if (process.ExitCode != 0)
                    throw new ApplicationException(
                        $"adb devices exited with code {process.ExitCode}."
                    );

                var devices = ParseConnectedDevices(output).ToList();

                if (devices.Count == 0)
                    throw new ApplicationException("No Android device is connected over USB.");
                if (devices.Count > 1)
                {
                    throw new ApplicationException(
                        "More than one Android device is connected: "
                            + string.Join(", ", devices.Select(device => device.DisplayName))
                            + ". Disconnect extra devices and try again."
                    );
                }

                return devices[0];
            }
        }

        internal static IReadOnlyList<string> ParseConnectedDeviceSerials(string adbDevicesOutput)
        {
            return ParseConnectedDevices(adbDevicesOutput).Select(device => device.Serial).ToList();
        }

        internal static IReadOnlyList<string> ParseConnectedDeviceDisplayNames(
            string adbDevicesOutput
        )
        {
            return ParseConnectedDevices(adbDevicesOutput)
                .Select(device => device.DisplayName)
                .ToList();
        }

        internal static string BuildLaunchAppArguments(string deviceSerial, string packageName)
        {
            return string.Join(
                " ",
                new[]
                {
                    "-s",
                    QuoteArgument(deviceSerial),
                    "shell monkey",
                    "-p",
                    QuoteArgument(packageName),
                    "-c android.intent.category.LAUNCHER",
                    "1",
                }
            );
        }

        private static string GetEnvironmentVariable(
            IReadOnlyDictionary<string, string> environmentVariables,
            string variableName
        )
        {
            if (environmentVariables == null)
                return null;

            environmentVariables.TryGetValue(variableName, out var value);
            return value;
        }

        private static IReadOnlyDictionary<string, string> GetEnvironmentVariables()
        {
            var environmentVariables = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key)
                    environmentVariables[key] = entry.Value?.ToString();
            }

            return environmentVariables;
        }

        private static IEnumerable<RabAdbConnectedDevice> ParseConnectedDevices(
            string adbDevicesOutput
        )
        {
            return (adbDevicesOutput ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseConnectedDevice)
                .Where(device => device != null)
                .Where(device => !IsWindowsSubsystemForAndroid(device));
        }

        private static RabAdbConnectedDevice ParseConnectedDevice(string line)
        {
            var trimmedLine = line?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
                return null;

            if (
                trimmedLine.StartsWith(
                    "List of devices attached",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return null;

            var tokens = trimmedLine
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            if (tokens.Length < 2)
                return null;

            if (!string.Equals(tokens[1], "device", StringComparison.OrdinalIgnoreCase))
                return null;

            return new RabAdbConnectedDevice()
            {
                Serial = tokens[0],
                Product = GetAdbMetadataValue(tokens, "product"),
                Model = GetAdbMetadataValue(tokens, "model"),
                Device = GetAdbMetadataValue(tokens, "device"),
            };
        }

        private static bool IsWindowsSubsystemForAndroid(RabAdbConnectedDevice device)
        {
            if (device == null)
                return false;

            // Ignore WSA so local Windows Android emulation does not count as a real attached phone.
            return string.Equals(
                    device.Product,
                    "windows_x86_64",
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    device.Device,
                    "windows_x86_64",
                    StringComparison.OrdinalIgnoreCase
                )
                || (
                    !string.IsNullOrWhiteSpace(device.Model)
                    && device.Model.IndexOf(
                        "subsystem_for_android",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0
                );
        }

        private static string GetAdbMetadataValue(IEnumerable<string> tokens, string key)
        {
            var prefix = key + ":";
            var token = tokens.FirstOrDefault(entry =>
                entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            );
            return token?.Substring(prefix.Length);
        }

        private static string QuoteArgument(string value)
        {
            if (value == null)
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
