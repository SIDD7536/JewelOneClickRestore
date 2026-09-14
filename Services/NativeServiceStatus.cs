using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace JewelOneClickRestore.Services;

internal static class NativeServiceStatus
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;

    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceRunning = 0x00000004;
    private const uint ServiceContinuePending = 0x00000005;
    private const uint ServicePausePending = 0x00000006;
    private const uint ServicePaused = 0x00000007;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(
        string? lpMachineName,
        string? lpDatabaseName,
        uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(
        IntPtr hSCManager,
        string lpServiceName,
        uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(
        IntPtr hService,
        int infoLevel,
        ref SERVICE_STATUS_PROCESS lpBuffer,
        int cbBufSize,
        out int pcbBytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);


    public static string? GetServiceAccountName(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var value = Convert.ToString(key?.GetValue("ObjectName"));
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (string.Equals(value, "LocalSystem", StringComparison.OrdinalIgnoreCase))
                return @"NT AUTHORITY\SYSTEM";

            if (value.StartsWith(@".\", StringComparison.Ordinal))
                return $@"{Environment.MachineName}\{value[2..]}";

            return value;
        }
        catch
        {
            return null;
        }
    }

    public static string GetStatus(string serviceName)
    {
        IntPtr scm = IntPtr.Zero;
        IntPtr service = IntPtr.Zero;

        try
        {
            scm = OpenSCManager(null, null, ScManagerConnect);
            if (scm == IntPtr.Zero)
                return "Unknown";

            service = OpenService(scm, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
                return "Not Found";

            var status = new SERVICE_STATUS_PROCESS();
            if (!QueryServiceStatusEx(
                    service,
                    ScStatusProcessInfo,
                    ref status,
                    Marshal.SizeOf<SERVICE_STATUS_PROCESS>(),
                    out _))
            {
                return "Unknown";
            }

            return status.dwCurrentState switch
            {
                ServiceStopped => "Stopped",
                ServiceStartPending => "Starting",
                ServiceStopPending => "Stopping",
                ServiceRunning => "Running",
                ServiceContinuePending => "Continuing",
                ServicePausePending => "Pausing",
                ServicePaused => "Paused",
                _ => "Unknown"
            };
        }
        catch
        {
            return "Unknown";
        }
        finally
        {
            if (service != IntPtr.Zero) CloseServiceHandle(service);
            if (scm != IntPtr.Zero) CloseServiceHandle(scm);
        }
    }
}
