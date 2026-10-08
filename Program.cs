// DM-CLICK: dos (o mas) mouse, cada uno con su puntero, estilo AnyDesk.
//
// - Mouse principal = el cursor normal de Windows. Sus eventos pasan intactos.
// - Mouse secundario = puntero de color fijo dibujado encima de todo. Gracias al driver
//   Interception sus eventos NUNCA llegan a Windows como movimiento, asi que el cursor real no
//   se mueve ni parpadea aunque muevas los dos a la vez.
// - Clicks/rueda del secundario: se inyectan en su posicion y el cursor real vuelve a su sitio en
//   el mismo lote de eventos. Click derecho -> el menu contextual se abre donde esta el secundario
//   (y el menu anterior se cierra, nunca se duplica).
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

static unsafe class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var single = new Mutex(true, @"Local\DM-CLICK", out bool first);
        if (!first) return;   // ya esta corriendo

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);   // coordenadas fisicas en todo el proceso
        Application.EnableVisualStyles();

        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DM-CLICK");
        Directory.CreateDirectory(dataDir);
        var cfg = Config.Load(Path.Combine(dataDir, "config.ini"));
        bool reset = Array.IndexOf(args, "--reset") >= 0;
        if (reset) { cfg.Primary = null; cfg.Save(); }

        if (!Ic.Load(Path.Combine(AppContext.BaseDirectory, "interception.dll")))
        {
            Fail("Falta interception.dll junto a DM-CLICK.exe. Vuelve a ejecutar INSTALAR.cmd.");
            return;
        }
        IntPtr ctx = Ic.create();
        if (ctx == IntPtr.Zero)
        {
            Fail("El driver Interception no esta activo.\n\nEjecuta INSTALAR.cmd y reinicia Windows.");
            return;
        }

        var hub = new Hub();
        using var engine = new Engine(ctx, hub, cfg);
        engine.Start();

        if (!engine.HasPrimary)
            hub.Notify("Mueve primero tu mouse PRINCIPAL", "Quedara guardado. Para cambiarlo: icono de bandeja > Elegir mouse principal.");

        Application.Run(hub);
    }

    static void Fail(string msg) =>
        MessageBox.Show(msg, "DM-CLICK", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

sealed class Config
{
    public string Path;
    public string Primary;        // "<numero de dispositivo>|<hardware id>"
    public double Speed2 = 1.0;   // multiplicador de velocidad de los mouse secundarios

    public static Config Load(string path)
    {
        var c = new Config { Path = path };
        if (!File.Exists(path)) return c;
        foreach (var line in File.ReadAllLines(path))
        {
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
            if (k == "primary") c.Primary = v.Length > 0 ? v : null;
            else if (k == "speed2" && double.TryParse(v, System.Globalization.NumberStyles.Float,
                         System.Globalization.CultureInfo.InvariantCulture, out double s) && s > 0) c.Speed2 = s;
        }
        return c;
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Path,
                $"primary={Primary}\r\nspeed2={Speed2.ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
