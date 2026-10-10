// la flecha: ventanita transparente encima de todo, los clicks la atraviesan
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

class Puntero : Form
{
    readonly Bitmap imagen;
    readonly Bitmap imagenGrande;   // para el zoom del click
    readonly int margen;
    readonly Timer finDelZoom = new Timer { Interval = 120 };

    public Puntero(Color color, string etiqueta)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;

        float escala = DeviceDpi / 96f;   // zoom de pantalla
        margen = (int)Math.Ceiling(1.5f * escala);
        imagen = Dibujar(color, etiqueta, escala, margen);
        imagenGrande = Dibujar(color, etiqueta, escala * 1.2f, margen);
        Size = imagen.Size;

        finDelZoom.Tick += (s, e) =>
        {
            finDelZoom.Stop();
            Nativo.PonerImagenConTransparencia(Handle, imagen);
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Nativo.WS_EX_LAYERED
                        | Nativo.WS_EX_TRANSPARENT
                        | Nativo.WS_EX_TOOLWINDOW
                        | Nativo.WS_EX_NOACTIVATE
                        | Nativo.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Nativo.PonerImagenConTransparencia(Handle, imagen);
    }

    public void MoverA(int x, int y)
    {
        // topmost cada vez para quedar encima de los menus
        Nativo.SetWindowPos(Handle, Nativo.HWND_TOPMOST, x - margen, y - margen, 0, 0,
            Nativo.SWP_NOSIZE | Nativo.SWP_NOACTIVATE | Nativo.SWP_SHOWWINDOW | Nativo.SWP_NOSENDCHANGING);
    }

    // se agranda un momento y vuelve a su tamaño
    public void Pulsar()
    {
        Nativo.PonerImagenConTransparencia(Handle, imagenGrande);
        finDelZoom.Stop();
        finDelZoom.Start();
    }

    public void Esconder()
    {
        Nativo.ShowWindow(Handle, Nativo.SW_HIDE);
    }

    static Bitmap Dibujar(Color color, string etiqueta, float escala, int margen)
    {
        // forma de la flecha de windows
        PointF[] flecha =
        {
            new PointF(0, 0), new PointF(0, 17), new PointF(4.2f, 13), new PointF(7, 19.5f),
            new PointF(9.6f, 18.4f), new PointF(6.9f, 12.2f), new PointF(12.2f, 12.2f),
        };
        for (int i = 0; i < flecha.Length; i++)
            flecha[i] = new PointF(margen + flecha[i].X * escala, margen + flecha[i].Y * escala);

        bool conEtiqueta = etiqueta != null;
        int ancho = (int)((conEtiqueta ? 34 : 16) * escala) + margen;
        int alto = (int)((conEtiqueta ? 32 : 23) * escala) + margen;

        var imagen = new Bitmap(ancho, alto, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(imagen);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        Color colorBorde = color == Color.White ? Color.Black : Color.White;
        using (var relleno = new SolidBrush(color))
        using (var borde = new Pen(colorBorde, 1.2f * escala) { LineJoin = LineJoin.Round })
        {
            g.FillPolygon(relleno, flecha);
            g.DrawPolygon(borde, flecha);
        }

        if (conEtiqueta)
            DibujarEtiqueta(g, color, etiqueta, escala, margen);

        return imagen;
    }

    // cuadrito con el numero como anydesk
    static void DibujarEtiqueta(Graphics g, Color color, string texto, float escala, int margen)
    {
        var caja = new RectangleF(margen + 12 * escala, margen + 18 * escala, 20 * escala, 13 * escala);
        float curva = 4 * escala;

        using var forma = new GraphicsPath();
        forma.AddArc(caja.X, caja.Y, curva, curva, 180, 90);
        forma.AddArc(caja.Right - curva, caja.Y, curva, curva, 270, 90);
        forma.AddArc(caja.Right - curva, caja.Bottom - curva, curva, curva, 0, 90);
        forma.AddArc(caja.X, caja.Bottom - curva, curva, curva, 90, 90);
        forma.CloseFigure();

        using var relleno = new SolidBrush(color);
        g.FillPath(relleno, forma);

        using var letra = new Font("Segoe UI", 7.5f * escala, FontStyle.Bold, GraphicsUnit.Pixel);
        using var centrado = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(texto, letra, Brushes.White, caja, centrado);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            finDelZoom.Dispose();
            imagen.Dispose();
            imagenGrande.Dispose();
        }
        base.Dispose(disposing);
    }
}
