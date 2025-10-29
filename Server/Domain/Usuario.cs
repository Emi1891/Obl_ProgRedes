using System.Collections.Generic;

namespace Dominio
{
    public class Usuario
    {
        public string nombre;
        public string contrasena;
        public List<Clase> clasesCreadas;
        public List<HistorialItem> Historial;

        public Usuario(string nombre, string contrasena)
        {
            this.nombre = nombre;
            this.contrasena = contrasena;
            this.clasesCreadas = new List<Clase>();
            this.Historial = new List<HistorialItem>();
        }
    }
}
