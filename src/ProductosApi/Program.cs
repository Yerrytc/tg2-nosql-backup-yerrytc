using System.Text.Json;
using Microsoft.Azure.Cosmos;
using ProductosApi.Models;
using ProductosApi.Services;

var b = WebApplication.CreateBuilder(args);
var cfg = b.Configuration.GetSection("Cosmos");
var opt = new CosmosClientOptions
{
    UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
};
var cli = new CosmosClient(cfg["Endpoint"], cfg["Key"], opt);
var db = (await cli.CreateDatabaseIfNotExistsAsync(cfg["Database"] ?? "productosdb")).Database;
var con = (await db.CreateContainerIfNotExistsAsync(cfg["Container"] ?? "productos", "/id")).Container;
b.Services.AddSingleton(new ProductosRepo(con));

var app = b.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (ProductosRepo r) => Results.Ok(new { estado = "ok", base_datos = "Azure Cosmos DB", productos = await r.Contar() }));

var g = app.MapGroup("/api/productos");

g.MapGet("/", async (ProductosRepo r) => Results.Ok(await r.Listar()));

g.MapGet("/{id}", async (string id, ProductosRepo r) =>
    await r.Buscar(id) is Producto p ? Results.Ok(p) : Results.NotFound());

g.MapPost("/", async (ProductoIn x, ProductosRepo r) =>
{
    if (string.IsNullOrWhiteSpace(x.Nombre))
        return Results.BadRequest(new { error = "El nombre es obligatorio" });
    var p = await r.Crear(new Producto { Nombre = x.Nombre.Trim(), Precio = x.Precio, Stock = x.Stock });
    return Results.Created($"/api/productos/{p.Id}", p);
});

g.MapPost("/ejemplos", async (ProductosRepo r) =>
{
    var datos = new[]
    {
        ("Laptop Lenovo", 2499.90m, 12),
        ("Mouse Logitech", 89.50m, 40),
        ("Teclado mecanico", 189.00m, 25),
        ("Monitor 24 pulgadas", 649.00m, 8),
        ("Audifonos Sony", 229.90m, 30)
    };
    foreach (var (n, pr, s) in datos)
        await r.Crear(new Producto { Nombre = n, Precio = pr, Stock = s });
    return Results.Ok(new { agregados = datos.Length });
});

g.MapPut("/{id}", async (string id, ProductoIn x, ProductosRepo r) =>
{
    var p = await r.Buscar(id);
    if (p is null)
        return Results.NotFound();
    p.Nombre = x.Nombre.Trim();
    p.Precio = x.Precio;
    p.Stock = x.Stock;
    return Results.Ok(await r.Actualizar(p));
});

g.MapDelete("/{id}", async (string id, ProductosRepo r) =>
{
    if (await r.Buscar(id) is null)
        return Results.NotFound();
    await r.Borrar(id);
    return Results.NoContent();
});

app.Run();
