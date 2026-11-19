using System.Runtime.InteropServices;

namespace Facet.Interop;

/// <summary>Open files in a packaged (Store / MSIX) app by its AppUserModelID.</summary>
internal static class AppActivation
{
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);
        [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
    }

    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint count, IntPtr[] pidls, out IntPtr itemArray);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    /// <returns>True if the app accepted the files.</returns>
    public static bool TryOpenFiles(string appUserModelId, IReadOnlyList<string> files)
    {
        var pidls = new List<IntPtr>();
        var array = IntPtr.Zero;
        object? manager = null;
        try
        {
            foreach (var file in files)
                if (SHParseDisplayName(file, IntPtr.Zero, out var pidl, 0, out _) == 0) pidls.Add(pidl);
            if (pidls.Count == 0) return false;
            if (SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out array) != 0) return false;

            manager = new ApplicationActivationManager();
            return ((IApplicationActivationManager)manager).ActivateForFile(appUserModelId, array, "open", out _) == 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return false;
        }
        finally
        {
            if (array != IntPtr.Zero) Marshal.Release(array);
            foreach (var pidl in pidls) ILFree(pidl);
            if (manager is not null) Marshal.ReleaseComObject(manager);
        }
    }
}
