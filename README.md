# Productos NoSQL con estrategia de respaldo - Actividad Grupal 02-U2

Integrantes:

- Yerry Maycol Tarqui Collatupa
- Milton H Flores Chino

Aplicacion de productos con ASP.NET Core (.NET 10) y Azure Cosmos DB (NoSQL), con una estrategia de respaldo y recuperacion automatizada con GitHub Actions. La aplicacion esta publicada en Azure App Service: https://app-tg2-backup-yerrytc.azurewebsites.net

## Estrategia de respaldo

La estrategia combina tres capas:

1. Respaldo nativo de Cosmos DB: politica Periodic cada 240 minutos con retencion de 48 horas, definida en Terraform.
2. Respaldo logico propio: la herramienta `BackupTool` lee todos los documentos, los empaqueta en JSON con un manifiesto (fecha, base, contenedor, cantidad), calcula el SHA-256, los comprime con GZip y los cifra con AES-256-GCM (clave derivada con PBKDF2). El archivo `.json.gz.enc` se guarda en un contenedor privado de Blob Storage.
3. Retencion automatica: una politica de ciclo de vida elimina los respaldos con mas de 30 dias.

La recuperacion descarga el respaldo, lo descifra, restaura los documentos en el contenedor indicado y verifica que la cantidad coincida con la del manifiesto.

### Respaldo diario automatico

`backup.yml` esta programado con `cron: '17 10 * * *'`: GitHub Actions lo ejecuta solo todos los dias a las 10:17 UTC (05:17 hora de Peru), sin intervencion manual. Tambien puede lanzarse a demanda con `workflow_dispatch`. Cada ejecucion genera un archivo `productos-AAAAMMDD-HHMMSS.json.gz.enc` nuevo en el contenedor `backups`, y la politica de ciclo de vida elimina los que superan los 30 dias. El flujo se probo de forma manual (ver Respaldo en las evidencias).

## Arquitectura

- `src/ProductosApi`: API minima y pagina web para administrar productos en Cosmos DB.
- `src/BackupTool`: herramienta de consola con los comandos `backup`, `restore`, `listar` y `destruir --confirmar`.
- `infra`: Terraform con Cosmos DB serverless, cuenta de Storage con contenedor `backups`, plan F1 y App Service.
- `.github/workflows`: automatizacion.

| Workflow | Disparador | Funcion |
| --- | --- | --- |
| `ci.yml` | cada push y pull request | compila ambos proyectos y valida Terraform |
| `infra.yml` | manual | crea la infraestructura en Azure con Terraform |
| `deploy.yml` | push a `src/ProductosApi` o manual | publica la aplicacion en App Service |
| `backup.yml` | diario 10:17 UTC y manual | genera el respaldo cifrado |
| `restore.yml` | manual (`blob`, `destino`) | restaura un respaldo y verifica |
| `desastre.yml` | manual (`confirmar=BORRAR`) | simula la perdida de datos |

## Ejecucion local

```
dotnet run --project src/ProductosApi
```

Variables necesarias: `Cosmos__Endpoint` y `Cosmos__Key`. Para la herramienta de respaldo tambien `BACKUP_STORAGE_CONNECTION` y `BACKUP_PASSPHRASE`:

```
dotnet run --project src/BackupTool -- backup
dotnet run --project src/BackupTool -- listar
dotnet run --project src/BackupTool -- restore --blob ultimo --destino productos_restaurado
```

## Evidencias

### Construccion del proyecto

Creacion de la carpeta y los proyectos.

![paso01](evidencias/paso01_powershell_proyecto.png)

![paso02](evidencias/paso02_dotnet_new_web.png)

Paquetes de la herramienta de respaldo.

![paso03](evidencias/paso03_paquetes_backuptool.png)

Codigo de la API y del repositorio de datos.

![paso04](evidencias/paso04_productosapi_program_cs.png)

![paso05](evidencias/paso05_productosrepo_cs.png)

Herramienta de respaldo y recuperacion.

![paso06](evidencias/paso06_backuptool_program_cs.png)

![paso07](evidencias/paso07_backuptool_restore.png)

Infraestructura y workflows de respaldo.

![paso08](evidencias/paso08_main_tf.png)

![paso09](evidencias/paso09_backup_yml.png)

![paso10](evidencias/paso10_restore_yml.png)

### Despliegue automatizado

Ejecucion del workflow de infraestructura.

![paso11](evidencias/paso11_gh_workflow_run_infra.png)

![paso12](evidencias/paso12_actions_infra_en_curso.png)

Ejecucion del workflow de despliegue.

![paso13](evidencias/paso13_gh_workflow_run_deploy.png)

![paso14](evidencias/paso14_actions_deploy_en_curso.png)

### Aplicacion en la nube

Aplicacion publica sin datos, con los productos de ejemplo y con un producto nuevo.

![paso15](evidencias/paso15_app_publica_vacia.png)

![paso16](evidencias/paso16_app_publica_ejemplos.png)

![paso17](evidencias/paso17_app_publica_producto_nuevo.png)

### Respaldo

Ejecucion del respaldo, resultado y detalle del paso que genera el archivo cifrado.

![paso18](evidencias/paso18_gh_workflow_run_backup.png)

![paso19](evidencias/paso19_actions_backup_en_curso.png)

![paso20](evidencias/paso20_actions_backup_exitoso.png)

![paso21](evidencias/paso21_actions_backup_log.png)

Archivo cifrado en el contenedor privado `backups` de Azure Storage.

![paso22](evidencias/paso22_portal_blob_cifrado.png)

### Simulacion de desastre

Eliminacion de todos los productos y aplicacion sin datos.

![paso23](evidencias/paso23_gh_workflow_run_desastre.png)

![paso24](evidencias/paso24_actions_desastre.png)

![paso25](evidencias/paso25_app_sin_datos.png)

### Recuperacion

Restauracion desde el ultimo respaldo, verificacion y aplicacion con los 6 productos recuperados.

![paso26](evidencias/paso26_gh_workflow_run_restore.png)

![paso27](evidencias/paso27_actions_restore.png)

![paso29](evidencias/paso29_actions_restore_log.png)

![paso28](evidencias/paso28_app_restaurada.png)

### Repositorio publico y automatizacion

Repositorio publico con el ultimo commit validado y lista de workflows ejecutados.

![paso30](evidencias/paso30_repositorio_publico.png)

![paso31](evidencias/paso31_actions_workflows.png)
