// Funciones del driver Interception que usamos.
// Documentacion original: https://github.com/oblitum/Interception (archivo interception.h)
using System;
using System.Runtime.InteropServices;
using System.Text;

// Lo que nos manda el driver cada vez que un mouse se mueve o hace click
[StructLayout(LayoutKind.Sequential)]
struct TrazoMouse
{
    public ushort Estado;     // que botones se apretaron o soltaron, y si se movio la rueda
    public ushort Banderas;   // 0 = movimiento relativo, 1 = posicion absoluta
    public short Rueda;       // cuanto giro la rueda
    public int X;
    public int Y;
    public uint Info;
}

static class Interception
{
    const string Dll = "interception.dll";

    // El driver numera los teclados del 1 al 10 y los mouse del 11 al 20
    public const int PrimerMouse = 11;
    public const int UltimoMouse = 20;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int Filtro(int dispositivo);

    // Hay que guardar el filtro en un campo para que el recolector de basura no lo borre
    static readonly Filtro soloMouses = d => d >= PrimerMouse && d <= UltimoMouse ? 1 : 0;

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_create_context")]
    public static extern IntPtr CrearContexto();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_destroy_context")]
    public static extern void DestruirContexto(IntPtr contexto);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_set_filter")]
    static extern void PonerFiltro(IntPtr contexto, Filtro filtro, ushort queCosas);

    // Devuelve el numero del dispositivo que mando algo, o 0 si paso el tiempo sin nada
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_wait_with_timeout")]
    public static extern int Esperar(IntPtr contexto, uint milisegundos);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_receive")]
    public static extern int Recibir(IntPtr contexto, int dispositivo, ref TrazoMouse trazo, uint cantidad);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_send")]
    public static extern int Enviar(IntPtr contexto, int dispositivo, ref TrazoMouse trazo, uint cantidad);

    // Igual que el anterior pero varios trazos seguidos; Windows los procesa en ese orden exacto
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_send")]
    public static extern int Enviar(IntPtr contexto, int dispositivo, TrazoMouse[] trazos, uint cantidad);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_get_hardware_id")]
    static extern uint LeerIdHardware(IntPtr contexto, int dispositivo, byte[] buffer, uint tamano);

    // Desde aqui todos los eventos de todos los mouse pasan por nuestro programa
    public static void CapturarTodosLosMouse(IntPtr contexto)
    {
        PonerFiltro(contexto, soloMouses, 0xFFFF);
    }

    // Id del mouse (ej. "HID\VID_046D&PID_C077"). Si esta desconectado devuelve null.
    public static string IdHardware(IntPtr contexto, int dispositivo)
    {
        var buffer = new byte[1024];
        uint bytes = LeerIdHardware(contexto, dispositivo, buffer, (uint)buffer.Length);
        if (bytes == 0) return null;

        string texto = Encoding.Unicode.GetString(buffer, 0, (int)bytes);
        int fin = texto.IndexOf('\0');
        return fin >= 0 ? texto.Substring(0, fin) : texto;
    }
}
