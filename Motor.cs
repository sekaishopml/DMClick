// lo principal, recibe todo de los mouse antes que windows
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

class Motor : IDisposable
{
    const ushort BitsDeBotones = 0x03FF;
    const ushort BitsDeRueda = 0x0C00;
    const ushort SoltoDerecho = 0x0008;

    // espera al soltar click derecho para que el menu salga en la flecha naranja
    const int EsperaParaMenuMs = 80;

    class EstadoMouse
    {
        public double X, Y;
        public bool YaSeUso;
        public byte Botones;
    }

    readonly IntPtr contexto;
    readonly Pantalla pantalla;
    readonly Configuracion config;
    readonly EstadoMouse[] mouses = new EstadoMouse[Interception.UltimoMouse + 1];
    readonly Rectangle escritorio = SystemInformation.VirtualScreen;
    readonly Stopwatch reloj = Stopwatch.StartNew();
    readonly TrazoMouse[] lote = new TrazoMouse[3];
    readonly double velocidadWindows;
    readonly bool precisionWindows;

    int principal;            // 0 = aun no hay
    int arrastrando;          // mouse 2 arrastrando
    bool cursorPrestado;
    long devolverCursorEn = -1;
    long revisarOtraVezEn = -1;

    Thread hilo;
    volatile bool corriendo;
    volatile bool pidieronElegirPrincipal;
    volatile bool cambiaronLosDispositivos;

    public bool TienePrincipal => principal != 0;

    long Ahora => reloj.ElapsedMilliseconds;

    public Motor(IntPtr contexto, Pantalla pantalla, Configuracion config)
    {
        this.contexto = contexto;
        this.pantalla = pantalla;
        this.config = config;

        for (int i = 0; i < mouses.Length; i++) mouses[i] = new EstadoMouse();

        (velocidadWindows, precisionWindows) = LeerAjustesDelPuntero();
        principal = BuscarPrincipalGuardado();
        pantalla.Principal = principal;

        pantalla.AlElegirPrincipal = () => pidieronElegirPrincipal = true;
        pantalla.AlCambiarDispositivos = () => cambiaronLosDispositivos = true;
        pantalla.AlAbrirConfiguracion = () =>
        {
            config.Guardar();
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + config.Ruta + "\"") { UseShellExecute = true });
        };
    }

    public void Arrancar()
    {
        Interception.CapturarTodosLosMouse(contexto);
        corriendo = true;

        // prioridad alta para que no haya lag
        hilo = new Thread(Bucle) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "mouses" };
        hilo.Start();
    }

    void Bucle()
    {
        var trazo = new TrazoMouse();

        while (corriendo)
        {
            // si no pasa nada se queda dormido
            uint espera = 500;
            if (devolverCursorEn >= 0)
            {
                long falta = devolverCursorEn - Ahora;
                if (falta <= 0) DevolverCursor();
                else espera = (uint)falta;
            }

            RevisarDesconectados();

            int dispositivo = Interception.Esperar(contexto, espera);
            if (dispositivo < Interception.PrimerMouse || dispositivo > Interception.UltimoMouse) continue;
            if (Interception.Recibir(contexto, dispositivo, ref trazo, 1) <= 0) continue;

            if (pidieronElegirPrincipal) EmpezarDeCero();
            if (principal == 0) GuardarPrincipal(dispositivo);

            if (dispositivo == principal)
                LlegoDelPrincipal(dispositivo, ref trazo);
            else
                LlegoDeOtroMouse(dispositivo, ref trazo);
        }
    }

    void LlegoDelPrincipal(int dispositivo, ref TrazoMouse trazo)
    {
        var mouse = mouses[dispositivo];

        if (arrastrando != 0)
        {
            // el otro esta arrastrando, el principal va como flecha blanca
            if (SeMovio(trazo))
            {
                Mover(mouse, trazo.X, trazo.Y, 1.0);
                pantalla.Mostrar(dispositivo, mouse.X, mouse.Y);
            }
            return;
        }

        if (cursorPrestado) DevolverCursor();

        mouse.Botones = ActualizarBotones(mouse.Botones, trazo.Estado);
        Interception.Enviar(contexto, dispositivo, ref trazo, 1);
    }

    void LlegoDeOtroMouse(int dispositivo, ref TrazoMouse trazo)
    {
        var mouse = mouses[dispositivo];

        if (!mouse.YaSeUso)
        {
            Nativo.GetCursorPos(out var cursor);
            mouse.X = cursor.X + 60;
            mouse.Y = cursor.Y + 60;
            mouse.YaSeUso = true;
        }

        if (SeMovio(trazo)) Mover(mouse, trazo.X, trazo.Y, config.Velocidad2);
        pantalla.Mostrar(dispositivo, mouse.X, mouse.Y);

        ushort botones = (ushort)(trazo.Estado & BitsDeBotones);
        bool hizoClickORueda = (trazo.Estado & (BitsDeBotones | BitsDeRueda)) != 0;

        if (arrastrando == dispositivo)
        {
            SeguirArrastrando(mouse, trazo, botones, hizoClickORueda);
            return;
        }

        // si solo se movio no se toca el cursor, asi no parpadea
        if (arrastrando != 0 || mouses[principal].Botones != 0 || !hizoClickORueda) return;

        PrestarCursor();

        // ir a la flecha, click y regresar
        int n = 0;
        lote[n++] = IrA(mouse);
        lote[n++] = SoloBotones(trazo);
        mouse.Botones = ActualizarBotones(mouse.Botones, botones);

        if (mouse.Botones != 0)
            arrastrando = dispositivo;   // arrastre
        else if ((botones & SoltoDerecho) != 0)
            devolverCursorEn = Ahora + EsperaParaMenuMs;
        else
            lote[n++] = IrA(mouses[principal]);

        Interception.Enviar(contexto, principal, lote, (uint)n);
        ActualizarFlechaBlanca();
    }

    void SeguirArrastrando(EstadoMouse mouse, TrazoMouse trazo, ushort botones, bool hizoClickORueda)
    {
        int n = 0;
        lote[n++] = IrA(mouse);

        if (hizoClickORueda)
        {
            lote[n++] = SoloBotones(trazo);
            mouse.Botones = ActualizarBotones(mouse.Botones, botones);
        }

        if (mouse.Botones == 0)
        {
            arrastrando = 0;
            if ((botones & SoltoDerecho) != 0)
                devolverCursorEn = Ahora + EsperaParaMenuMs;
            else
                lote[n++] = IrA(mouses[principal]);
        }

        Interception.Enviar(contexto, principal, lote, (uint)n);
        ActualizarFlechaBlanca();
    }

    // guarda donde estaba el principal
    void PrestarCursor()
    {
        if (!cursorPrestado)
        {
            Nativo.GetCursorPos(out var cursor);
            mouses[principal].X = cursor.X;
            mouses[principal].Y = cursor.Y;
            cursorPrestado = true;
        }
        devolverCursorEn = -1;
    }

    void DevolverCursor()
    {
        devolverCursorEn = -1;
        if (!cursorPrestado) return;

        var volver = IrA(mouses[principal]);
        Interception.Enviar(contexto, principal, ref volver, 1);
        cursorPrestado = false;
        pantalla.Ocultar(principal);
    }

    void ActualizarFlechaBlanca()
    {
        bool yaRegreso = arrastrando == 0 && devolverCursorEn < 0;
        if (yaRegreso)
        {
            cursorPrestado = false;
            pantalla.Ocultar(principal);
        }
        else
        {
            pantalla.Mostrar(principal, mouses[principal].X, mouses[principal].Y);
        }
    }

    // posicion absoluta 0 a 65535
    TrazoMouse IrA(EstadoMouse mouse)
    {
        return new TrazoMouse
        {
            Banderas = 0x001 | 0x002,
            X = (int)Math.Round((mouse.X - escritorio.Left) * 65535.0 / Math.Max(1, escritorio.Width - 1)),
            Y = (int)Math.Round((mouse.Y - escritorio.Top) * 65535.0 / Math.Max(1, escritorio.Height - 1)),
        };
    }

    static TrazoMouse SoloBotones(TrazoMouse trazo)
    {
        return new TrazoMouse { Estado = trazo.Estado, Rueda = trazo.Rueda, Info = trazo.Info };
    }

    static bool SeMovio(TrazoMouse trazo)
    {
        return (trazo.Banderas & 0x001) == 0 && (trazo.X != 0 || trazo.Y != 0);
    }

    // misma velocidad que windows x velocidad2
    void Mover(EstadoMouse mouse, int dx, int dy, double extra)
    {
        double factor = velocidadWindows * extra;

        if (precisionWindows)
        {
            // parecido a mejorar precision del puntero
            double rapidez = Math.Sqrt(dx * dx + dy * dy);
            factor *= Math.Clamp(0.45 + rapidez * 0.12, 0.45, 2.6);
        }

        mouse.X = Math.Clamp(mouse.X + dx * factor, escritorio.Left, escritorio.Right - 1);
        mouse.Y = Math.Clamp(mouse.Y + dy * factor, escritorio.Top, escritorio.Bottom - 1);
    }

    // cada boton tiene 2 bits, apreto y solto
    static byte ActualizarBotones(byte apretados, ushort estado)
    {
        for (int boton = 0; boton < 5; boton++)
        {
            bool apreto = (estado & (1 << (boton * 2))) != 0;
            bool solto = (estado & (2 << (boton * 2))) != 0;

            if (apreto) apretados |= (byte)(1 << boton);
            if (solto) apretados &= (byte)~(1 << boton);
        }
        return apretados;
    }

    // revisa al desconectar y otra vez 1.5s despues
    void RevisarDesconectados()
    {
        if (cambiaronLosDispositivos)
        {
            cambiaronLosDispositivos = false;
            QuitarLosDesconectados();
            revisarOtraVezEn = Ahora + 1500;
        }
        else if (revisarOtraVezEn >= 0 && Ahora >= revisarOtraVezEn)
        {
            revisarOtraVezEn = -1;
            QuitarLosDesconectados();
        }
    }

    void QuitarLosDesconectados()
    {
        for (int d = Interception.PrimerMouse; d <= Interception.UltimoMouse; d++)
        {
            var mouse = mouses[d];
            if (!mouse.YaSeUso || d == principal) continue;
            if (Interception.IdHardware(contexto, d) != null) continue;

            if (arrastrando == d)
            {
                arrastrando = 0;
                DevolverCursor();
            }
            mouse.YaSeUso = false;
            mouse.Botones = 0;
            pantalla.Ocultar(d);
        }
    }

    // velocidad del puntero de windows
    static (double, bool) LeerAjustesDelPuntero()
    {
        // barra 1 a 20, la 10 es normal
        double[] multiplicadores = { 1, 1 / 32.0, 1 / 16.0, 1 / 8.0, 2 / 8.0, 3 / 8.0, 4 / 8.0, 5 / 8.0, 6 / 8.0, 7 / 8.0, 1,
                                     1.25, 1.5, 1.75, 2, 2.25, 2.5, 2.75, 3, 3.25, 3.5 };
        int velocidad = 10;
        Nativo.SystemParametersInfo(Nativo.SPI_GETMOUSESPEED, 0, ref velocidad, 0);

        var acelerar = new int[3];
        Nativo.SystemParametersInfo(Nativo.SPI_GETMOUSE, 0, acelerar, 0);

        return (multiplicadores[Math.Clamp(velocidad, 1, 20)], acelerar[2] != 0);
    }

    // busca el principal guardado
    int BuscarPrincipalGuardado()
    {
        if (config.Principal == null) return 0;

        string[] partes = config.Principal.Split('|');
        if (partes.Length != 2 || !int.TryParse(partes[0], out int numero)) return 0;
        string id = partes[1];

        if (numero >= Interception.PrimerMouse && numero <= Interception.UltimoMouse &&
            Interception.IdHardware(contexto, numero) == id)
            return numero;

        for (int d = Interception.PrimerMouse; d <= Interception.UltimoMouse; d++)
            if (Interception.IdHardware(contexto, d) == id) return d;

        return 0;
    }

    void GuardarPrincipal(int dispositivo)
    {
        principal = dispositivo;
        pantalla.Principal = dispositivo;
        config.Principal = dispositivo + "|" + Interception.IdHardware(contexto, dispositivo);
        config.Guardar();
    }

    void EmpezarDeCero()
    {
        pidieronElegirPrincipal = false;
        if (cursorPrestado) DevolverCursor();
        arrastrando = 0;
        foreach (var mouse in mouses)
        {
            mouse.YaSeUso = false;
            mouse.Botones = 0;
        }
        principal = 0;
    }

    public void Dispose()
    {
        corriendo = false;
        hilo?.Join(1000);
        if (cursorPrestado) DevolverCursor();

        // al cerrar todo vuelve a la normalidad
        Interception.DestruirContexto(contexto);
    }
}
