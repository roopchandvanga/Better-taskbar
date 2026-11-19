using System.Runtime.InteropServices;

namespace Facet.Interop;

/// <summary>Core Audio endpoint volume (speakers and microphone) via COM.</summary>
internal static class AudioInterop
{
    private const int eRender = 0;
    private const int eCapture = 1;
    private const int eMultimedia = 1;
    private const int CLSCTX_ALL = 23;
    private static readonly Guid IID_IAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCoClass;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public readonly record struct EndpointState(bool Available, float Volume, bool Muted);

    public static EndpointState GetSpeakers() => Read(eRender);
    public static EndpointState GetMicrophone() => Read(eCapture);

    public static void SetSpeakerVolume(float level) => With(eRender, v =>
    {
        var ctx = Guid.Empty;
        v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref ctx);
    });

    public static void SetSpeakerMute(bool mute) => With(eRender, v => { var ctx = Guid.Empty; v.SetMute(mute, ref ctx); });
    public static void SetMicMute(bool mute) => With(eCapture, v => { var ctx = Guid.Empty; v.SetMute(mute, ref ctx); });

    private static EndpointState Read(int flow)
    {
        var state = new EndpointState(false, 0, false);
        With(flow, v =>
        {
            v.GetMasterVolumeLevelScalar(out var level);
            v.GetMute(out var muted);
            state = new EndpointState(true, level, muted);
        });
        return state;
    }

    private static void With(int flow, Action<IAudioEndpointVolume> action)
    {
        object? enumerator = null, device = null, volume = null;
        try
        {
            enumerator = new MMDeviceEnumeratorCoClass();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(flow, eMultimedia, out var dev) != 0 || dev is null) return;
            device = dev;
            var iid = IID_IAudioEndpointVolume;
            if (dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var vol) != 0 || vol is null) return;
            volume = vol;
            action((IAudioEndpointVolume)vol);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // no audio device / service unavailable
        }
        finally
        {
            if (volume is not null) Marshal.ReleaseComObject(volume);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }
}
