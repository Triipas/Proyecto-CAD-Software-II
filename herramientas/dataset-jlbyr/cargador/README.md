# Cargador del dataset

Vacía una base de datos del sistema y carga el [dataset de J. L. Bustamante y Rivero](../README.md), con las fechas desplazadas
para que el periodo de 13 semanas **termine en el momento de la carga**. Así «hoy», «7 días» y «¿Cómo está la central ahora?»
muestran datos.

Todo ocurre en una sola transacción: si algo falla, la base de datos queda como estaba.

## Uso

```bash
# Base de datos local del servidor (la de sus user-secrets)
dotnet run --project herramientas/dataset-jlbyr/cargador -c Release

# Neon (Render): un archivo con la URL que copia la consola de Neon (PGURL=postgresql://…)
dotnet run --project herramientas/dataset-jlbyr/cargador -c Release -- --conexion ~/.cad/neon.env --confirmar <host-de-neon>
```

| Opción | Qué hace |
|---|---|
| `--conexion <archivo>` | Archivo con la URL de PostgreSQL (`PGURL=postgresql://…`) o una cadena de Npgsql. Sin ella, usa `ConnectionStrings:Cad` de los user-secrets del servidor. |
| `--confirmar <host>` | Obligatoria si la base de datos no es local: repite el host para confirmar que se vaciará esa base. |
| `--fin <fecha y hora>` | Dónde termina el periodo (ISO 8601). Por omisión, ahora. No puede estar en el futuro. |
| `--credenciales <archivo>` | Dónde guardar las credenciales. Por omisión, `~/.cad/cargador-credenciales.txt`. |
| `--solo-vaciar` | Solo vacía la base de datos (conserva el administrador). |
| `--dataset <carpeta>` | La carpeta del dataset, si no la encuentra sola. |

La base de datos debe tener todas las migraciones: si es nueva, arranca el servidor una vez contra ella antes de cargar.

## Qué hace

1. **Vacía** todas las tablas de datos y borra las cuentas, salvo el administrador. Conserva lo que el sistema necesita para arrancar:
   migraciones, roles, categorías y claves de las cookies del panel.
2. **Crea las cuentas:** 6 operadores, 36 serenos y 1300 ciudadanos, con su perfil.
   - El personal entra con su correo y una contraseña común. Los ciudadanos entran con su celular y un PIN común.
   - Las dos claves se generan la primera vez y se guardan en el archivo de credenciales, con la lista de cuentas. Ese archivo queda
     fuera del repositorio.
   - Las cargas siguientes reutilizan las mismas claves.
3. **Reproduce cada caso evento por evento** con los métodos del dominio (`Caso.EmitirSos`, `Asignar`, `Cerrar`, `Decidir`…). Así
   quedan coherentes:
   - el estado, el historial y las asignaciones;
   - las decisiones, los cierres y las evaluaciones;
   - las notificaciones al ciudadano, la mayoría ya leídas.

   Si algún caso no termina en el estado que dice el dataset, no carga nada.
4. **Genera lo que el dataset no guarda:**
   - **El estado operativo de cada sereno:** entra en servicio al empezar su turno y sale al terminarlo.
   - **Una posición cada 5 minutos durante el turno:** patrulla las vías de su sector y, mientras atiende un caso, va hacia el punto
     y se queda ahí. Es la base de «¿Dónde falta cobertura?».
   - **Los envíos push del despacho:** el 95 % llega.
   - **Las fotos de los reportes:** imágenes de muestra (`fotos/`).
   - **Los días de uso de la app.**

## Cuándo cargar

El periodo termina en el momento de la carga, y los datos «envejecen»: los 55 casos abiertos llevan de 1 a 60 minutos al cargar,
y cada minuto que pasa se suma a su espera. Para una presentación, conviene **cargar justo antes de empezar** (5 a 15 minutos
antes): no hace falta vaciar nada a mano, el cargador vacía y vuelve a cargar todo. Cada carga tarda unos 30 s en local y
unos minutos contra Neon; las horas parten del inicio de la carga.

Las horas del día se desplazan junto con el periodo. Si se carga a las 20:00, los picos quedan donde los puso el generador
(robos a la salida del trabajo, alcohol y ruido de noche); a otra hora, se corren las mismas horas.

Después de cargar en Render, el panel puede mostrar durante unos minutos la analítica que tenía en caché.

## Lo que ocupa

Unos 110 MB en PostgreSQL. Casi todo son las posiciones de los serenos: unas 275 000.
