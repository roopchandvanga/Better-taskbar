using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Facet.Interop;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using ContainerVisual = Windows.UI.Composition.ContainerVisual;

namespace Facet.Glass;

/// <summary>
/// The dock's glass, drawn by the Windows compositor behind the WPF content: a host-backdrop brush
/// (live blur of whatever is behind the window, apps included), a tint layer, and an anti-aliased
/// rounded-rectangle clip. Unlike the accent-policy blur, this respects any shape — so the dock can
/// have macOS-sized corners — and unlike a window region its edge is smooth.
/// </summary>
internal sealed class CompositionBackdrop
{
    private const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
    private const int DWMWCP_DONOTROUND = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;    // DQTYPE_THREAD_CURRENT = 2
        public int apartmentType; // DQTAT_COM_STA = 2
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        [PreserveSig] int CreateDesktopWindowTarget(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out IntPtr target);
        [PreserveSig] int EnsureOnThread(uint threadId);
    }

    private static IntPtr _queueController; // kept for the life of the process
    private static Compositor? _compositor;

    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly SpriteVisual _tint;
    private readonly CompositionColorBrush _tintBrush;
    private readonly CompositionRoundedRectangleGeometry _shape;

    private CompositionBackdrop(IntPtr hwnd, Compositor compositor)
    {
        var interop = (ICompositorDesktopInterop)Marshal.GetObjectForIUnknown(QueryInterop(compositor));
        Marshal.ThrowExceptionForHR(interop.CreateDesktopWindowTarget(hwnd, false, out var targetPtr));
        _target = DesktopWindowTarget.FromAbi(targetPtr);

        _root = compositor.CreateContainerVisual();
        var blur = compositor.CreateSpriteVisual();
        blur.Brush = compositor.CreateHostBackdropBrush();
        blur.RelativeSizeAdjustment = Vector2.One;
        _tintBrush = compositor.CreateColorBrush();
        _tint = compositor.CreateSpriteVisual();
        _tint.Brush = _tintBrush;
        _tint.RelativeSizeAdjustment = Vector2.One;
        _root.Children.InsertAtTop(blur);
        _root.Children.InsertAtTop(_tint);

        _shape = compositor.CreateRoundedRectangleGeometry();
        _root.Clip = compositor.CreateGeometricClip(_shape);
        _target.Root = _root;
    }

    /// <summary>Set up the compositor glass for a (non-layered) window; null if this Windows can't.</summary>
    public static CompositionBackdrop? TryCreate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || Environment.OSVersion.Version.Build < 18362) return null; // rounded geometry: 1903+
        try
        {
            if (_compositor is null)
            {
                var options = new DispatcherQueueOptions
                {
                    dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                    threadType = 2,
                    apartmentType = 2,
                };
                if (_queueController == IntPtr.Zero)
                    Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _queueController));
                _compositor = new Compositor();
            }

            // let this Win32 window use the host backdrop; draw no DWM border or corner of its own
            var on = 1;
            Native.DwmSetWindowAttribute(hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref on, sizeof(int));
            var noBorder = DWMWA_COLOR_NONE;
            Native.DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
            var square = DWMWCP_DONOTROUND;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref square, sizeof(int));

            // WPF's surface must be see-through so the compositor visuals behind it show
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget is null) return null;
            // the compositor tree covers any WPF content on the same window, so this goes on a content-less
            // window (DockGlassWindow); its own surface just has to stay clear outside the rounded shape
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);

            return new CompositionBackdrop(hwnd, _compositor);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or DllNotFoundException
                                       or EntryPointNotFoundException or TypeLoadException or InvalidOperationException)
        {
            App.Log(ex);
            return null;
        }
    }

    /// <summary>Resize/reshape the glass. Units are the window's physical pixels.</summary>
    public void Update(double widthPx, double heightPx, double radiusPx, Color tint)
    {
        var size = new Vector2((float)widthPx, (float)heightPx);
        _root.Size = size;
        _shape.Size = size;
        _shape.CornerRadius = new Vector2((float)radiusPx, (float)radiusPx);
        _tintBrush.Color = global::Windows.UI.Color.FromArgb(tint.A, tint.R, tint.G, tint.B);
    }

    private static IntPtr QueryInterop(Compositor compositor)
    {
        var unknown = ((WinRT.IWinRTObject)compositor).NativeObject.ThisPtr;
        var iid = typeof(ICompositorDesktopInterop).GUID;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var ptr));
        return ptr;
    }
}
