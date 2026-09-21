using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace RS2XboxConnect;

public sealed class Settings
{
    public string Mode { get; set; } = "host";
    public string WorldId { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; } = 43594;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Template { get; set; } = "";
    public int Preset { get; set; }
    public WorldInvite? Invitation { get; set; }
    public WorldInvite? HostInvitation { get; set; }
    public Dictionary<string, WorldInvite> HostedInvitations { get; set; } = new();
    public Dictionary<string, WorldInvite> JoinedInvitations { get; set; } = new();
    public RelayProfile? Relay { get; set; }
}

internal static class PrivateSettings
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError=true, CharSet=CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static void RestrictDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            Blob output;
            bool ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { LocalFree(output.Data); }
        } finally { Marshal.FreeHGlobal(input.Data); Array.Clear(bytes); }
    }
    public static Settings Load(string path) => !File.Exists(path) ? new Settings() : JsonSerializer.Deserialize<Settings>(Transform(File.ReadAllBytes(path), false), ProfileFiles.Json) ?? new Settings();
    public static void Save(string path, Settings settings)
    {
        var encrypted = Transform(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, ProfileFiles.Json)), true);
        File.WriteAllBytes(path + ".tmp", encrypted); File.Move(path + ".tmp", path, true);
    }
}
