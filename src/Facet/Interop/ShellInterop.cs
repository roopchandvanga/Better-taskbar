using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Facet.Interop;

/// <summary>Shell COM: shortcuts, AppUserModelIDs, and high-quality icons with alpha.</summary>
internal static class ShellInterop
{
    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid IID_IShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
    private static PROPERTYKEY PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY(Guid fmtid, uint pid)
    {
        public Guid fmtid = fmtid;
        public uint pid = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public IntPtr p1;
        public IntPtr p2;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Native.SIZE size, int flags, out IntPtr hbitmap);
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr pidl);
        [PreserveSig] int SetIDList(IntPtr pidl);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int max);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int max);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCmd(out int cmd);
        [PreserveSig] int SetShowCmd(int cmd);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr hwnd, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        [PreserveSig] int GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string file, bool remember);
        [PreserveSig] int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        [PreserveSig] int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass;

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindCtx, int flags, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pv);

    private const ushort VT_LPWSTR = 31;
    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;

    public static string? GetWindowAppId(IntPtr hwnd)
    {
        try
        {
            var iid = IID_IPropertyStore;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store is null) return null;
            try { return ReadString(store); }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
    }

    public static string? GetFileAppId(string path)
    {
        try
        {
            var iid = IID_IPropertyStore;
            if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out var store) != 0 || store is null) return null;
            try { return ReadString(store); }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or FileNotFoundException)
        {
            return null;
        }
    }

    private static string? ReadString(IPropertyStore store)
    {
        if (store.GetValue(ref PKEY_AppUserModel_ID, out var pv) != 0) return null;
        try
        {
            return pv.vt == VT_LPWSTR && pv.p1 != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p1) : null;
        }
        finally
        {
            PropVariantClear(ref pv);
        }
    }

    /// <summary>Resolve a .lnk to (target path, arguments). Target is empty for shell-namespace links.</summary>
    public static (string Target, string Arguments) ResolveShortcut(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new ShellLinkCoClass();
            ((IPersistFile)link).Load(lnkPath, 0);
            var sl = (IShellLinkW)link;
            var target = new StringBuilder(1024);
            sl.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(1024);
            sl.GetArguments(args, args.Capacity);
            return (Environment.ExpandEnvironmentVariables(target.ToString()), args.ToString());
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return ("", "");
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>Icon for any shell parsing name (file, .lnk, "shell:AppsFolder\AUMID"), with alpha.</summary>
    public static BitmapSource? GetShellIcon(string parsingName, int sizePx)
    {
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = IID_IShellItemImageFactory;
            if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory) != 0 || factory is null) return null;
            // BIGGERSIZEOK: take the app's own larger frame rather than the shell's (low-quality) resize;
            // callers ask for 2x the display size and let WPF halve it cleanly
            if (factory.GetImage(new Native.SIZE(sizePx, sizePx), SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hbmp) != 0
                || hbmp == IntPtr.Zero)
                return null;
            try { return FromHBitmap(hbmp); }
            finally { Native.DeleteObject(hbmp); }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or FileNotFoundException)
        {
            return null;
        }
        finally
        {
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    public static BitmapSource? GetWindowIcon(IntPtr hwnd)
    {
        var icon = IntPtr.Zero;
        foreach (var kind in new[] { Native.ICON_BIG, Native.ICON_SMALL2 })
        {
            Native.SendMessageTimeout(hwnd, Native.WM_GETICON, new IntPtr(kind), IntPtr.Zero,
                Native.SMTO_ABORTIFHUNG, 60, out icon);
            if (icon != IntPtr.Zero) break;
        }
        if (icon == IntPtr.Zero) icon = Native.GetClassLongPtr(hwnd, Native.GCLP_HICON);
        if (icon == IntPtr.Zero) icon = Native.GetClassLongPtr(hwnd, Native.GCLP_HICONSM);
        if (icon == IntPtr.Zero) return null;
        try
        {
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
    }

    private static BitmapSource? FromHBitmap(IntPtr hbmp)
    {
        if (Native.GetObject(hbmp, Marshal.SizeOf<Native.BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0) return null;

        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[w * h * 4];
        var dc = Native.GetDC(IntPtr.Zero);
        try
        {
            if (Native.GetDIBits(dc, hbmp, 0, (uint)h, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, dc);
        }

        // The shell hands back either premultiplied or straight alpha depending on the icon. Treating straight
        // alpha as premultiplied makes semi-transparent edge pixels too bright: a light fringe around icons.
        // In premultiplied data no colour channel can exceed alpha, so any pixel that does means straight.
        var hasAlpha = false;
        var straight = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a == 0) continue;
            hasAlpha = true;
            if (pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a)
            {
                straight = true;
                break;
            }
        }

        var format = !hasAlpha ? PixelFormats.Bgr32 : straight ? PixelFormats.Bgra32 : PixelFormats.Pbgra32;
        var src = BitmapSource.Create(w, h, 96, 96, format, null, pixels, w * 4);
        src.Freeze();
        return src;
    }
}
