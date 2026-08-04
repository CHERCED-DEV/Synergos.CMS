# syntax=docker/dockerfile:1
#
# Synergos.CMS — imagen para correr el host Umbraco fuera de la máquina
# del arquitecto (típicamente: Docker Desktop en la PC, consumido desde
# una tablet en la misma LAN).
#
# Dos etapas:
#   build   — SDK 10 porque global.json lo pinea (rollForward latestFeature),
#             aunque los proyectos targeteen net8.0.
#   runtime — aspnet 8.0, que es el TargetFramework real de Synergos.CMS.Web.
#
# El environment por defecto es `Docker`, NO `Development`: appsettings.
# Development.json tiene rutas cableadas a Windows (el cert de Kestrel en
# C:\LOCAL_CDN, el maildrop en el Desktop) y el cert faltante mata el bind
# HTTPS al arrancar. appsettings.Docker.json es el equivalente Linux.

# ─────────────────────────── build ───────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore en su propia capa: mientras no cambien los .csproj ni la lista
# central de paquetes, Docker reusa el caché y se saltea bajar Umbraco.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY Synergos.CMS.Interfaces/Synergos.CMS.Interfaces.csproj Synergos.CMS.Interfaces/
COPY Synergos.CMS.Application/Synergos.CMS.Application.csproj Synergos.CMS.Application/
COPY Synergos.CMS.Web/Synergos.CMS.Web.csproj Synergos.CMS.Web/
RUN dotnet restore Synergos.CMS.Web/Synergos.CMS.Web.csproj

COPY Synergos.CMS.Interfaces/ Synergos.CMS.Interfaces/
COPY Synergos.CMS.Application/ Synergos.CMS.Application/
COPY Synergos.CMS.Web/ Synergos.CMS.Web/

RUN dotnet publish Synergos.CMS.Web/Synergos.CMS.Web.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# ────────────────────────── runtime ──────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl es sólo para el HEALTHCHECK de abajo; la imagen aspnet no lo trae.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# uSync/v9 y App_Plugins son `None` para el SDK Web (no son .cs ni .cshtml
# ni wwwroot), así que `dotnet publish` NO los copia. Sin uSync/v9 el
# arquitecto no puede correr Import desde el backoffice, y sin App_Plugins
# el Layout Composer pierde sus custom views. Van explícitos.
#
# En docker-compose.yml uSync/v9 además se monta como bind desde el repo,
# para que ExportOnSave escriba el XML de vuelta en el árbol versionado.
COPY --from=build /src/Synergos.CMS.Web/uSync/v9 ./uSync/v9
COPY --from=build /src/Synergos.CMS.Web/App_Plugins ./App_Plugins

# Directorios de estado. Se crean acá para que existan aunque se arranque
# sin volúmenes; con volúmenes, Docker los monta encima. Nada se siembra
# en boot (ADR 0013) — son carpetas vacías.
#
# DataProtection-Keys es la ruta que ASP.NET elige sola en contenedor. Sin
# persistirla, cada `docker compose up --build` invalida las cookies del
# backoffice y los secretos TOTP de los members (ADR 0084). Va con volumen
# propio en docker-compose.yml.
RUN mkdir -p umbraco/Data umbraco/Logs umbraco/mediacache App_Data wwwroot/media \
             /root/.aspnet/DataProtection-Keys

# Qué versión es esta imagen. La inyecta el workflow con el SHA del commit y
# `/_health` la devuelve (HU #19): es lo que permite al humo de un despliegue
# distinguir «el sitio responde» de «el sitio responde con lo que acabo de
# subir». Sin esto, un reinicio que falla en silencio deja viva la versión
# anterior, el humo pasa, y el despliegue se da por bueno.
#
# Va en la etapa de runtime y no en la de build a propósito: cambiar el SHA no
# puede invalidar el caché de compilación, o cada commit reconstruiría todo.
ARG VERSION=desconocida

ENV ASPNETCORE_ENVIRONMENT=Docker \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_gcServer=0 \
    SYNERGOS_BUILD_SHA=${VERSION}

EXPOSE 8080

# Liveness, no readiness: a propósito SIN `--fail`. `/_health` devuelve 503
# cuando alguna probe está roja, y la probe `bundle_registry` está roja por
# diseño mientras no se monte el CDN local — que es el caso por defecto.
# Con `--fail` el contenedor viviría marcado `unhealthy` sin que nada falle.
# Lo que se chequea acá es que Kestrel conteste; el JSON de `/_health` sigue
# siendo la señal real de readiness para el operador.
#
# El primer boot instala Umbraco desatendido y compila los modelos en
# memoria; 180s de start-period le dan aire antes de contar fallos.
HEALTHCHECK --interval=30s --timeout=5s --start-period=180s --retries=5 \
    CMD curl --silent --output /dev/null http://localhost:8080/_health || exit 1

ENTRYPOINT ["dotnet", "Synergos.CMS.Web.dll"]
