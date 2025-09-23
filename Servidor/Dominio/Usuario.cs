

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