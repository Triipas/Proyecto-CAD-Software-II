# Sistema Integrado CAD — MVP académico

Prototipo académico (Ingeniería de Software II, USMP) de alertas SOS georreferenciadas y despacho para la seguridad ciudadana de una municipalidad tipo B. **No es un canal oficial de emergencias**: todos los datos son simulados.

Documentos de referencia (fuera del repositorio): Plan Scrum, Historias de usuario estructuradas y Plan de Arquitectura del Sistema Integrado CAD.

## Estado

Sprint 1 en curso. El avance por tarea está en [docs/trazabilidad-sprint1.md](docs/trazabilidad-sprint1.md).

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (fijado en `global.json`; acepta cualquier 10.0.x igual o superior a 10.0.100).

Requisitos previstos para bloques posteriores (todavía no necesarios): Docker Desktop (PostgreSQL local) y la carga de trabajo de .NET MAUI para Android.

## Estructura

```
Cad.slnx                     solución
src/Cad.Server/              Servidor CAD (ASP.NET Core 10): API, panel y tiempo real
tests/Cad.Server.Tests/      pruebas (xUnit + WebApplicationFactory)
docs/                        trazabilidad y documentación técnica
```

## Comandos

```bash
dotnet build Cad.slnx
dotnet test Cad.slnx
dotnet run --project src/Cad.Server --launch-profile http
```

Con el servidor en marcha, `http://localhost:5220/salud` responde `Healthy`. Este sondeo indica solo que el proceso está activo; no consulta la base de datos.

## Reglas del repositorio

- Ningún secreto, contraseña ni cadena de conexión en el repositorio: se usan variables de entorno y un `.env` local (ignorado por Git).
- No se registran en los logs contraseñas, tokens, coordenadas ni datos personales.
- La autorización se aplica en el servidor, no solo en la interfaz.
