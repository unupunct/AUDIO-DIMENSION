using NAudio.CoreAudioApi;
using Serilog;

namespace AudioDimension.Audio.Devices;

public enum AudioSource { Microphone, SystemAudio }

public sealed record AudioDeviceInfo(string Id, string Name, bool IsInput, string Status, bool IsUsable, int SampleRate, int Channels, bool IsDefault)
{
    public override string ToString() => IsDefault ? Name + " (default)" : Name;
}

public static class AudioDeviceService
{
    /// <summary>Lists endpoints. Inputs are microphones; outputs are render devices (usable for loopback and monitoring).</summary>
    public static IReadOnlyList<AudioDeviceInfo> Enumerate(bool inputs)
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            using var en = new MMDeviceEnumerator();
            var flow = inputs ? DataFlow.Capture : DataFlow.Render;
            string? defaultId = null;
            try { using var d = en.GetDefaultAudioEndpoint(flow, Role.Multimedia); defaultId = d.ID; } catch (Exception) { /* none */ }

            foreach (var dev in en.EnumerateAudioEndPoints(flow, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged))
            {
                using (dev)
                {
                    int sr = 0, ch = 0;
                    bool usable = dev.State == DeviceState.Active;
                    if (usable)
                    {
                        try { var f = dev.AudioClient.MixFormat; sr = f.SampleRate; ch = f.Channels; }
                        catch (Exception ex) { Log.Debug(ex, "Mix format unavailable for {Dev}", dev.FriendlyName); }
                    }
                    result.Add(new AudioDeviceInfo(dev.ID, dev.FriendlyName, inputs, DescribeState(dev.State), usable, sr, ch, dev.ID == defaultId));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Audio device enumeration failed");
        }
        return result.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
    }

    public static string DescribeState(DeviceState s) => s switch
    {
        DeviceState.Active => "Ready",
        DeviceState.Disabled => "Disabled",
        DeviceState.Unplugged => "Unplugged",
        DeviceState.NotPresent => "Not present",
        _ => s.ToString()
    };
}
