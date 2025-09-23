public class Cliente
{
    public string nombre;
    public string contrasena;
    public List<Clases> clasesCreadas;
    
    public cliente(string nombre, string contrasena)
    {
        this.nombre = nombre;
        this.contrasena = contrasena;
        this.clasesCreadas = new List<Clases>();
    }
    
}
