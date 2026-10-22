
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VantaraMCUTool;

public partial class MainWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(
        out SystemPowerStatus systemPowerStatus);
    private string? selectedFirmwarePath;
    private string? selectedFirmwareVersion;
    private string? selectedFirmwareHash;

    public MainWindow()
    {
        InitializeComponent();
        RefreshSystem();
        WriteLog("Vantara MCU Tool started.");
        WriteLog("Diagnostic-only mode: no firmware writing is available.");

        bool acConnected = TryGetAcPowerStatus(out string powerStatus);
        WriteLog("Power check: " + powerStatus);
    }

    private bool TryGetAcPowerStatus(out string status)
    {
        status = "Unknown — power status could not be read";

        try
        {
            if (!GetSystemPowerStatus(out SystemPowerStatus power))
                return false;

            switch (power.ACLineStatus)
            {
                case 1:
                    status = "AC power connected";
                    return true;

                case 0:
                    status = "AC power disconnected";
                    return false;

                default:
                    status = "AC power status unknown";
                    return false;
            }
        }
        catch (Exception ex)
        {
            status = "AC power check failed: " + ex.Message;
            return false;
        }
    }
    private void WriteLog(string message)
    {
        OutputLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        OutputLog.ScrollToEnd();
    }

    private void RefreshSystem()
    {
        OsValue.Text = Environment.OSVersion.Version.ToString();

        DeviceNameValue.Text = Environment.MachineName;
        ModelValue.Text = "Not queried";
        SerialValue.Text = "Not queried";
        HardwareIdValue.Text = "Not queried";
        BiosValue.Text = "Not queried";

        DeviceDetails.Text =
            $"Computer name: {Environment.MachineName}{Environment.NewLine}" +
            $"Operating system: {Environment.OSVersion}{Environment.NewLine}" +
            $"64-bit operating system: {Environment.Is64BitOperatingSystem}{Environment.NewLine}" +
            $".NET runtime: {Environment.Version}{Environment.NewLine}" +
            $"Processor count: {Environment.ProcessorCount}{Environment.NewLine}" +
            $"User profile: {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}";

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments =
                "-NoProfile -NonInteractive -Command " +
                "\"$cs=Get-CimInstance Win32_ComputerSystem; " +
                "$bios=Get-CimInstance Win32_BIOS; " +
                "$p=Get-CimInstance Win32_ComputerSystemProduct; " +
                "[pscustomobject]@{Model=$cs.Model;Serial=$bios.SerialNumber;" +
                "Bios=$bios.SMBIOSBIOSVersion;ProductId=$p.IdentifyingNumber} " +
                "| ConvertTo-Json -Compress\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0 &&
                !string.IsNullOrWhiteSpace(output))
            {
                using var document =
                    System.Text.Json.JsonDocument.Parse(output);

                var root = document.RootElement;

                string ReadValue(string name)
                {
                    if (root.TryGetProperty(name, out var value) &&
                        value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        string result = value.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(result) &&
                            !result.Equals("To be filled by O.E.M.",
                                StringComparison.OrdinalIgnoreCase))
                            return result.Trim();
                    }

                    return "Not available";
                }

                ModelValue.Text = ReadValue("Model");
                SerialValue.Text = ReadValue("Serial");
                BiosValue.Text = ReadValue("Bios");
                HardwareIdValue.Text = ReadValue("ProductId");
            }
            else
            {
                WriteLog("Windows system inventory query failed.");
            }
        }
        catch (Exception ex)
        {
            WriteLog("System inventory query failed: " + ex.GetType().Name);
        }

        WriteLog("Local system information refreshed.");
        WriteLog("MCU firmware version has not been queried directly by this application.");
    }

    private void RefreshSystem_Click(object sender, RoutedEventArgs e)
    {
        RefreshSystem();

        bool acConnected = TryGetAcPowerStatus(out string powerStatus);
        WriteLog("Power check: " + powerStatus);
    }

    private void CopyDeviceInformation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string report =
                "Vantara MCU Tool - Device Information" + Environment.NewLine +
                "======================================" + Environment.NewLine +
                $"Device name: {DeviceNameValue.Text}" + Environment.NewLine +
                $"Model: {ModelValue.Text}" + Environment.NewLine +
                $"Serial number: {SerialValue.Text}" + Environment.NewLine +
                $"Hardware identifier: {HardwareIdValue.Text}" + Environment.NewLine +
                $"BIOS version: {BiosValue.Text}" + Environment.NewLine +
                $"Operating system: {Environment.OSVersion}" + Environment.NewLine +
                $"64-bit operating system: {Environment.Is64BitOperatingSystem}" + Environment.NewLine +
                $"MCU firmware: {McuVersionValue.Text}" + Environment.NewLine +
                $"MCU read status: {McuStatusValue.Text}";

            Clipboard.SetText(report);
            WriteLog("Device information copied to clipboard.");

            MessageBox.Show(
                "Device information copied to the clipboard.",
                "Vantara MCU Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not copy device information: " + ex.Message,
                "Vantara MCU Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CopyAsusDeviceDetails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string text = AsusDeviceDetails.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(
                    "There are no detection results to copy yet.",
                    "Vantara MCU Tool",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(text);

            MessageBox.Show(
                "Detection results copied to the clipboard.",
                "Vantara MCU Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            WriteLog("ASUS device detection results copied to clipboard.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not copy the results: " + ex.Message,
                "Vantara MCU Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DetectAsusDevice_Click(object sender, RoutedEventArgs e)
    {
        AsusDeviceDetails.Text = "Checking Windows device inventory...";
        WriteLog("Starting read-only ASUS device detection.");

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(
                "$ErrorActionPreference = 'Stop'; " +
                "$all = @(Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -like '*VID_0B05&PID_1B4C*' }); " +
                "Write-Output ('Matching Windows PnP entries found: ' + $all.Count); " +
                "$selected = @($all | Where-Object { $_.InstanceId -like 'USB\\VID_0B05&PID_1B4C\\*' } | Select-Object -First 1) + " +
                "@($all | Where-Object { $_.FriendlyName -eq 'ASUS Component Firmware Update' } | Select-Object -First 1); " +
                "$selected | ForEach-Object { " +
                "$id = [string]$_.InstanceId; " +
                "$vid = if ($id -match '(?i)VID_([0-9A-F]{4})') { $Matches[1].ToUpperInvariant() } else { 'Not specified' }; " +
                "$productId = if ($id -match '(?i)PID_([0-9A-F]{4})') { $Matches[1].ToUpperInvariant() } else { 'Not specified' }; " +
                "$mi = if ($id -match '(?i)&MI_([0-9A-F]{2})') { $Matches[1].ToUpperInvariant() } else { 'Not specified' }; " +
                "$entryType = if ($_.FriendlyName -eq 'ASUS Component Firmware Update') { 'Firmware-update interface (firmware not queried)' } else { 'Main ASUS USB composite device' }; " +
                "[pscustomobject]@{ EntryType=$entryType; Status=$_.Status; Class=$_.Class; FriendlyName=$_.FriendlyName; VID=$vid; PID=$productId; MI=$mi; InstanceId=$id } " +
                "} | Format-List | Out-String -Width 240");

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "Could not start Windows PowerShell.");

            if (!process.WaitForExit(10000))
            {
                process.Kill();
                AsusDeviceDetails.Text =
                    "Detection timed out. No MCU commands were sent.";
                WriteLog("ASUS device detection timed out.");
                return;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            string error = process.StandardError.ReadToEnd().Trim();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? "Windows device inventory query failed."
                        : error);
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                AsusDeviceDetails.Text =
                    "No present Windows device entry matching " +
                    "ASUS VID_0B05 and PID_1B4C was found.";
                WriteLog("No matching present ASUS device entry found.");
                return;
            }

            AsusDeviceDetails.Text =
                "Matching Windows device entries:" +
                Environment.NewLine + output +
                Environment.NewLine +
                "Detection only â€” MCU firmware has NOT been queried.";

            bool mainDeviceFound = output.Contains(
                "EntryType    : Main ASUS USB composite device");
            bool interfaceFound = output.Contains(
                "EntryType    : Firmware-update interface (firmware not queried)");
            bool windowsStatusOk = mainDeviceFound && interfaceFound &&
                System.Text.RegularExpressions.Regex.IsMatch(
                    output, @"Status\s*:\s*OK");

            AsusMainStatus.Text = mainDeviceFound
                ? "ASUS device: Detected"
                : "ASUS device: Not found";
            AsusInterfaceStatus.Text = interfaceFound
                ? "Firmware-update interface: Detected"
                : "Firmware-update interface: Not found";
            AsusWindowsStatus.Text = windowsStatusOk
                ? "Windows device entries: Status OK"
                : "Windows device entries: Check results";

            AsusOverallStatus.Text =
                mainDeviceFound && interfaceFound && windowsStatusOk
                    ? "Overall status: Device detected"
                    : mainDeviceFound || interfaceFound
                        ? "Overall status: Partially detected"
                        : "Overall status: Device not found";

            var accentBrush = (Brush)FindResource("Accent");
            AsusOverallStatus.Foreground =
                mainDeviceFound && interfaceFound && windowsStatusOk
                    ? accentBrush
                    : (Brush)FindResource("Muted");

            AsusMainStatus.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#59C3A5"));
            AsusInterfaceStatus.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#59C3A5"));
            AsusWindowsStatus.Foreground = windowsStatusOk
                ? new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#59C3A5"))
                : (Brush)FindResource("Muted");

            var countMatch = System.Text.RegularExpressions.Regex.Match(
                output, @"Matching Windows PnP entries found: (\d+)");
            if (countMatch.Success)
            {
                WriteLog("Windows PnP inventory found " +
                    countMatch.Groups[1].Value + " matching entries.");
            }

            WriteLog("Matching ASUS device entry found in Windows inventory.");
            WriteLog("No HID feature reports or MCU commands were sent.");
        }
        catch (Exception ex)
        {
            AsusDeviceDetails.Text =
                "Device detection failed: " + ex.Message;
            WriteLog("ASUS device detection failed: " + ex.Message);
        }
    }

    private void ReadMcuVersion_Click(object sender, RoutedEventArgs e)
    {
        McuVersionValue.Text = "Not queried";
        McuStatusValue.Text = "Checking ASUS device...";
        WriteLog("MCU version read requested.");

        try
        {
            const string expectedHash =
                "7D3227FA24435A594B48E49AA331AB14C78FC3934D272D18F68CB18DC615F380";

            string dllPath = Path.Combine(
                AppContext.BaseDirectory, "Native", "OEM_Library.dll");

            if (!File.Exists(dllPath))
                throw new FileNotFoundException(
                    "The ASUS native reader DLL is missing.", dllPath);

            string actualHash;
            using (var stream = File.OpenRead(dllPath))
            using (var sha = SHA256.Create())
            {
                actualHash = Convert.ToHexString(sha.ComputeHash(stream));
            }

            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The ASUS native DLL failed its SHA-256 integrity check.");

            bool detected = AsusMcuNative.GetAsusDevice(20, 0x1B4C);

            if (!detected)
            {
                McuStatusValue.Text =
                    "ASUS device not detected by native reader";
                WriteLog("Native ASUS device detection returned false.");
                return;
            }

            var buffer = new byte[100];
            AsusMcuNative.GetFirmwareVersion(buffer);

            int terminator = Array.IndexOf(buffer, (byte)0);
            int length = terminator >= 0 ? terminator : buffer.Length;
            string version = Encoding.ASCII.GetString(buffer, 0, length).Trim();

            if (string.IsNullOrWhiteSpace(version) ||
                !version.StartsWith("FGA", StringComparison.OrdinalIgnoreCase) ||
                version.Length > 64 ||
                version.Any(c => !(char.IsLetterOrDigit(c) ||
                                   c == '.' || c == '-' || c == '_')))
            {
                McuStatusValue.Text = "No valid firmware version returned";
                WriteLog("Native reader returned an empty or invalid version string.");
                return;
            }

            McuVersionValue.Text = version;
            McuStatusValue.Text = "Read directly from ASUS MCU interface";
            WriteLog("ASUS native device detection succeeded.");
            WriteLog("MCU firmware version read successfully: " + version);
        }
        catch (Exception ex)
        {
            McuVersionValue.Text = "Not queried";
            McuStatusValue.Text = "Read failed â€” see log";
            WriteLog("MCU version read failed: " +
                ex.GetType().Name + ": " + ex.Message);

            MessageBox.Show(
                "The MCU version could not be read.\n\n" + ex.Message +
                "\n\nNo firmware update, reset, or ROM-switch function was called.",
                "MCU version read failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void InspectFirmware_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an MCU firmware file",
            Filter = "Firmware binary (*.bin)|*.bin|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        selectedFirmwarePath = null;
        selectedFirmwareVersion = null;
        selectedFirmwareHash = null;

        try
        {
            var info = new FileInfo(dialog.FileName);
            string hash;

            using (var stream = File.OpenRead(dialog.FileName))
            using (var sha = SHA256.Create())
            {
                hash = Convert.ToHexString(sha.ComputeHash(stream));
            }

            const long expectedSize = 262144;

            string expectedHash = "";
            string expectedVersion = "";

            if (string.Equals(info.Name, "FGA80101.RC73YA.334.bin",
                StringComparison.OrdinalIgnoreCase))
            {
                expectedVersion = "V334";
                expectedHash = "3CC7704B58BB559E531194D5D66393105C2AD80DFB9AD95544C8F606C5ECB63C";
            }
            else if (string.Equals(info.Name, "FGA80101.RC73YA.335.bin",
                StringComparison.OrdinalIgnoreCase))
            {
                expectedVersion = "V335";
                expectedHash = "BE73A72F773D9CE13DDD9994FE441555282A215661C8AA0E78B60BB2AF872C85";
            }

            bool knownFile = !string.IsNullOrEmpty(expectedVersion);
            bool sizeMatches = info.Length == expectedSize;
            bool hashMatches = knownFile &&
                string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase);

            string result;
            if (knownFile && sizeMatches && hashMatches)
            {
                selectedFirmwarePath = info.FullName;
                selectedFirmwareVersion = expectedVersion;
                selectedFirmwareHash = hash;

                result = $"LOCAL REFERENCE MATCH — {expectedVersion}";
            }
            else
            {
                result = "UNVERIFIED — do not treat this file as a verified package";
            }

            FirmwareDetails.Text =
                $"Result: {result}{Environment.NewLine}" +
                $"File: {info.Name}{Environment.NewLine}" +
                $"Size: {info.Length:N0} bytes " +
                $"(expected for known files: {expectedSize:N0}){Environment.NewLine}" +
                $"Size check: {(sizeMatches ? "MATCH" : "MISMATCH")}{Environment.NewLine}" +
                $"SHA-256: {hash}{Environment.NewLine}" +
                $"Reference hash: {(knownFile ? expectedHash : "No reference for this filename")}{Environment.NewLine}" +
                $"Hash check: {(hashMatches ? "MATCH" : "NO MATCH / UNKNOWN")}{Environment.NewLine}" +
                $"Path: {info.FullName}{Environment.NewLine}{Environment.NewLine}" +
                "Important: A local reference match is not independent ASUS payload authentication " +
                "and does not establish device compatibility. No firmware was written.";

            WriteLog($"Inspected firmware file: {info.Name}");
            WriteLog($"Size: {info.Length:N0} bytes; SHA-256: {hash}");
            WriteLog($"Firmware inspection result: {result}");
            WriteLog("Inspection only; no firmware write, reset, or ROM-switch command was called.");
        }
        catch (Exception ex)
        {
            WriteLog($"Could not inspect file: {ex.Message}");
            MessageBox.Show(ex.Message, "Firmware inspection failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an ASUS MCU updater log",
            Filter = "Log and text files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = @"C:\ProgramData\ASUS\ROGMCUFWUpdateToolLog"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            string content = File.ReadAllText(dialog.FileName);
            OutputLog.Text = content;
            OutputLog.ScrollToEnd();
            WriteLog($"Loaded log: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            WriteLog($"Could not read log: {ex.Message}");
            MessageBox.Show(ex.Message, "Log reading failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool _isLightTheme;

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _isLightTheme = !_isLightTheme;

        SetBrush("WindowBackground", _isLightTheme ? "#F3F5F7" : "#10151C");
        SetBrush("MainForeground", _isLightTheme ? "#20252B" : "#E8EDF4");
        SetBrush("Panel", _isLightTheme ? "#FFFFFF" : "#1A222D");
        SetBrush("Muted", _isLightTheme ? "#5C6570" : "#9AA9BB");
        SetBrush("Accent", _isLightTheme ? "#347B62" : "#59C3A5");
        SetBrush("ButtonBackground", _isLightTheme ? "#E5EAEF" : "#263443");
        SetBrush("ButtonForeground", _isLightTheme ? "#20252B" : "#F0F5FA");
        SetBrush("InsetBackground", _isLightTheme ? "#EDF0F3" : "#10151C");
        SetBrush("LogForeground", _isLightTheme ? "#303943" : "#C6D4E3");
        SetBrush("NavHeading", _isLightTheme ? "#5C6570" : "#8FA1B5");
        SetBrush("ActiveBackground", _isLightTheme ? "#347B62" : "#245748");
        SetBrush("Warning", _isLightTheme ? "#8A5B12" : "#E6B86A");

        if (sender is Button button)
        {
            button.Content = _isLightTheme
                ? "Switch to dark mode"
                : "Switch to light mode";
        }

        WriteLog(_isLightTheme
            ? "Light theme enabled."
            : "Dark theme enabled.");
    }

    private void SetBrush(string key, string colour)
    {
        var converter = new BrushConverter();
        Resources[key] = (Brush)converter.ConvertFromString(colour)!;
    }
}

internal static class AsusMcuNative
{
    static AsusMcuNative()
    {
        NativeLibrary.SetDllImportResolver(
            typeof(AsusMcuNative).Assembly,
            (name, assembly, searchPath) =>
            {
                if (name.Equals(
                    @"Native\OEM_Library.dll",
                    StringComparison.OrdinalIgnoreCase))
                {
                    string path = Path.Combine(
                        AppContext.BaseDirectory,
                        "Native",
                        "OEM_Library.dll");

                    return NativeLibrary.Load(path);
                }

                return IntPtr.Zero;
            });
    }

    [DllImport(
        @"Native\OEM_Library.dll",
        EntryPoint = "OEM_GetASUSDevice",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern bool GetAsusDevice(
        int checkType,
        int device);

    [DllImport(
        @"Native\OEM_Library.dll",
        EntryPoint = "OEM_GetFWVersion",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern void GetFirmwareVersion(
        byte[] buffer);
}





