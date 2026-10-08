using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;

// Ventana oculta del hilo UI: dibuja los punteros, atajo de salida e icono de bandeja.
// El hilo del driver solo escribe la posicion deseada y avisa con UN PostMessage por cuadro
// (coalescido): nunca se bloquea esperando a la UI.
sealed class Hub : Form
{
    const int N = 21;
    // Color fijo por orden de los secundarios: el primero siempre naranja
    static readonly Color[] Palette = { Color.FromArgb(255, 140, 0), Color.FromArgb(0, 190, 90), Color.FromArgb(30, 144, 255), Color.FromArgb(220, 50, 140) };

    public volatile int Primary;
    readonly IntPtr hwnd;
    readonly int[] px = new int[N], py = new int[N], pending = new int[N];
    readonly bool[] visible = new bool[N];
    readonly Pointer[] pointers = new Pointer[N];
    readonly NotifyIcon tray = new NotifyIcon();
    int nextColor;

    public Hub()
    {
        ShowInTaskbar = false;
        hwnd = Handle;

        tray.Icon = SystemIcons.Application;
        tray.Text = "DM-CLICK (Ctrl+Alt+Q para salir)";
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("Elegir mouse principal", null, (s, e) => ResetPrimary());
        tray.ContextMenuStrip.Items.Add("Abrir configuracion (velocidad)", null, (s, e) => OpenConfig?.Invoke());
        tray.ContextMenuStrip.Items.Add("Salir", null, (s, e) => Application.Exit());
        tray.Visible = true;

        Native.RegisterHotKey(hwnd, 1, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.Q);
    }

    public Action RequestPrimaryReset;
    public Action OpenConfig;
    public Action DevicesChanged;

    public void Notify(string title, string text) => tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    void ResetPrimary()
    {
        RequestPrimaryReset?.Invoke();
        // Los colores se reasignan: el nuevo principal no debe conservar una flecha de color
        for (int i = 0; i < N; i++)
        {
            pointers[i]?.Dispose();
            pointers[i] = null;
        }
        nextColor = 0;
        Notify("Mueve tu mouse PRINCIPAL", "El primero que muevas sera el principal.");
    }

    // Llamado desde el hilo del driver
    public void Set(int dev, double x, double y, bool show)
    {
        px[dev] = (int)x;
        py[dev] = (int)y;
        visible[dev] = show;
        if (Interlocked.Exchange(ref pending[dev], 1) == 0)
            Native.PostMessage(hwnd, Native.WM_APP_POINTER, (IntPtr)dev, IntPtr.Zero);
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_APP_POINTER)
        {
            int dev = (int)m.WParam;
            Interlocked.Exchange(ref pending[dev], 0);
            var p = Get(dev);
            if (visible[dev]) p.MoveTo(px[dev], py[dev]);
            else p.Conceal();
            return;
        }
        if (m.Msg == Native.WM_DEVICECHANGE)
        {
            // Windows avisa a todas las ventanas cuando se conecta/desconecta hardware: sin sondeo
            DevicesChanged?.Invoke();
        }
        else if (m.Msg == Native.WM_HOTKEY)
        {
            Application.Exit();
            return;
        }
        base.WndProc(ref m);
    }

    Pointer Get(int dev)
    {
        return pointers[dev] ??= dev == Primary
            ? new Pointer(Color.White, null)                                    // fantasma del principal = flecha normal
            : new Pointer(Palette[nextColor % Palette.Length], (++nextColor + 1).ToString());
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Native.UnregisterHotKey(hwnd, 1);
        tray.Visible = false;
        tray.Dispose();
        foreach (var p in pointers) p?.Dispose();
        base.OnFormClosed(e);
    }
}

// Puntero: ventana layered con alfa por pixel (bordes suaves), encima de todo, sin foco,
// transparente a clicks. La imagen se sube una sola vez; moverla es solo SetWindowPos.
sealed class Pointer : Form
{
    readonly Bitmap image;
    readonly int hot;   // desplazamiento de la punta de la flecha dentro de la imagen

    public Pointer(Color color, string label)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;

        float k = DeviceDpi / 96f;
        hot = (int)Math.Ceiling(1.5f * k);
        image = Render(color, label, k, hot);
        Size = image.Size;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyLayeredBitmap(Handle, image);
    }

    public void MoveTo(int x, int y) =>
        // HWND_TOPMOST en cada movimiento: queda encima incluso de menus contextuales recien abiertos
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x - hot, y - hot, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOSENDCHANGING);

    public void Conceal() => Native.ShowWindow(Handle, Native.SW_HIDE);

    static Bitmap Render(Color color, string label, float k, int hot)
    {
        // Silueta de la flecha estandar de Windows (tamaño 100%)
        PointF[] arrow =
        {
            new PointF(0, 0), new PointF(0, 17), new PointF(4.2f, 13), new PointF(7, 19.5f),
            new PointF(9.6f, 18.4f), new PointF(6.9f, 12.2f), new PointF(12.2f, 12.2f)
        };
        for (int i = 0; i < arrow.Length; i++) arrow[i] = new PointF(hot + arrow[i].X * k, hot + arrow[i].Y * k);

        int w = (int)((label != null ? 34 : 16) * k) + hot;
        int h = (int)((label != null ? 32 : 23) * k) + hot;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        using (var fill = new SolidBrush(color))
        using (var outline = new Pen(color == Color.White ? Color.Black : Color.White, 1.2f * k) { LineJoin = LineJoin.Round })
        {
            g.FillPolygon(fill, arrow);
            g.DrawPolygon(outline, arrow);
        }

        if (label != null)
        {
            // Etiqueta tipo AnyDesk junto a la flecha
            var tag = new RectangleF(hot + 12 * k, hot + 18 * k, 20 * k, 13 * k);
            using var path = new GraphicsPath();
            float r = 4 * k;
            path.AddArc(tag.X, tag.Y, r, r, 180, 90);
            path.AddArc(tag.Right - r, tag.Y, r, r, 270, 90);
            path.AddArc(tag.Right - r, tag.Bottom - r, r, r, 0, 90);
            path.AddArc(tag.X, tag.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(color);
            g.FillPath(fill, path);
            using var font = new Font("Segoe UI", 7.5f * k, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(label, font, Brushes.White, tag, fmt);
        }
        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) image.Dispose();
        base.Dispose(disposing);
    }
}
