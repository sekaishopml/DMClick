// El corazon del programa. Corre en su propio hilo y recibe cada movimiento y click
// de cada mouse ANTES que Windows. Decide que dejar pasar y que hacer con el segundo mouse.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

class Motor : IDisposable
{
    // Bits del campo Estado (ver interception.h)
    const ushort BitsDeBotones = 0x03FF;
    const ushort BitsDeRueda = 0x0C00;
    const ushort SoltoDerecho = 0x0008;

    // Al soltar click derecho dejamos el cursor un momento donde esta el segundo mouse,
    // porque algunos programas leen la posicion del cursor para saber donde abrir el menu
    const int EsperaParaMenuMs = 80;

    class EstadoMouse
    {
        public double X, Y;
        public bool YaSeUso;
        public byte Botones;   // un bit por boton apretado
    }

    readonly IntPtr contexto;
    readonly Pantalla pantalla;
    readonly Configuracion config;
    readonly EstadoMouse[] mouses = new EstadoMouse[Interception.UltimoMouse + 1];
    readonly Rectangle escritorio = SystemInformation.VirtualScreen;   // todos los monitores juntos
    readonly Stopwatch reloj = Stopwatch.StartNew();
    readonly TrazoMouse[] lote = new TrazoMouse[3];   // se reutiliza para no crear basura en cada click
    readonly double velocidadWindows;
    readonly bool precisionWindows;

    int principal;            // numero del mouse principal (0 = todavia no lo sabemos)
    int arrastrando;          // segundo mouse con un boton apretado (0 = ninguno)
    bool cursorPrestado;      // el cursor real esta en la flecha de un segundo mouse
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

        // Lo que la pantalla nos pide desde el menu del icono
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

        // Prioridad alta para que el mouse responda aunque haya programas pesados
        hilo = new Thread(Bucle) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "mouses" };
        hilo.Start();
    }

    void Bucle()
    {
        var trazo = new TrazoMouse();

        while (corriendo)
        {
            // Sin eventos el hilo se queda dormido dentro del driver
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
            // El segundo mouse esta arrastrando algo y tiene el cursor.
            // El principal se dibuja como flecha blanca y no lo dejamos pasar a Windows.
            if (SeMovio(trazo))
            {
                Mover(mouse, trazo.X, trazo.Y, 1.0);
                pantalla.Mostrar(dispositivo, mouse.X, mouse.Y);
            }
            return;
        }

        if (cursorPrestado) DevolverCursor();

        mouse.Botones = ActualizarBotones(mouse.Botones, trazo.Estado);
        Interception.Enviar(contexto, dispositivo, ref trazo, 1);   // pasa normal a Windows
    }

    void LlegoDeOtroMouse(int dispositivo, ref TrazoMouse trazo)
    {
        var mouse = mouses[dispositivo];

        if (!mouse.YaSeUso)
        {
            // La primera vez aparece un poco al lado del cursor
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

        // Si otro esta arrastrando, o si solo se movio, el cursor real ni se entera.
        // Esto es lo que evita el parpadeo cuando mueves los dos a la vez.
        if (arrastrando != 0 || mouses[principal].Botones != 0 || !hizoClickORueda) return;

        PrestarCursor();

        // Todo en un solo envio: ir a la flecha, hacer el click y (si toca) regresar
        int n = 0;
        lote[n++] = IrA(mouse);
        lote[n++] = SoloBotones(trazo);
        mouse.Botones = ActualizarBotones(mouse.Botones, botones);

        if (mouse.Botones != 0)
            arrastrando = dispositivo;                         // sigue apretado: puede ser un arrastre
        else if ((botones & SoltoDerecho) != 0)
            devolverCursorEn = Ahora + EsperaParaMenuMs;        // dejamos que se abra el menu
        else
            lote[n++] = IrA(mouses[principal]);                // rueda: regresamos de una vez

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
            // Solto todos los botones: termina el arrastre
            arrastrando = 0;
            if ((botones & SoltoDerecho) != 0)
                devolverCursorEn = Ahora + EsperaParaMenuMs;
            else
                lote[n++] = IrA(mouses[principal]);
        }

        Interception.Enviar(contexto, principal, lote, (uint)n);
        ActualizarFlechaBlanca();
    }

    // Antes de usar el cursor para el segundo mouse, anotamos donde estaba el principal
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

    // Mientras el cursor esta prestado, dibujamos una flecha blanca donde quedo el principal
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

    // Trazo que pone el cursor justo en la posicion de ese mouse.
    // El driver usa coordenadas de 0 a 65535 sobre todo el escritorio.
    TrazoMouse IrA(EstadoMouse mouse)
    {
        return new TrazoMouse
        {
            Banderas = 0x001 | 0x002,   // posicion absoluta sobre el escritorio virtual
            X = (int)Math.Round((mouse.X - escritorio.Left) * 65535.0 / Math.Max(1, escritorio.Width - 1)),
            Y = (int)Math.Round((mouse.Y - escritorio.Top) * 65535.0 / Math.Max(1, escritorio.Height - 1)),
        };
    }

    // El mismo trazo pero sin movimiento, solo botones y rueda
    static TrazoMouse SoloBotones(TrazoMouse trazo)
    {
        return new TrazoMouse { Estado = trazo.Estado, Rueda = trazo.Rueda, Info = trazo.Info };
    }

    static bool SeMovio(TrazoMouse trazo)
    {
        return (trazo.Banderas & 0x001) == 0 && (trazo.X != 0 || trazo.Y != 0);   // bit 0 apagado = relativo
    }

    // Movemos la flecha con la misma velocidad que tiene configurada Windows,
    // asi el segundo mouse se siente parecido al principal
    void Mover(EstadoMouse mouse, int dx, int dy, double extra)
    {
        double factor = velocidadWindows * extra;

        if (precisionWindows)
        {
            // Aproximacion de "Mejorar precision del puntero": lento = mas preciso, rapido = llega mas lejos
            double rapidez = Math.Sqrt(dx * dx + dy * dy);
            factor *= Math.Clamp(0.45 + rapidez * 0.12, 0.45, 2.6);
        }

        mouse.X = Math.Clamp(mouse.X + dx * factor, escritorio.Left, escritorio.Right - 1);
        mouse.Y = Math.Clamp(mouse.Y + dy * factor, escritorio.Top, escritorio.Bottom - 1);
    }

    // En Estado cada boton tiene dos bits: uno para "apreto" y el siguiente para "solto".
    // Boton 1 = bits 0 y 1, boton 2 = bits 2 y 3, etc.
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

    // Windows nos avisa cuando se conecta o desconecta algo (no revisamos a cada rato).
    // Revisamos al momento y otra vez un poco despues por si el driver tarda en soltarlo.
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
            if (Interception.IdHardware(contexto, d) != null) continue;   // sigue conectado

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

    // Lo que el usuario tiene puesto en Configuracion > Mouse > Velocidad del puntero
    static (double, bool) LeerAjustesDelPuntero()
    {
        // Tabla de Windows: posicion de la barra (1 a 20) -> multiplicador. La posicion 10 es 1x.
        double[] multiplicadores = { 1, 1 / 32.0, 1 / 16.0, 1 / 8.0, 2 / 8.0, 3 / 8.0, 4 / 8.0, 5 / 8.0, 6 / 8.0, 7 / 8.0, 1,
                                     1.25, 1.5, 1.75, 2, 2.25, 2.5, 2.75, 3, 3.25, 3.5 };
        int velocidad = 10;
        Nativo.SystemParametersInfo(Nativo.SPI_GETMOUSESPEED, 0, ref velocidad, 0);

        var acelerar = new int[3];
        Nativo.SystemParametersInfo(Nativo.SPI_GETMOUSE, 0, acelerar, 0);

        return (multiplicadores[Math.Clamp(velocidad, 1, 20)], acelerar[2] != 0);
    }

    // Buscamos el mouse principal guardado. Primero por numero + id, si no solo por id.
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

        return 0;   // no esta conectado: el primero que se mueva sera el principal
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

        // Al cerrar el contexto el driver deja pasar todo normal otra vez
        Interception.DestruirContexto(contexto);
    }
}
