using System.Net;
using Microsoft.Azure.Cosmos;
using ProductosApi.Models;

namespace ProductosApi.Services;

public class ProductosRepo
{
    private readonly Container c;

    public ProductosRepo(Container c)
    {
        this.c = c;
    }

    public async Task<List<Producto>> Listar()
    {
        var q = c.GetItemQueryIterator<Producto>("SELECT * FROM c ORDER BY c.fecha DESC");
        var r = new List<Producto>();
        while (q.HasMoreResults)
            r.AddRange(await q.ReadNextAsync());
        return r;
    }

    public async Task<Producto?> Buscar(string id)
    {
        try
        {
            return (await c.ReadItemAsync<Producto>(id, new PartitionKey(id))).Resource;
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Producto> Crear(Producto p)
    {
        return (await c.CreateItemAsync(p, new PartitionKey(p.Id))).Resource;
    }

    public async Task<Producto> Actualizar(Producto p)
    {
        return (await c.ReplaceItemAsync(p, p.Id, new PartitionKey(p.Id))).Resource;
    }

    public async Task Borrar(string id)
    {
        await c.DeleteItemAsync<Producto>(id, new PartitionKey(id));
    }

    public async Task<int> Contar()
    {
        var q = c.GetItemQueryIterator<int>("SELECT VALUE COUNT(1) FROM c");
        var r = await q.ReadNextAsync();
        return r.First();
    }
}
