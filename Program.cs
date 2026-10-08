// DMClick - 2 mouse 2 punteros
// el principal es el cursor normal, el otro es la flecha naranja
// cuando el otro hace click se lleva el cursor ahi, click y regresa
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // si ya esta abierto no abre otro
        using var unaSolaVez = new Mutex(true, @"Local\DM-CLICK", out bool somosElPrimero);
        if (!somosElPrimero) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();

        var config = Configuracion.Cargar();
        if (Array.IndexOf(args, "--reset") >= 0)
        {
            config.Principal = null;
            config.Guardar();
        }

        IntPtr contexto;
        try
        {
            contexto = Interception.CrearContexto();
        }
        catch (DllNotFoundException)
        {
            Avisar("Falta interception.dll junto a DM-CLICK.exe. Vuelve a ejecutar INSTALAR.cmd.");
            return;
        }

        if (contexto == IntPtr.Zero)
        {
            Avisar("El driver Interception no esta activo.\n\nEjecuta INSTALAR.cmd y reinicia Windows.");
            return;
        }

        var pantalla = new Pantalla();
        using var motor = new Motor(contexto, pantalla, config);
        motor.Arrancar();

        if (!motor.TienePrincipal)
            pantalla.Notificar("Mueve primero tu mouse PRINCIPAL",
                "Se queda guardado. Si te equivocas: icono del reloj > Elegir mouse principal.");

        Application.Run(pantalla);
    }

    static void Avisar(string mensaje)
    {
        MessageBox.Show(mensaje, "DM-CLICK", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

// config.ini en %APPDATA%\DM-CLICK
class Configuracion
{
    public string Ruta;
    public string Principal;   // numero|id del mouse principal
    public double Velocidad2 = 1.0;   // velocidad del mouse 2

    public static Configuracion Cargar()
    {
        string carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DM-CLICK");
        Directory.CreateDirectory(carpeta);

        var config = new Configuracion { Ruta = Path.Combine(carpeta, "config.ini") };
        if (!File.Exists(config.Ruta)) return config;

        foreach (string linea in File.ReadAllLines(config.Ruta))
        {
            string[] partes = linea.Split('=', 2);
            if (partes.Length != 2) continue;

            string clave = partes[0].Trim();
            string valor = partes[1].Trim();

            // primary y speed2 = version vieja
            if (clave == "principal" || clave == "primary")
                config.Principal = valor == "" ? null : valor;

            if ((clave == "velocidad2" || clave == "speed2") &&
                double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0)
                config.Velocidad2 = v;
        }
        return config;
    }

    public void Guardar()
    {
        string texto = "principal=" + Principal + "\r\n" +
                       "velocidad2=" + Velocidad2.ToString(CultureInfo.InvariantCulture) + "\r\n";
        try
        {
            File.WriteAllText(Ruta, texto);
        }
        catch (Exception)
        {
        }
    }
}
