using System;
using System.Runtime.InteropServices;

class MicrophoneDeviceProbe {
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class Enumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevices {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint states, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevice {
        [PreserveSig] int Activate(ref Guid id, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVolume {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float value, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float value, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float value);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float value, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float value);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Caps {
        public ushort manufacturer, product; public uint version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string name;
        public uint formats; public ushort channels, reserved;
    }
    [DllImport("winmm.dll")] static extern uint waveInGetNumDevs();
    [DllImport("winmm.dll", CharSet=CharSet.Unicode)] static extern uint waveInGetDevCaps(UIntPtr id, ref Caps caps, uint size);
    static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
    [STAThread] static int Main() {
        try {
            var devices=(IDevices)new Enumerator(); IDevice device; Check(devices.GetDefaultAudioEndpoint(1,1,out device));
            Guid iid=typeof(IVolume).GUID;object endpoint;Check(device.Activate(ref iid,23,IntPtr.Zero,out endpoint));
            var volume=(IVolume)endpoint;float scalar;bool mute;uint channels;
            Check(volume.GetMasterVolumeLevelScalar(out scalar));Check(volume.GetMute(out mute));Check(volume.GetChannelCount(out channels));
            Console.WriteLine("default_capture_level_percent="+Math.Round(scalar*100)+" muted="+mute+" channels="+channels);
            for(uint i=0;i<waveInGetNumDevs();i++) {
                var caps=new Caps();if(waveInGetDevCaps(new UIntPtr(i),ref caps,(uint)Marshal.SizeOf(typeof(Caps)))==0)
                    Console.WriteLine("input_device_"+i+"="+caps.name+" channels="+caps.channels);
            }
            Marshal.ReleaseComObject(endpoint);Marshal.ReleaseComObject(device);Marshal.ReleaseComObject(devices);
            return 0;
        } catch(Exception error) { Console.WriteLine(error.ToString());return 1; }
    }
}
