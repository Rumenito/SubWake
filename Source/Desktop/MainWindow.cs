using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SubWake;

/// <summary>
/// The settings window: a dark-titled WinForms window hosting the HTML interface in WebView2.
/// It is created when opened and disposed when closed, so the tray app stays small while it runs in the background.
/// </summary>
sealed class MainWindow : Form
{
    const double Zoom = 1.1;   // Slightly enlarged; easier to read on high-resolution screens.
    readonly WebView2 browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(13, 14, 16) };

    public event Action<JsonElement>? MessageReceived;
    public bool Ready { get; private set; }

    public MainWindow(int[]? savedBounds)
    {
        Text = "SubWake";
        Icon = AppIcon.Window;
        BackColor = Color.FromArgb(13, 14, 16);
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        ClientSize = new Size(LogicalToDeviceUnits((int)(440 * Zoom)), Math.Min(LogicalToDeviceUnits((int)(800 * Zoom)), area.Height - LogicalToDeviceUnits(60)));
        MinimumSize = new Size(LogicalToDeviceUnits((int)(400 * Zoom)), LogicalToDeviceUnits(480));
        // Reopen where it was last closed, if that spot is still on a screen.
        if (savedBounds is [int x, int y, int width, int height])
        {
            var saved = new Rectangle(x, y, Math.Max(width, MinimumSize.Width), Math.Max(height, MinimumSize.Height));
            var titleBar = new Rectangle(saved.X + 40, saved.Y + 8, Math.Max(1, saved.Width - 80), 24);
            if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(titleBar)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = saved;
            }
        }
        Controls.Add(browser);
    }

    /// <summary>Position and size to restore next time.</summary>
    public int[] SavedBounds
    {
        get
        {
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            return [b.X, b.Y, b.Width, b.Height];
        }
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView);
            await browser.EnsureCoreWebView2Async(environment);
            browser.ZoomFactor = Zoom;
            var core = browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("subwake.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += (_, args) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                    if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "ready") Ready = true;
                    MessageReceived?.Invoke(doc.RootElement.Clone());
                }
                catch (Exception ex) { Log.Write("Window message ignored: " + ex.Message); }
            };
            core.Navigate("https://subwake.local/index.html");
        }
        catch (Exception ex)
        {
            // Keep-awake keeps working from the tray even if the window can't load.
            Log.Write("Window could not load (is Microsoft WebView2 installed?): " + ex.Message);
            Controls.Remove(browser);
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(220, 224, 229),
                Text = "The window needs Microsoft WebView2, which couldn't start.\n\nKeep-awake is still running from the tray.",
            });
        }
    }

    public void Send(object message)
    {
        if (IsDisposed || !Ready || browser.CoreWebView2 == null) return;
        try { browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json.Options)); }
        catch (InvalidOperationException) { }
        catch (COMException) { }
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Dark title bar matching the page.
        int dark = 1, caption = 0x00100E0D, text = 0x00E5E0DC, border = 0x0024211F;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
        DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
    }
}
