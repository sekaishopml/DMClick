// Ventana invisible que maneja lo visual: las flechas, el icono junto al reloj y Ctrl+Alt+Q.
// El motor corre en otro hilo, asi que no toca las ventanas directamente: deja la posicion
// anotada y nos manda un mensaje. Si llegan muchos movimientos seguidos, solo se dibuja el ultimo.
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

class Pantalla : Form
{
    const int Total = Interception.UltimoMouse + 1;

    // Colores fijos para los mouse secundarios, en orden. El primero siempre es naranja.
    static readonly Color[] Colores =
    {
        Color.FromArgb(255, 140, 0),
        Color.FromArgb(0, 190, 90),
        Color.FromArgb(30, 144, 255),
        Color.FromArgb(220, 50, 140),
    };

    public volatile int Principal;
    public Action AlElegirPrincipal;
    public Action AlAbrirConfiguracion;
    public Action AlCambiarDispositivos;

    readonly IntPtr ventana;
    readonly int[] posX = new int[Total];
    readonly int[] posY = new int[Total];
    readonly bool[] visible = new bool[Total];
    readonly int[] mensajePendiente = new int[Total];
    readonly Puntero[] flechas = new Puntero[Total];
    readonly NotifyIcon icono = new NotifyIcon();
    int coloresUsados;

    public Pantalla()
    {
        ShowInTaskbar = false;
        ventana = Handle;

        icono.Icon = SystemIcons.Application;
        icono.Text = "DM-CLICK (Ctrl+Alt+Q para salir)";
        icono.ContextMenuStrip = new ContextMenuStrip();
        icono.ContextMenuStrip.Items.Add("Elegir mouse principal", null, (s, e) => ElegirPrincipal());
        icono.ContextMenuStrip.Items.Add("Abrir configuracion (velocidad)", null, (s, e) => AlAbrirConfiguracion?.Invoke());
        icono.ContextMenuStrip.Items.Add("Salir", null, (s, e) => Application.Exit());
        icono.Visible = true;

        Nativo.RegisterHotKey(ventana, 1, Nativo.MOD_CONTROL | Nativo.MOD_ALT | Nativo.MOD_NOREPEAT, (uint)Keys.Q);
    }

    // Estas dos se llaman desde el hilo del motor
    public void Mostrar(int dispositivo, double x, double y) => Pedir(dispositivo, x, y, true);
    public void Ocultar(int dispositivo) => Pedir(dispositivo, 0, 0, false);

    void Pedir(int dispositivo, double x, double y, bool mostrar)
    {
        posX[dispositivo] = (int)x;
        posY[dispositivo] = (int)y;
        visible[dispositivo] = mostrar;

        // Solo mandamos mensaje si no hay otro esperando para este mouse
        if (Interlocked.Exchange(ref mensajePendiente[dispositivo], 1) == 0)
            Nativo.PostMessage(ventana, Nativo.WM_MOVER_FLECHA, (IntPtr)dispositivo, IntPtr.Zero);
    }

    public void Notificar(string titulo, string texto)
    {
        icono.ShowBalloonTip(5000, titulo, texto, ToolTipIcon.Info);
    }

    // La ventana principal nunca se muestra
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Nativo.WM_MOVER_FLECHA)
        {
            int dispositivo = (int)m.WParam;
            Interlocked.Exchange(ref mensajePendiente[dispositivo], 0);

            var flecha = FlechaDe(dispositivo);
            if (visible[dispositivo]) flecha.MoverA(posX[dispositivo], posY[dispositivo]);
            else flecha.Esconder();
            return;
        }

        if (m.Msg == Nativo.WM_DEVICECHANGE)
        {
            // Windows avisa a todas las ventanas cuando se conecta o desconecta hardware
            AlCambiarDispositivos?.Invoke();
        }
        else if (m.Msg == Nativo.WM_HOTKEY)
        {
            Application.Exit();
            return;
        }

        base.WndProc(ref m);
    }

    Puntero FlechaDe(int dispositivo)
    {
        if (flechas[dispositivo] != null) return flechas[dispositivo];

        if (dispositivo == Principal)
        {
            // Flecha blanca normal, para cuando el cursor real se lo presta al otro mouse
            flechas[dispositivo] = new Puntero(Color.White, null);
        }
        else
        {
            Color color = Colores[coloresUsados % Colores.Length];
            coloresUsados++;
            string etiqueta = (coloresUsados + 1).ToString();   // el primer secundario es el "2"
            flechas[dispositivo] = new Puntero(color, etiqueta);
        }
        return flechas[dispositivo];
    }

    void ElegirPrincipal()
    {
        AlElegirPrincipal?.Invoke();

        // Borramos las flechas para que los colores se repartan de nuevo
        for (int i = 0; i < Total; i++)
        {
            flechas[i]?.Dispose();
            flechas[i] = null;
        }
        coloresUsados = 0;
        Notificar("Mueve tu mouse PRINCIPAL", "El primero que muevas sera el principal.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Nativo.UnregisterHotKey(ventana, 1);
        icono.Visible = false;
        icono.Dispose();
        foreach (var flecha in flechas) flecha?.Dispose();
        base.OnFormClosed(e);
    }
}
