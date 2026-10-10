// funciones de windows (user32 y gdi32)
using System;
using System.Drawing;
using System.Runtime.InteropServices;

static class Nativo
{
    public const int WM_DEVICECHANGE = 0x0219;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_MOVER_FLECHA = 0x8001;
    public const int WM_PULSAR_FLECHA = 0x8002;
    public const int WM_SALIR = 0x8003;

    // Ctrl+Alt+Q
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint SPI_GETMOUSE = 0x0003;
    public const uint SPI_GETMOUSESPEED = 0x0070;

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public const int SW_HIDE = 0;

    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct Punto
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Tamano
    {
        public int Ancho;
        public int Alto;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct Mezcla
    {
        public byte Operacion;
        public byte Banderas;
        public byte Opacidad;
        public byte FormatoAlfa;
    }

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Punto punto);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr ventana, int mensaje, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr ventana, IntPtr despuesDe, int x, int y, int ancho, int alto, uint banderas);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr ventana, int como);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr ventana, int id, uint modificadores, uint tecla);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr ventana, int id);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint accion, uint parametro, ref int valor, uint guardar);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint accion, uint parametro, int[] valores, uint guardar);

    // para esconder el cursor de windows mientras lo usa el mouse 2
    [DllImport("Magnification.dll")] public static extern bool MagInitialize();
    [DllImport("Magnification.dll")] public static extern bool MagUninitialize();
    [DllImport("Magnification.dll")] public static extern bool MagShowSystemCursor(bool mostrar);

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr ventana);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr ventana, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr objeto);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr objeto);

    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr ventana, IntPtr dcDestino, IntPtr posicion, ref Tamano tamano,
        IntPtr dcOrigen, ref Punto origen, int colorClave, ref Mezcla mezcla, int banderas);

    // pone la imagen con transparencia para que no se vea pixelada
    public static void PonerImagenConTransparencia(IntPtr ventana, Bitmap imagen)
    {
        IntPtr pantalla = GetDC(IntPtr.Zero);
        IntPtr memoria = CreateCompatibleDC(pantalla);
        IntPtr bitmap = imagen.GetHbitmap(Color.FromArgb(0));
        IntPtr anterior = SelectObject(memoria, bitmap);

        try
        {
            var tamano = new Tamano { Ancho = imagen.Width, Alto = imagen.Height };
            var origen = new Punto();
            var mezcla = new Mezcla { Opacidad = 255, FormatoAlfa = 1 };
            UpdateLayeredWindow(ventana, pantalla, IntPtr.Zero, ref tamano, memoria, ref origen, 0, ref mezcla, 2);
        }
        finally
        {
            SelectObject(memoria, anterior);
            DeleteObject(bitmap);
            DeleteDC(memoria);
            ReleaseDC(IntPtr.Zero, pantalla);
        }
    }
}
