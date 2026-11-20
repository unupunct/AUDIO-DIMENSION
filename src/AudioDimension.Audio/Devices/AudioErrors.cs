using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AudioDimension.Audio.Devices;

public enum MicrophonePermission { Allowed, Denied, Unknown }

public static class AudioErrors
{
    public const int DeviceInvalidated = unchecked((int)0x88890004);
    public const int DeviceInUse = unchecked((int)0x8889000A);
    public const int UnsupportedFormat = unchecked((int)0x88890008);
    public const int AccessDenied = unchecked((int)0x80070005);
    public const int NotFound = unchecked((int)0x80070490);

    /// <summary>Converts low-level WASAPI failures into readable text. Technical detail belongs in the log.</summary>
    public static string Describe(Exception ex, bool isMicrophone)
    {
        int hr = ex is COMException c ? c.HResult : ex.HResult;
        return hr switch
        {
            DeviceInvalidated => "The audio device was disconnected or changed. Reconnect it or choose another device.",
            DeviceInUse => "The audio device is in use by another application in exclusive mode.",
            UnsupportedFormat => "The device does not support the required audio format.",
            AccessDenied when isMicrophone => "Windows blocked microphone access. Enable 'Let desktop apps access your microphone' in Windows privacy settings.",
            AccessDenied => "Access to the audio device was denied.",
            NotFound => "The selected audio device could not be found.",
            _ => ex is UnauthorizedAccessException && isMicrophone
                ? "Windows blocked microphone access. Enable 'Let desktop apps access your microphone' in Windows privacy settings."
                : "Audio capture failed: " + ex.Message
        };
    }

    /// <summary>Reads the Windows microphone consent switches (global and per-user).</summary>
    public static MicrophonePermission CheckMicrophonePermission()
    {
        try
        {
            const string sub = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var key = root.OpenSubKey(sub);
                if (key?.GetValue("Value") is string v && v.Equals("Deny", StringComparison.OrdinalIgnoreCase))
                    return MicrophonePermission.Denied;
            }
            using var user = Registry.CurrentUser.OpenSubKey(sub);
            return user?.GetValue("Value") is string s && s.Equals("Allow", StringComparison.OrdinalIgnoreCase)
                ? MicrophonePermission.Allowed : MicrophonePermission.Unknown;
        }
        catch (Exception) { return MicrophonePermission.Unknown; }
    }
}
