using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var cmd = args.Length > 0 ? args[0] : "ayuda";
var op = new Dictionary<string, string>();
for (var i = 1; i < args.Length; i++)
{
    if (!args[i].StartsWith("--"))
        continue;
    var k = args[i].TrimStart('-');
    op[k] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
}

var dbn = Environment.GetEnvironmentVariable("Cosmos__Database") ?? "productosdb";
var conn = Environment.GetEnvironmentVariable("Cosmos__Container") ?? "productos";

try
{
    switch (cmd)
    {
        case "backup":
            await Backup();
            break;
        case "restore":
            await Restore();
            break;
        case "listar":
            await Listar();
            break;
        case "destruir":
            await Destruir();
            break;
        default:
            Console.WriteLine("Uso: BackupTool backup | restore [--blob nombre|ultimo] [--destino contenedor] | listar | destruir --confirmar");
            break;
    }
}
catch (Exception e)
{
    Console.WriteLine("ERROR: " + e.Message);
    Environment.Exit(1);
}

string Env(string n)
{
    return Environment.GetEnvironmentVariable(n) ?? throw new Exception("Falta la variable " + n);
}

CosmosClient Cli()
{
    return new CosmosClient(Env("Cosmos__Endpoint"), Env("Cosmos__Key"));
}

BlobContainerClient Almacen()
{
    return new BlobContainerClient(Env("BACKUP_STORAGE_CONNECTION"), "backups");
}

byte[] Comprimir(byte[] d)
{
    using var ms = new MemoryStream();
    using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
        gz.Write(d, 0, d.Length);
    return ms.ToArray();
}

byte[] Descomprimir(byte[] d)
{
    using var ms = new MemoryStream(d);
    using var gz = new GZipStream(ms, CompressionMode.Decompress);
    using var o = new MemoryStream();
    gz.CopyTo(o);
    return o.ToArray();
}

byte[] Cifrar(byte[] d, string clave)
{
    var sal = RandomNumberGenerator.GetBytes(16);
    var nonce = RandomNumberGenerator.GetBytes(12);
    var k = Rfc2898DeriveBytes.Pbkdf2(clave, sal, 200000, HashAlgorithmName.SHA256, 32);
    var c = new byte[d.Length];
    var tag = new byte[16];
    using var aes = new AesGcm(k, 16);
    aes.Encrypt(nonce, d, c, tag);
    return Encoding.ASCII.GetBytes("TG2B").Concat(sal).Concat(nonce).Concat(tag).Concat(c).ToArray();
}

byte[] Descifrar(byte[] d, string clave)
{
    if (d.Length < 48 || Encoding.ASCII.GetString(d, 0, 4) != "TG2B")
        throw new Exception("El archivo no tiene el formato de respaldo esperado");
    var sal = d[4..20];
    var nonce = d[20..32];
    var tag = d[32..48];
    var c = d[48..];
    var k = Rfc2898DeriveBytes.Pbkdf2(clave, sal, 200000, HashAlgorithmName.SHA256, 32);
    var o = new byte[c.Length];
    using var aes = new AesGcm(k, 16);
    aes.Decrypt(nonce, c, tag, o);
    return o;
}

async Task Backup()
{
    var cont = Cli().GetContainer(dbn, conn);
    var docs = new JArray();
    var q = cont.GetItemQueryIterator<JObject>("SELECT * FROM c");
    while (q.HasMoreResults)
    {
        foreach (var d in await q.ReadNextAsync())
        {
            foreach (var s in new[] { "_rid", "_self", "_etag", "_attachments", "_ts" })
                d.Remove(s);
            docs.Add(d);
        }
    }
    var p = new JObject
    {
        ["version"] = 1,
        ["fecha"] = DateTime.UtcNow,
        ["base"] = dbn,
        ["contenedor"] = conn,
        ["cantidad"] = docs.Count,
        ["documentos"] = docs
    };
    var raw = Encoding.UTF8.GetBytes(p.ToString(Formatting.None));
    var sha = Convert.ToHexString(SHA256.HashData(raw)).ToLower();
    var enc = Cifrar(Comprimir(raw), Env("BACKUP_PASSPHRASE"));
    var nombre = $"{conn}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json.gz.enc";
    var al = Almacen();
    await al.CreateIfNotExistsAsync();
    var bl = al.GetBlobClient(nombre);
    await bl.UploadAsync(new BinaryData(enc), false);
    await bl.SetMetadataAsync(new Dictionary<string, string> { ["cantidad"] = docs.Count.ToString(), ["sha256"] = sha, ["contenedor"] = conn });
    Console.WriteLine("Documentos respaldados: " + docs.Count);
    Console.WriteLine("Archivo cifrado: " + nombre + " (" + enc.Length + " bytes)");
    Console.WriteLine("SHA-256 de los datos: " + sha);
    Console.WriteLine("BACKUP OK");
}

async Task Restore()
{
    var al = Almacen();
    var nombre = op.GetValueOrDefault("blob", "ultimo");
    if (nombre == "ultimo" || nombre == "latest")
    {
        BlobItem? u = null;
        await foreach (var b in al.GetBlobsAsync(new GetBlobsOptions { Traits = BlobTraits.Metadata }))
        {
            if (u is null || b.Properties.LastModified > u.Properties.LastModified)
                u = b;
        }
        nombre = u?.Name ?? throw new Exception("No hay respaldos en el almacenamiento");
    }
    var bytes = (await al.GetBlobClient(nombre).DownloadContentAsync()).Value.Content.ToArray();
    var raw = Descomprimir(Descifrar(bytes, Env("BACKUP_PASSPHRASE")));
    var sha = Convert.ToHexString(SHA256.HashData(raw)).ToLower();
    var p = JObject.Parse(Encoding.UTF8.GetString(raw));
    var docs = (JArray)p["documentos"]!;
    var esperado = (int)p["cantidad"]!;
    Console.WriteLine("Respaldo: " + nombre);
    Console.WriteLine("Fecha del respaldo: " + p["fecha"]);
    Console.WriteLine("SHA-256 de los datos: " + sha);
    var destino = op.GetValueOrDefault("destino", conn + "_restaurado");
    var cli = Cli();
    var db = cli.GetDatabase(dbn);
    var cont = (await db.CreateContainerIfNotExistsAsync(destino, "/id")).Container;
    foreach (var d in docs)
        await cont.UpsertItemAsync((JObject)d, new PartitionKey((string)d["id"]!));
    var q = cont.GetItemQueryIterator<int>("SELECT VALUE COUNT(1) FROM c");
    var total = (await q.ReadNextAsync()).First();
    Console.WriteLine("Documentos restaurados en '" + destino + "': " + docs.Count);
    Console.WriteLine("Documentos en el contenedor ahora: " + total);
    if (docs.Count != esperado || total < esperado)
        throw new Exception("La verificacion fallo: se esperaban " + esperado + " documentos");
    Console.WriteLine("VERIFICACION OK: " + esperado + " documentos recuperados");
}

async Task Listar()
{
    var al = Almacen();
    var n = 0;
    await foreach (var b in al.GetBlobsAsync(new GetBlobsOptions { Traits = BlobTraits.Metadata }))
    {
        n++;
        b.Metadata.TryGetValue("cantidad", out var c);
        Console.WriteLine(b.Properties.LastModified?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC  " + b.Properties.ContentLength + " bytes  docs=" + c + "  " + b.Name);
    }
    Console.WriteLine("Respaldos almacenados: " + n);
}

async Task Destruir()
{
    if (!op.ContainsKey("confirmar"))
        throw new Exception("Agregue --confirmar para borrar todos los documentos de " + conn);
    var cont = Cli().GetContainer(dbn, conn);
    var ids = new List<string>();
    var q = cont.GetItemQueryIterator<string>("SELECT VALUE c.id FROM c");
    while (q.HasMoreResults)
        ids.AddRange(await q.ReadNextAsync());
    foreach (var id in ids)
        await cont.DeleteItemAsync<JObject>(id, new PartitionKey(id));
    Console.WriteLine("Documentos eliminados de '" + conn + "': " + ids.Count);
}
