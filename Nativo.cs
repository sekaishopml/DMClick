// Funciones de Windows que C# no trae directamente (user32.dll y gdi32.dll).
// Los nombres en mayusculas son los mismos de la documentacion de Microsoft para que sea facil buscarlos.
using System;
using System.Drawing;
using System.Runtime.InteropServices;

static class Nativo
{
    // Mensajes
    public const int WM_DEVICECHANGE = 0x0219;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_MOVER_FLECHA = 0x8001;   // mensaje propio (WM_APP + 1)

    // Teclas para el atajo Ctrl+Alt+Q
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;

    // Ajustes del mouse de Windows
    public const uint SPI_GETMOUSE = 0x0003;
    public const uint SPI_GETMOUSESPEED = 0x0070;

    // Para mover las flechas
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOSENDCHANGING = 0x0400;
    public const int SW_HIDE = 0;

    // Estilos de ventana de las flechas
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

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr ventana);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr ventana, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr objeto);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr objeto);

    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr ventana, IntPtr dcDestino, IntPtr posicion, ref Tamano tamano,
        IntPtr dcOrigen, ref Punto origen, int colorClave, ref Mezcla mezcla, int banderas);

    // Pone una imagen PNG-style (con transparencia) como contenido de la ventana.
    // Asi la flecha tiene bordes suaves en vez de pixelados.
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
            var mezcla = new Mezcla { Opacidad = 255, FormatoAlfa = 1 };   // usar el alfa de cada pixel
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
