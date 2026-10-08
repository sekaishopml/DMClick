using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[StructLayout(LayoutKind.Sequential)]
struct MouseStroke
{
    public ushort state;
    public ushort flags;
    public short rolling;
    public int x;
    public int y;
    public uint information;
}

// interception.dll cargada a mano con punteros a funcion: sin marshaling en el camino caliente.
static unsafe class Ic
{
    public static delegate* unmanaged[Cdecl]<IntPtr> create;
    public static delegate* unmanaged[Cdecl]<IntPtr, void> destroy;
    public static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ushort, void> setFilter;
    public static delegate* unmanaged[Cdecl]<IntPtr, uint, int> waitTimeout;
    public static delegate* unmanaged[Cdecl]<IntPtr, int, MouseStroke*, uint, int> receive;
    public static delegate* unmanaged[Cdecl]<IntPtr, int, MouseStroke*, uint, int> send;
    public static delegate* unmanaged[Cdecl]<IntPtr, int, char*, uint, uint> hardwareId;
    public static IntPtr isMouse;

    public static bool Load(string path)
    {
        if (!NativeLibrary.TryLoad(path, out IntPtr h)) return false;
        create = (delegate* unmanaged[Cdecl]<IntPtr>)NativeLibrary.GetExport(h, "interception_create_context");
        destroy = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(h, "interception_destroy_context");
        setFilter = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ushort, void>)NativeLibrary.GetExport(h, "interception_set_filter");
        waitTimeout = (delegate* unmanaged[Cdecl]<IntPtr, uint, int>)NativeLibrary.GetExport(h, "interception_wait_with_timeout");
        receive = (delegate* unmanaged[Cdecl]<IntPtr, int, MouseStroke*, uint, int>)NativeLibrary.GetExport(h, "interception_receive");
        send = (delegate* unmanaged[Cdecl]<IntPtr, int, MouseStroke*, uint, int>)NativeLibrary.GetExport(h, "interception_send");
        hardwareId = (delegate* unmanaged[Cdecl]<IntPtr, int, char*, uint, uint>)NativeLibrary.GetExport(h, "interception_get_hardware_id");
        isMouse = NativeLibrary.GetExport(h, "interception_is_mouse");
        return true;
    }
}

// Hilo dedicado de alta prioridad: cada evento de cada mouse pasa por aqui antes que por Windows.
sealed unsafe class Engine : IDisposable
{
    const int N = 21;                       // dispositivos 1..10 teclados, 11..20 mouse
    const ushort ButtonBits = 0x03FF;
    const ushort WheelBits = 0x0C00;
    const ushort RightUp = 0x0008;
    const int ContextMenuDelayMs = 80;      // tras soltar click derecho, tiempo para que la app lea la posicion

    readonly IntPtr ctx;
    readonly Hub hub;
    readonly Config cfg;
    readonly double[] x = new double[N], y = new double[N];
    readonly bool[] seen = new bool[N];
    readonly byte[] held = new byte[N];     // bitmask de botones presionados
    readonly Rectangle vs = SystemInformation.VirtualScreen;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly double speed;                  // velocidad del puntero de Windows (Panel de control)
    readonly bool epp;                      // "Mejorar precision del puntero"

    int primary;        // dispositivo del mouse principal
    int owner;          // secundario que tiene un boton presionado (posee el cursor real mientras tanto)
    bool parked;        // el cursor real no esta donde va el principal
    long restoreAt = -1;
    Thread thread;
    volatile bool running;
    volatile bool resetRequested;
    volatile bool devicesChanged;   // lo marca la UI cuando Windows avisa WM_DEVICECHANGE
    long recheckAt = -1;

    public bool HasPrimary => primary != 0;

    public Engine(IntPtr context, Hub hub, Config cfg)
    {
        ctx = context;
        this.hub = hub;
        this.cfg = cfg;
        (speed, epp) = ReadPointerSettings();
        primary = FindPrimary();
        hub.Primary = primary;
        hub.RequestPrimaryReset = () => resetRequested = true;
        hub.DevicesChanged = () => devicesChanged = true;
        hub.OpenConfig = () =>
        {
            cfg.Save();
            System.Diagnostics.Process.Start(new ProcessStartInfo("notepad.exe", $"\"{cfg.Path}\"") { UseShellExecute = true });
        };
    }

    public void Start()
    {
        Ic.setFilter(ctx, Ic.isMouse, 0xFFFF);
        running = true;
        thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "interception" };
        thread.Start();
    }

    void Run()
    {
        MouseStroke s;
        while (running)
        {
            // Sin eventos el hilo duerme en el driver; solo despierta 2 veces por segundo para
            // ver si debe cerrarse o si Windows aviso de un cambio de dispositivos.
            uint wait = 500;
            if (restoreAt >= 0)
            {
                long left = restoreAt - clock.ElapsedMilliseconds;
                if (left <= 0) Restore(); else wait = (uint)left;
            }

            if (devicesChanged)
            {
                devicesChanged = false;
                DropDisconnected();
                recheckAt = clock.ElapsedMilliseconds + 1500;   // por si el driver tarda en soltar el dispositivo
            }
            else if (recheckAt >= 0 && clock.ElapsedMilliseconds >= recheckAt)
            {
                recheckAt = -1;
                DropDisconnected();
            }

            int dev = Ic.waitTimeout(ctx, wait);
            if (dev <= 0 || dev >= N) continue;
            if (Ic.receive(ctx, dev, &s, 1) <= 0) continue;

            if (resetRequested)
            {
                resetRequested = false;
                if (parked) Restore();
                owner = 0;
                Array.Clear(seen);
                Array.Clear(held);
                primary = 0;
            }
            if (primary == 0) SetPrimary(dev);
            if (dev == primary) OnPrimary(dev, &s);
            else OnSecondary(dev, &s);
        }
    }

    // Un mouse desconectado deja de tener hardware id: se oculta su puntero y se olvida su estado.
    // Si se vuelve a conectar, su puntero reaparece al moverlo.
    void DropDisconnected()
    {
        for (int d = 11; d < N; d++)
        {
            if (!seen[d] || d == primary || HardwareId(d) != null) continue;

            if (owner == d)
            {
                owner = 0;
                Restore();
            }
            seen[d] = false;
            held[d] = 0;
            hub.Set(d, 0, 0, false);
        }
    }

    void OnPrimary(int dev, MouseStroke* s)
    {
        bool rel = (s->flags & 0x001) == 0;

        if (owner != 0)
        {
            // Un secundario esta arrastrando: el principal se ve como fantasma y no toca el cursor
            if (rel && (s->x | s->y) != 0)
            {
                Accelerate(dev, s->x, s->y);
                hub.Set(dev, x[dev], y[dev], true);
            }
            return;
        }

        if (parked) Restore();
        held[dev] = Held(held[dev], s->state);
        Ic.send(ctx, dev, s, 1);
    }

    void OnSecondary(int dev, MouseStroke* s)
    {
        if (!seen[dev])
        {
            Native.GetCursorPos(out var c);
            x[dev] = c.x + 60;
            y[dev] = c.y + 60;
            seen[dev] = true;
        }

        bool moved = (s->flags & 0x001) == 0 && (s->x | s->y) != 0;
        if (moved) Accelerate(dev, s->x, s->y, cfg.Speed2);

        ushort btn = (ushort)(s->state & ButtonBits);
        ushort wheel = (ushort)(s->state & WheelBits);
        hub.Set(dev, x[dev], y[dev], true);

        MouseStroke* batch = stackalloc MouseStroke[3];
        int n = 0;

        if (owner == dev)
        {
            // Este mouse tiene un boton abajo (arrastrar / mantener): mueve el cursor real
            batch[n++] = Abs(dev);
            if ((btn | wheel) != 0)
            {
                batch[n++] = Clean(s);
                held[dev] = Held(held[dev], btn);
            }
            if (held[dev] == 0)
            {
                owner = 0;
                if ((btn & RightUp) != 0) restoreAt = clock.ElapsedMilliseconds + ContextMenuDelayMs;
                else batch[n++] = Abs(primary);
            }
            Ic.send(ctx, primary, batch, (uint)n);
            AfterSend();
            return;
        }

        // Otro mouse esta arrastrando: este solo mueve su puntero
        if (owner != 0 || held[primary] != 0) return;

        // Solo movimiento: el cursor real ni se entera -> cero parpadeo
        if ((btn | wheel) == 0) return;

        // Click o rueda: lote atomico [ir a mi posicion, boton, volver]
        if (!parked)
        {
            Native.GetCursorPos(out var c);
            x[primary] = c.x;
            y[primary] = c.y;
            parked = true;
        }
        restoreAt = -1;

        batch[n++] = Abs(dev);
        batch[n++] = Clean(s);
        held[dev] = Held(held[dev], btn);

        if (held[dev] != 0) owner = dev;                                                     // empieza click/arrastre
        else if ((btn & RightUp) != 0) restoreAt = clock.ElapsedMilliseconds + ContextMenuDelayMs;
        else batch[n++] = Abs(primary);                                                     // rueda u otro: vuelve ya

        Ic.send(ctx, primary, batch, (uint)n);
        AfterSend();
    }

    // Tras un lote: si el ultimo stroke devolvio el cursor, ya no estamos estacionados
    void AfterSend()
    {
        if (owner == 0 && restoreAt < 0 && parked)
        {
            parked = false;
            hub.Set(primary, 0, 0, false);
        }
        else if (parked)
        {
            hub.Set(primary, x[primary], y[primary], true);   // fantasma blanco donde va el principal
        }
    }

    void Restore()
    {
        restoreAt = -1;
        if (!parked) return;
        MouseStroke m = Abs(primary);
        Ic.send(ctx, primary, &m, 1);
        parked = false;
        hub.Set(primary, 0, 0, false);
    }

    MouseStroke Abs(int dev) => new MouseStroke
    {
        flags = 0x001 | 0x002,   // MOVE_ABSOLUTE | VIRTUAL_DESKTOP
        x = (int)Math.Round((x[dev] - vs.Left) * 65535.0 / Math.Max(1, vs.Width - 1)),
        y = (int)Math.Round((y[dev] - vs.Top) * 65535.0 / Math.Max(1, vs.Height - 1)),
    };

    static MouseStroke Clean(MouseStroke* s) => new MouseStroke
    {
        state = s->state, rolling = s->rolling, information = s->information
    };

    // Emula la velocidad del puntero de Windows para que el secundario se sienta igual
    void Accelerate(int dev, int dx, int dy, double extra = 1.0)
    {
        double g = speed * extra;
        if (epp)
        {
            double v = Math.Sqrt((double)dx * dx + (double)dy * dy);
            g *= Math.Clamp(0.45 + v * 0.12, 0.45, 2.6);
        }
        x[dev] = Math.Clamp(x[dev] + dx * g, vs.Left, vs.Right - 1);
        y[dev] = Math.Clamp(y[dev] + dy * g, vs.Top, vs.Bottom - 1);
    }

    static byte Held(byte h, ushort state)
    {
        if (state == 0) return h;
        for (int i = 0; i < 5; i++)
        {
            if ((state & (1 << (2 * i))) != 0) h |= (byte)(1 << i);
            if ((state & (2 << (2 * i))) != 0) h &= (byte)~(1 << i);
        }
        return h;
    }

    static (double, bool) ReadPointerSettings()
    {
        double[] table = { 1, 1 / 32.0, 1 / 16.0, 1 / 8.0, 2 / 8.0, 3 / 8.0, 4 / 8.0, 5 / 8.0, 6 / 8.0, 7 / 8.0, 1,
                           1.25, 1.5, 1.75, 2, 2.25, 2.5, 2.75, 3, 3.25, 3.5 };
        int sp = 10;
        Native.SystemParametersInfo(Native.SPI_GETMOUSESPEED, 0, ref sp, 0);
        var m = new int[3];
        Native.SystemParametersInfo(Native.SPI_GETMOUSE, 0, m, 0);
        return (table[Math.Clamp(sp, 1, 20)], m[2] != 0);
    }

    string HardwareId(int dev)
    {
        char* b = stackalloc char[512];
        uint n = Ic.hardwareId(ctx, dev, b, 1024);
        if (n == 0) return null;
        return new string(b, 0, (int)Math.Min(n / 2, 511)).Split('\0')[0];
    }

    int FindPrimary()
    {
        if (cfg.Primary == null) return 0;
        var parts = cfg.Primary.Split('|');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int num)) return 0;
        if (num > 10 && num < N && HardwareId(num) == parts[1]) return num;
        for (int d = 11; d < N; d++)
            if (HardwareId(d) == parts[1]) return d;
        return 0;   // no esta conectado: el primero que se mueva sera el principal en esta sesion
    }

    void SetPrimary(int dev)
    {
        primary = dev;
        hub.Primary = dev;
        cfg.Primary = $"{dev}|{HardwareId(dev)}";
        cfg.Save();
    }

    public void Dispose()
    {
        running = false;
        thread?.Join(500);
        if (parked) Restore();
        Ic.destroy(ctx);   // al destruir el contexto el driver vuelve a dejar pasar todo
    }
}
