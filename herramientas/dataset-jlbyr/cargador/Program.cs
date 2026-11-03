using System.Diagnostics;
using Cad.CargadorDataset;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Serenos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

// Vacía la base de datos (salvo el administrador) y carga el dataset de J. L. Bustamante y Rivero con las fechas desplazadas
// para que el periodo termine ahora. Todo en una transacción: si algo falla, la base de datos queda como estaba.
// Uso y opciones: README.md de esta carpeta.

const string UserSecretsServidor = "9ad5770a-a2bb-4267-8ded-529c4c72d3ef";

var opciones = args.Select((a, i) => (a, i)).Where(x => x.a.StartsWith("--"))
    .ToDictionary(x => x.a[2..], x => x.i + 1 < args.Length && !args[x.i + 1].StartsWith("--") ? args[x.i + 1] : "");
string? Opcion(string nombre) => opciones.TryGetValue(nombre, out var v) ? v : null;

try
{
    var conexion = new NpgsqlConnectionStringBuilder(LeerConexion(Opcion("conexion"))) { CommandTimeout = 600, IncludeErrorDetail = false };
    var local = conexion.Host is "localhost" or "127.0.0.1" or "::1";
    Console.WriteLine($"Base de datos: {conexion.Database} en {conexion.Host}");
    if (!local && Opcion("confirmar") != conexion.Host)
        throw new ArgumentException($"Es una base de datos remota: repite el comando con --confirmar {conexion.Host} para continuar.");

    var carpeta = Opcion("dataset") ?? BuscarDataset();
    var fin = Opcion("fin") is { } texto ? DateTimeOffset.Parse(texto) : DateTimeOffset.UtcNow;
    fin = new DateTimeOffset(fin.UtcTicks - fin.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    if (fin > DateTimeOffset.UtcNow.AddMinutes(1))
        throw new ArgumentException("--fin no puede estar en el futuro: los casos quedarían registrados después de ahora.");

    await using var db = new CadDbContext(new DbContextOptionsBuilder<CadDbContext>().UseNpgsql(conexion.ConnectionString).Options);
    var pendientes = (await db.Database.GetPendingMigrationsAsync()).ToList();
    if (pendientes.Count > 0)
        throw new InvalidOperationException($"A la base de datos le faltan {pendientes.Count} migraciones: arranca el servidor una vez para aplicarlas.");

    var reloj = Stopwatch.StartNew();
    await using var transaccion = await db.Database.BeginTransactionAsync();
    Console.WriteLine("Vaciando (se conserva solo el administrador)…");
    await Vaciado.VaciarAsync(db);
    if (opciones.ContainsKey("solo-vaciar"))
    {
        await transaccion.CommitAsync();
        Console.WriteLine("Listo: base de datos vacía.");
        return 0;
    }

    var datos = Dataset.Leer(carpeta);
    var rutaCredenciales = Opcion("credenciales")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cad", "cargador-credenciales.txt");
    var credenciales = Credenciales.LeerOCrear(rutaCredenciales);
    var fotos = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fotos"), "*.jpg").Order().Select(File.ReadAllBytes).ToList();

    Console.WriteLine($"Construyendo el dataset con el periodo terminando el {fin.ToOffset(TimeSpan.FromHours(-5)):dd/MM/yyyy HH:mm} (hora de Lima)…");
    var construccion = new Construccion(datos, fin, credenciales, fotos);
    construccion.Construir(db);
    if (construccion.Discrepancias.Count > 0)
        throw new InvalidDataException("El historial no reproduce el estado del dataset:\n" + string.Join('\n', construccion.Discrepancias.Take(10)));

    // Estado operativo de cada sereno: entra en servicio al empezar su turno y sale al terminarlo.
    var turnos = datos.Turnos.GroupBy(t => t.Sereno).ToDictionary(g => g.Key, g => g
        .Select(t => (Horario: Patrullaje.Horario(t), t.Sector))
        .Select(t => (Inicio: construccion.T(t.Horario.Inicio), Fin: construccion.T(t.Horario.Fin), t.Sector))
        .Where(t => t.Inicio < fin).OrderBy(t => t.Inicio).ToList());
    foreach (var (id, sereno) in construccion.Serenos)
    {
        var lista = turnos.GetValueOrDefault(sereno.Id) ?? [];
        var estado = EstadoSereno.Nuevo(id, lista.Count > 0 ? lista[0].Inicio.AddMinutes(-1) : fin);
        foreach (var turno in lista)
        {
            if (estado.Cambiar(true, turno.Inicio))
                db.CambiosEstadoSereno.Add(CambioEstadoSereno.Registrar(estado));
            if (turno.Fin <= fin && estado.Cambiar(false, turno.Fin))
                db.CambiosEstadoSereno.Add(CambioEstadoSereno.Registrar(estado));
        }
        db.EstadosSereno.Add(estado);
    }

    Console.WriteLine("Guardando cuentas, casos y acompañamientos…");
    await db.SaveChangesAsync();

    var conexionAbierta = (NpgsqlConnection)db.Database.GetDbConnection();
    Console.WriteLine("Guardando las posiciones de los serenos…");
    var patrullaje = new Patrullaje(datos, carpeta);
    long posiciones = 0;
    await using (var copia = await conexionAbierta.BeginBinaryImportAsync(
        """COPY "PosicionesSereno" ("Id", "SerenoId", "Latitud", "Longitud", "PrecisionMetros", "ObtenidaEn", "RecibidaEn") FROM STDIN (FORMAT BINARY)"""))
    {
        foreach (var (id, sereno) in construccion.Serenos)
        {
            var tramos = construccion.Tramos.GetValueOrDefault(id) ?? [];
            foreach (var p in patrullaje.Posiciones(turnos.GetValueOrDefault(sereno.Id) ?? [], tramos, fin))
            {
                await copia.StartRowAsync();
                await copia.WriteAsync(Guid.CreateVersion7(p.En), NpgsqlDbType.Uuid);
                await copia.WriteAsync(id, NpgsqlDbType.Uuid);
                await copia.WriteAsync(p.Lat, NpgsqlDbType.Double);
                await copia.WriteAsync(p.Lon, NpgsqlDbType.Double);
                await copia.WriteAsync(p.Precision, NpgsqlDbType.Double);
                await copia.WriteAsync(p.En.UtcDateTime, NpgsqlDbType.TimestampTz);
                await copia.WriteAsync(p.En.AddSeconds(1).UtcDateTime, NpgsqlDbType.TimestampTz);
                posiciones++;
            }
        }
        await copia.CompleteAsync();
    }

    // Días de uso de la app (adopción y retención), en la fecha de Lima ya desplazada.
    var hoyLima = DateOnly.FromDateTime(fin.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    var dias = construccion.Ciudadanos
        .SelectMany(c => datos.Actividad.GetValueOrDefault(c.Ciudadano, []).Select(d => (c.Id, Dia: DiaDesplazado(d))))
        .Where(x => x.Dia <= hoyLima).Distinct().ToList();
    await using (var copia = await conexionAbierta.BeginBinaryImportAsync(
        """COPY "ActividadesCiudadano" ("UsuarioId", "Dia") FROM STDIN (FORMAT BINARY)"""))
    {
        foreach (var (id, dia) in dias)
            await copia.WriteRowAsync(default, id, dia);
        await copia.CompleteAsync();
    }

    await transaccion.CommitAsync();
    credenciales.Guardar(rutaCredenciales, construccion.ListaDeCuentas());

    var porEstado = await db.Casos.AsNoTracking().GroupBy(c => c.Estado).Select(g => new { g.Key, Total = g.Count() }).ToListAsync();
    Console.WriteLine();
    Console.WriteLine($"Listo en {reloj.Elapsed.TotalSeconds:0} s.");
    Console.WriteLine($"  Cuentas: {await db.Users.CountAsync()} (con el administrador)");
    Console.WriteLine($"  Casos: {porEstado.Sum(e => e.Total)} — " + string.Join(", ", porEstado.OrderByDescending(e => e.Total)
        .Select(e => $"{EstadosCaso.Texto(e.Key)} {e.Total}")));
    Console.WriteLine($"  Acompañamientos: {await db.SesionesAcompanamiento.CountAsync()}");
    Console.WriteLine($"  Notificaciones al ciudadano: {await db.NotificacionesCiudadano.CountAsync()}");
    Console.WriteLine($"  Posiciones de serenos: {posiciones}; días de uso de la app: {dias.Count}");
    Console.WriteLine($"  Credenciales: {rutaCredenciales}");
    return 0;

    DateOnly DiaDesplazado(DateOnly dia)
    {
        var mediodia = new DateTimeOffset(dia.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-5));
        return DateOnly.FromDateTime(construccion.T(mediodia).ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or FileNotFoundException
    or DirectoryNotFoundException or NpgsqlException or DbUpdateException)
{
    // Sin la cadena de conexión ni datos de las personas: solo qué falló.
    Console.Error.WriteLine($"No se cargó nada: {ex.Message}");
    if (ex.InnerException is { } interna)
        Console.Error.WriteLine($"  {interna.Message}");
    return 1;
}

// La conexión: un archivo con la URL de Neon (PGURL=postgresql://…, la que copia su consola) o una cadena de Npgsql. Sin
// archivo, la del servidor en desarrollo (sus user-secrets).
static string LeerConexion(string? archivo)
{
    var valor = archivo is null
        ? new ConfigurationBuilder().AddUserSecrets(UserSecretsServidor).Build().GetConnectionString("Cad")
            ?? throw new ArgumentException("Sin --conexion y sin ConnectionStrings:Cad en los user-secrets del servidor.")
        : File.ReadAllLines(archivo).Select(l => l.Trim()).First(l => l.Length > 0 && !l.StartsWith('#'));
    // Una línea de .env (PGURL=postgresql://…): se queda la URL.
    if (valor.Split('=', 2) is [_, var url] && url.Trim().Trim('"', '\'').StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        valor = url.Trim().Trim('"', '\'');
    if (!valor.StartsWith("postgres", StringComparison.OrdinalIgnoreCase))
        return valor;

    var uri = new Uri(valor);
    var usuario = uri.UserInfo.Split(':', 2);
    var cadena = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
        Database = uri.AbsolutePath.TrimStart('/'),
        Username = Uri.UnescapeDataString(usuario[0]),
        Password = usuario.Length > 1 ? Uri.UnescapeDataString(usuario[1]) : null,
        SslMode = SslMode.Require,
    };
    foreach (var parametro in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)))
        if (parametro[0] == "channel_binding" && parametro.Length > 1 && parametro[1] == "require")
            cadena.ChannelBinding = ChannelBinding.Require;
    return cadena.ConnectionString;
}

// La carpeta del dataset, subiendo desde donde está el ejecutable (bin/…) o desde la carpeta actual.
static string BuscarDataset()
{
    foreach (var inicio in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        for (var d = new DirectoryInfo(inicio); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "salida", "casos.json")))
                return d.FullName;
            var candidata = Path.Combine(d.FullName, "herramientas", "dataset-jlbyr");
            if (File.Exists(Path.Combine(candidata, "salida", "casos.json")))
                return candidata;
        }
    throw new DirectoryNotFoundException("No encontré la carpeta del dataset: indícala con --dataset.");
}
