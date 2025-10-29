using System;
using System.Collections.Generic;

namespace Dominio
{
    public class Clase
    {
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public int CuposMaximos { get; set; }
        public DateTime Fecha { get; set; }
        public string Hora { get; set; }
        public List<Usuario> usuarios { get; set; } = new List<Usuario>();
        public string? UrlFoto { get; set; } = "No image uploaded";
        public int Duracion { get; set; }
        public Usuario owner { get; set; }
        public string Link { get; set; }

        public int CuposDisponibles
        {
            get { return CuposMaximos - usuarios.Count; }
        }

        public Clase(string _nombre, string _descripcion, int _duracion, int _cupos, Usuario _usuario, string _fechaInicio, string _horaInicio)
        {
            Nombre = _nombre;
            Descripcion = _descripcion;
            Duracion = _duracion;
            CuposMaximos = _cupos;
            owner = _usuario;
            Fecha = DateTime.Parse(_fechaInicio);
            Hora = _horaInicio;
            Link = $"www.classlink.com/{Nombre.Replace(" ", "_").ToLower()}";
        }

        public void AgregarUsuario(Usuario usuarioAAgregar)
        {
            usuarios.Add(usuarioAAgregar);
        }
    }
}
