using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Dominio;

public class Clase
{
    public string Nombre { get; set; }
    public string Descripcion { get; set; }
    public int CuposMaximos { get; set; }
    public DateTime Fecha { get; set; }
    public string Hora { get; set; }
    public List<Usuario> usuarios { get; set; } = new List<Usuario>();
    public string? UrlFoto { get; set; }
    public int Duracion { get; set; }
    public Usuario owner { get; set; }
    public string link { get; set; }

    public int CuposDisponibles 
    { 
        get
        {
            return CuposMaximos - usuarios.Count;
        }
    }

    public Clase(string _nombre, string _descripcion, int _duracion, int _cupos, Usuario _usuario, string _fechaInicio, string _horaInicio, string _urlFoto)
    {
        Nombre = _nombre;
        Descripcion = _descripcion;
        Duracion = _duracion;
        CuposMaximos = _cupos;
        owner = _usuario;
        Fecha = DateTime.Parse(_fechaInicio);
        Hora = _horaInicio;
        UrlFoto = _urlFoto;
    }

    public void AgregarUsuario()
    {

    }
}