namespace ProductosApi.Models;

public class Producto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    public int Stock { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}

public record ProductoIn(string Nombre, decimal Precio, int Stock);
