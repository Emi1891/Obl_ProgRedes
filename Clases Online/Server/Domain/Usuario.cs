using System;
using System.Collection.Generic;
using System.Linq;
using System.Text
using System.Threading.Tasks;


public class Usuario{
    public string nombre;
    public string contrasena;
    public List<Clase> clasesCreadas;

    public Usuario(string nombre, string contrasena){
        this.nombre = nombre;
        this.contrasena = contrasena;
        this.clasesCreadas = new List<Clase>();
    }
}