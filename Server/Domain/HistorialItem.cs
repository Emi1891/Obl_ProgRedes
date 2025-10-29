using System;

namespace Dominio
{
    public class HistorialItem
    {
        public string ClaseNombre { get; set; }
        public DateTime FechaInscripcion { get; set; }
        public string Estado { get; set; }
        public HistorialItem(string claseNombre, DateTime fecha, string estado)
        {
            ClaseNombre = claseNombre;
            FechaInscripcion = fecha;
            Estado = estado;
        }
    }
}
