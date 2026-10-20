using System.Runtime.Versioning;
using NAudio.CoreAudioApi;

namespace SttConsole.Services.Audio;

[SupportedOSPlatform("windows")]
internal static class WasapiAudioSources
{
    private const string MicPrefix = "[Mic] ";
    private const string OutputPrefix = "[PC Output] ";

    public static IReadOnlyList<string> List()
    {
        var labels = new List<string> { AudioSourceLabels.Default };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                labels.Add(MicPrefix + device.FriendlyName);
            }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                labels.Add(OutputPrefix + device.FriendlyName);
            }
        }
        catch (Exception)
        {
            // Enumeration failure leaves only the default entry; Start reports the real error.
        }

        return labels;
    }

    /// <summary>Opens the device for <paramref name="label"/>. Throws when it no longer exists.</summary>
    public static (MMDevice Device, bool Loopback) Resolve(string label, MMDeviceEnumerator enumerator)
    {
        if (label == AudioSourceLabels.Default || string.IsNullOrEmpty(label))
        {
            return (enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console), false);
        }

        var loopback = label.StartsWith(OutputPrefix, StringComparison.Ordinal);
        var name = label[(loopback ? OutputPrefix : MicPrefix).Length..];
        var flow = loopback ? DataFlow.Render : DataFlow.Capture;

        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            if (device.FriendlyName == name)
            {
                return (device, loopback);
            }
        }

        throw new InvalidOperationException($"Audio device not found: {label}");
    }
}
