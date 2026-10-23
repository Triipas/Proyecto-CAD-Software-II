# Servidor CAD: imagen para el entorno de validación (Render, ADR-06).
# Compilación con el SDK de .NET 10; ejecución con el runtime de ASP.NET Core 10, sin usuario root.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /fuente
# Primero los proyectos, para reutilizar la capa de restauración si solo cambia el código.
COPY global.json ./
COPY src/Cad.Contratos/Cad.Contratos.csproj src/Cad.Contratos/
COPY src/Cad.Calles/Cad.Calles.csproj src/Cad.Calles/
COPY src/Cad.Server/Cad.Server.csproj src/Cad.Server/
RUN dotnet restore src/Cad.Server/Cad.Server.csproj
COPY src/ src/
RUN dotnet publish src/Cad.Server/Cad.Server.csproj -c Release -o /publicacion --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS ejecucion
WORKDIR /app
COPY --from=compilacion /publicacion .
# Render dirige el tráfico al puerto de la variable PORT (definida en render.yaml como 8080).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080
# Usuario sin privilegios incluido en la imagen oficial.
USER $APP_UID
ENTRYPOINT ["dotnet", "Cad.Server.dll"]
