# VelsatMobile API

Backend ASP.NET Core (.NET 6) de la app móvil Velsat. Se despliega con Docker en Ubuntu
(`docker-compose` 1.29.2), detrás de Nginx, usando la red Docker externa `app-net`
donde vive el contenedor MySQL `mysql-gts`.

| Dato | Valor |
|---|---|
| Servicio / contenedor | `velsat-mobile` |
| Imagen | `velsat-mobile:latest` |
| Kestrel (dentro del contenedor) | `http://0.0.0.0:8080` |
| Publicado en el host | `127.0.0.1:3011` (solo loopback) |
| URL pública (vía Nginx) | `https://velsat.pe:2083` |
| Configuración | `./VelsatBackendAPI/appsettings.json` (fuera de git, se crea a mano) → `/app/appsettings.json` (solo lectura) |
| Zona horaria | `America/Lima` |

## Requisitos en el servidor

- Docker y `docker-compose` 1.29.2
- Red externa ya creada: `docker network ls | grep app-net`
  (si no existiera: `docker network create app-net`)
- Contenedor `mysql-gts` conectado a `app-net`, con el usuario de las cadenas de conexión
  con permisos sobre las bases `gts` y `dbv16_01`

## Instalación

```bash
git clone <url-del-repo> velsat-mobile
cd velsat-mobile

# Crear la configuración a mano (NO está en git ni entra en la imagen)
nano VelsatBackendAPI/appsettings.json
chmod 644 VelsatBackendAPI/appsettings.json

docker-compose build
docker-compose up -d
```

`VelsatBackendAPI/appsettings.json` contiene las contraseñas y la clave privada de Firebase, por eso
está en `.gitignore` y `.dockerignore`. Se crea una sola vez en el servidor; `git pull` no lo toca.
docker-compose lo monta en el contenedor en solo lectura.

El contenedor corre con el usuario sin privilegios `10001:10001`; el archivo montado debe ser
legible por ese uid (`chmod 644`, o `chmod 640` + `sudo chgrp 10001`).

### Claves de `appsettings.json`

- `ConnectionStrings:DefaultConnection` → base `gts` en `mysql-gts`
- `ConnectionStrings:SecondConnection` → base `dbv16_01` en `mysql-gts`
- `settings:secretkey` → clave de firma de los JWT
- `Firebase:ProjectId` y `Firebase:Credentials` → JSON completo de la cuenta de servicio de
  Firebase pegado como objeto (ya no se usa `firebase-credentials.json`)

## Actualización

```bash
cd velsat-mobile
git pull && docker-compose build && docker-compose down && docker-compose up -d
```

Si solo editas `appsettings.json` en el servidor, basta con `docker-compose restart`.

## Comprobar que arrancó

```bash
docker-compose ps                       # State: Up (healthy)
docker-compose logs -f --tail=100       # buscar "Aplicación iniciada correctamente"
curl -fsS http://127.0.0.1:3011/swagger/v1/swagger.json -o /dev/null && echo OK
docker inspect --format '{{.State.Health.Status}}' velsat-mobile
```

El `HEALTHCHECK` consulta `/swagger/v1/swagger.json`, que no toca la base de datos. Para probar
la conexión a MySQL usa un endpoint real, por ejemplo `POST /api/Login/MobileLogin`.

Swagger UI: `https://velsat.pe:2083/swagger`

## Nginx (ejemplo)

```nginx
server {
    listen 2083 ssl http2;
    server_name velsat.pe;

    ssl_certificate     /etc/letsencrypt/live/velsat.pe/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/velsat.pe/privkey.pem;

    client_max_body_size 50m;

    location / {
        proxy_pass         http://127.0.0.1:3011;
        proxy_http_version 1.1;

        # WebSockets (SignalR)
        proxy_set_header   Upgrade $http_upgrade;
        proxy_set_header   Connection "upgrade";

        proxy_set_header   Host $host;
        proxy_set_header   X-Real-IP $remote_addr;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;

        # Los reportes pueden tardar (DefaultCommandTimeout=120)
        proxy_read_timeout 180s;
        proxy_send_timeout 180s;
    }
}
```

## Tareas en segundo plano

`DocumentosNotificacionJob` (BackgroundService) envía notificaciones push de documentos por
vencer todos los días a las **07:00** y **18:00** hora de Lima.
