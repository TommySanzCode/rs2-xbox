using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RS2XboxConnect;

// Only the disposable tunnel process belongs to this job. Never put the saving
// game server here: engine shutdown must remain explicit and graceful.
internal sealed class ChildJob : IDisposable
{
    private readonly SafeFileHandle handle;
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit { public long PerProcessUserTime, PerJobUserTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct Limits { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref Limits value, uint length);
    [DllImport("kernel32.dll", SetLastError=true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    private ChildJob(Process process)
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        var limits = new Limits { Basic = new BasicLimit { Flags = 0x2000 } }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if (handle.IsInvalid || !SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<Limits>()) || !AssignProcessToJobObject(handle, process.Handle)) {
            var error = new Win32Exception(Marshal.GetLastWin32Error()); handle.Dispose(); throw error;
        }
    }
    public static ChildJob? Attach(Process process) => OperatingSystem.IsWindows() ? new ChildJob(process) : null;
    public void Dispose() => handle.Dispose();
}
