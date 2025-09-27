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
    public List<Usuario> usuarios { get; set; }
    public string? urlFoto { get; set; }
    public int Duracion { get; set; }
    public Usuario owner { get; set; }
    public string link { get; set; }

    public Clase(string _nombre, string _descripcion, int _duracion, int _cupos, Usuario _usuario)
    {
        Nombre = _nombre;
        Descripcion = _descripcion;
        Duracion = _duracion;
        Fecha = DateTime.Now;
        CuposMaximos = _cupos;
        owner = _usuario;
    }

    public void AgregarUsuario()
    {

    }
}