using Cad.Contratos.Casos;
using Cad.Contratos.Serenos;
using Cad.Server.Modulos.Acompanamiento.Dominio;
using Cad.Server.Modulos.Analitica;
using Cad.Server.Modulos.Casos.Dominio;
using Cad.Server.Modulos.Cuentas.Dominio;
using Cad.Server.Modulos.Serenos.Dominio;
using Cad.Server.Persistencia;
using Microsoft.AspNetCore.Identity;

namespace Cad.CargadorDataset;

// Construye las entidades del dataset con los métodos del dominio, reproduciendo cada caso evento por evento: así el estado,
// el historial, las asignaciones y las notificaciones al ciudadano quedan como los dejaría la aplicación. Todas las horas se
// desplazan para que el periodo del dataset termine en «fin». Solo agrega al contexto: guardar es de quien lo llama.
public sealed class Construccion(Dataset datos, DateTimeOffset fin, Credenciales credenciales, IReadOnlyList<byte[]> fotos)
{
    // La versión de los términos que aceptan los ciudadanos (appsettings.json, Cuentas:VersionTerminos).
    private const string VersionTerminos = "2026-10-20";

    private readonly TimeSpan desplazamiento = fin - datos.FinDelPeriodo;
    private readonly Random azar = new(20261028);
    private readonly Dictionary<string, Usuario> personas = [];
    private readonly Dictionary<string, string> celularDe = [];
    private readonly Dictionary<string, Caso> casos = [];
    private readonly Dictionary<string, SesionAcompanamiento> sesiones = [];
    private readonly Dictionary<Guid, List<Tramo>> tramos = [];
    private readonly List<string> discrepancias = [];
    private CadDbContext db = null!;

    public IReadOnlyList<string> Discrepancias => discrepancias;
    public IReadOnlyDictionary<Guid, List<Tramo>> Tramos => tramos;
    public IEnumerable<(Guid Id, SerenoDataset Datos)> Serenos => datos.Personas.Serenos.Select(s => (personas[s.Id].Id, s));
    public IEnumerable<(Guid Id, string Ciudadano)> Ciudadanos => datos.Personas.Ciudadanos.Select(c => (personas[c.Id].Id, c.Id));

    public DateTimeOffset T(DateTimeOffset momento) => (momento + desplazamiento).ToUniversalTime();

    public void Construir(CadDbContext contexto)
    {
        db = contexto;
        CrearCuentas();
        foreach (var sesion in datos.Sesiones)
            CrearSesion(sesion);
        foreach (var caso in datos.Casos)
            CrearCaso(caso);
        foreach (var sesion in datos.Sesiones)
            CrearAvisos(sesion);
    }

    // Líneas del archivo de credenciales: quién es quién (sin contraseñas, que van arriba del archivo).
    public IEnumerable<string> ListaDeCuentas()
    {
        yield return "# Operadores (panel web): correo, nombre y turno";
        foreach (var o in datos.Personas.Operadores)
            yield return $"{o.Correo}\t{o.Nombres} {o.Apellidos}\t{o.Turno}";
        yield return "";
        yield return "# Serenos (app móvil): correo, nombre, turno y sector";
        foreach (var s in datos.Personas.Serenos)
            yield return $"{s.Correo}\t{s.Nombres} {s.Apellidos}\t{s.Turno}\tsector {s.Sector}";
        yield return "";
        yield return "# Ciudadanos con más casos (app móvil): celular, nombre y número de casos";
        foreach (var c in datos.Casos.Where(c => c.Ciudadano is not null).GroupBy(c => c.Ciudadano!).OrderByDescending(g => g.Count()).Take(10))
        {
            var ciudadano = datos.Personas.Ciudadanos.Single(x => x.Id == c.Key);
            yield return $"{celularDe[ciudadano.Id]}\t{ciudadano.Nombres} {ciudadano.Apellidos}\t{c.Count()} casos";
        }
    }

    private void CrearCuentas()
    {
        // Un solo hash para cada clave: el hasher de Identity incluye su propia sal, y calcularlo 472 veces solo haría más lenta
        // la carga. Cada cuenta lo verifica igual.
        var hasher = new PasswordHasher<Usuario>();
        var muestra = new Usuario { Nombres = "", Apellidos = "" };
        var hashContrasena = hasher.HashPassword(muestra, credenciales.ContrasenaPersonal);
        var hashPin = hasher.HashPassword(muestra, credenciales.PinCiudadanos);
        var altaPersonal = T(datos.FinDelPeriodo.AddDays(-120));
        var rol = Roles.Todos.ToDictionary(r => r.Nombre, r => r.Id);

        void Agregar(string id, Usuario usuario, string nombreRol)
        {
            usuario.NormalizedUserName = usuario.UserName!.ToUpperInvariant();
            usuario.NormalizedEmail = usuario.Email?.ToUpperInvariant();
            usuario.SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant();
            personas[id] = usuario;
            db.Users.Add(usuario);
            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = usuario.Id, RoleId = rol[nombreRol] });
        }

        foreach (var (o, n) in datos.Personas.Operadores.Select((o, i) => (o, i + 1)))
            Agregar(o.Id, Personal(o.Correo, o.Nombres, o.Apellidos, null, $"0019{n:0000}", $"OP-{n:000}"), Roles.Operador);
        foreach (var (s, n) in datos.Personas.Serenos.Select((s, i) => (s, i + 1)))
            Agregar(s.Id, Personal(s.Correo, s.Nombres, s.Apellidos, s.Telefono, $"0018{n:0000}", $"S-{n:000}"), Roles.Sereno);
        // El celular es el usuario y debe ser único: si el generador repitió uno, el segundo recibe el siguiente libre.
        var celulares = new HashSet<string>();
        foreach (var c in datos.Personas.Ciudadanos)
        {
            var alta = T(c.RegistradoEn);
            var celular = c.Telefono;
            while (!celulares.Add(celular))
                celular = (long.Parse(celular) + 1).ToString();
            celularDe[c.Id] = celular;
            var usuario = new Usuario
            {
                Id = Guid.CreateVersion7(alta),
                UserName = celular,
                PhoneNumber = celular,
                PhoneNumberConfirmed = true,
                Nombres = c.Nombres,
                Apellidos = c.Apellidos,
                Activo = true,
                CreadoEn = alta,
                TerminosVersion = VersionTerminos,
                TerminosAceptadosEn = alta,
                PinHash = hashPin,
            };
            Agregar(c.Id, usuario, Roles.Ciudadano);
            if (c.Perfil is { } p)
            {
                var perfil = PerfilCiudadano.Nuevo(usuario.Id);
                perfil.Actualizar(p.TipoDocumento, p.NumeroDocumento, p.FechaNacimiento, p.Direccion, p.ContactoNombre, p.ContactoParentesco,
                    p.ContactoTelefono, alta);
                db.PerfilesCiudadano.Add(perfil);
            }
        }

        Usuario Personal(string correo, string nombres, string apellidos, string? telefono, string dni, string codigo) => new()
        {
            Id = Guid.CreateVersion7(altaPersonal),
            UserName = correo,
            Email = correo,
            EmailConfirmed = true,
            Nombres = nombres,
            Apellidos = apellidos,
            PhoneNumber = telefono,
            Dni = dni,
            CodigoPersonal = codigo,
            Activo = true,
            CreadoEn = altaPersonal,
            PasswordHash = hashContrasena,
        };
    }

    // ── Acompañamientos ────────────────────────────────────────────────────────────────────────────────────────────────────

    private readonly Dictionary<string, List<ConfirmacionBienestar>> confirmaciones = [];

    private void CrearSesion(SesionDataset s)
    {
        var ciudadano = personas[s.Ciudadano].Id;
        var sesion = SesionAcompanamiento.Iniciar(ciudadano, Guid.NewGuid(), T(s.IniciadaEn));
        sesiones[s.Id] = sesion;
        db.SesionesAcompanamiento.Add(sesion);
        foreach (var p in s.Posiciones)
        {
            var en = T(p.En);
            if (PosicionAcompanamiento.Crear(sesion.Id, p.Latitud, p.Longitud, p.PrecisionM, en, en.AddSeconds(1)) is { } posicion)
                db.PosicionesAcompanamiento.Add(posicion);
        }
        if (s.Posiciones.Count > 0)
            db.Entry(sesion).Property(x => x.UltimoContactoEn).CurrentValue = T(s.Posiciones[^1].En).AddSeconds(1);

        // Confirmaciones de bienestar: el ciudadano responde «Estoy bien» salvo la que quedó sin respuesta y generó un aviso.
        var sinRespuesta = s.Avisos.Where(a => a.Regla == nameof(ReglaAviso.ConfirmacionSinRespuesta))
            .Select(a => s.ConfirmacionesBienestar.Where(c => c <= a.GeneradoEn).DefaultIfEmpty(a.GeneradoEn.AddMinutes(-2)).Max())
            .ToHashSet();
        var lista = new List<ConfirmacionBienestar>();
        foreach (var solicitada in s.ConfirmacionesBienestar.Concat(sinRespuesta).Distinct().Order())
        {
            var en = T(solicitada);
            if (!sesion.AdmitePosiciones(en))
                continue;
            var confirmacion = ConfirmacionBienestar.Solicitar(sesion, Operador(en), Guid.NewGuid(), en);
            confirmacion.RegistrarPresentacion(en.AddSeconds(azar.Next(2, 8)));
            if (!sinRespuesta.Contains(solicitada))
                confirmacion.Responder(RespuestaBienestar.EstoyBien, null, en.AddSeconds(azar.Next(10, 45)));
            lista.Add(confirmacion);
            db.ConfirmacionesBienestar.Add(confirmacion);
        }
        confirmaciones[s.Id] = lista;

        // El cierre: la mayoría de quienes finalizan avisan «Llegué»; el resto la cierra a mano o la deja vencer.
        if (s.Estado == nameof(EstadoSesion.Finalizada))
        {
            var cierre = T(s.CerradaEn);
            if (azar.NextDouble() < 0.7)
                db.EstadosInformados.Add(EstadoInformado.Registrar(sesion, Guid.NewGuid(), EstadoCiudadano.Llegue, null, cierre.AddSeconds(-1)));
            sesion.Finalizar(cierre);
        }
        else
            sesion.Vencer(T(s.VenceEn));
    }

    // Los avisos van después de los casos: el que terminó en un caso urgente se vincula con él.
    private void CrearAvisos(SesionDataset s)
    {
        var sesion = sesiones[s.Id];
        var pendientes = confirmaciones[s.Id].Where(c => c.Pendiente).OrderBy(c => c.SolicitadaEn).ToList();
        foreach (var a in s.Avisos)
        {
            var generado = T(a.GeneradoEn);
            AvisoAcompanamiento aviso;
            if (a.Regla == nameof(ReglaAviso.ConfirmacionSinRespuesta) && pendientes.LastOrDefault(c => c.SolicitadaEn <= generado) is { } confirmacion)
                aviso = AvisoAcompanamiento.ConfirmacionSinRespuesta(sesion, confirmacion, generado);
            else
            {
                var ultima = s.Posiciones.Where(p => p.En <= a.GeneradoEn).Select(p => (DateTimeOffset?)T(p.En)).LastOrDefault();
                aviso = AvisoAcompanamiento.UbicacionDesactualizada(sesion, ultima, generado);
            }
            var operador = Operador(generado);
            var resuelto = T(a.ResueltoEn);
            aviso.IniciarRevision(operador, generado.AddSeconds(azar.Next(10, 40)));
            db.ActuacionesAviso.Add(ActuacionAviso.Registrar(aviso, TipoActuacionAviso.Contacto,
                "Se llamó al ciudadano (dato ficticio).", operador, Guid.NewGuid(), resuelto.AddSeconds(-40)));
            db.ActuacionesAviso.Add(ActuacionAviso.Registrar(aviso, TipoActuacionAviso.Resultado, a.Actuacion, operador, Guid.NewGuid(),
                resuelto.AddSeconds(-5)));
            if (a.Caso is { } id && casos.TryGetValue(id, out var caso))
            {
                aviso.Vincular(caso.Id, operador, caso.RegistradoEn, "No se pudo confirmar que el ciudadano estuviera bien (dato ficticio).");
                db.ConservacionesRecorrido.Add(ConservacionRecorrido.Registrar(sesion.Id, caso.Id,
                    "Recorrido vinculado con un caso urgente (dato ficticio).", operador, resuelto));
            }
            aviso.Resolver(operador, resuelto);
            db.AvisosAcompanamiento.Add(aviso);
        }

        // Solo una confirmación puede quedar sin respuesta por sesión: las anteriores las contestó al resolverse su aviso.
        foreach (var confirmacion in pendientes.SkipLast(1))
            confirmacion.Responder(RespuestaBienestar.EstoyBien, null, confirmacion.PresentadaEn!.Value.AddMinutes(azar.Next(2, 6)));
    }

    // ── Casos ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private void CrearCaso(CasoDataset c)
    {
        Caso? caso = null;
        DetalleReporte? detalle = null;
        Asignacion? vigente = null;
        Tramo? tramo = null;
        string? publico = null;
        var intentos = new Queue<IntentoDataset>(c.IntentosContacto ?? []);

        Guid Actor(string id) => personas[id].Id;
        void Evento(TipoEventoCaso tipo, Guid actor, VisibilidadEvento visibilidad, DateTimeOffset en) =>
            db.EventosCaso.Add(EventoCaso.Registrar(caso!, tipo, actor, visibilidad, en));

        foreach (var e in c.Eventos)
        {
            var en = T(e.En);
            switch (e.Tipo)
            {
                case nameof(TipoEventoCaso.SosEmitido):
                    var posicion = c.Ubicacion is { } u ? Posicion.ComoActual(u.Latitud, u.Longitud, u.PrecisionM, en, en) : null;
                    caso = Caso.EmitirSos(Actor(c.Ciudadano!), Guid.NewGuid(), posicion, en);
                    Registrar(caso, c);
                    Evento(TipoEventoCaso.SosEmitido, caso.CiudadanoId!.Value, VisibilidadEvento.Publica, en);
                    break;

                case nameof(TipoEventoCaso.CasoUrgenteCreado):
                    var operadorUrgente = Actor(e.Actor);
                    caso = Caso.CrearDesdeAviso(Actor(c.Ciudadano!), Guid.NewGuid(), UltimaPosicion(c, en), en);
                    Registrar(caso, c);
                    Evento(TipoEventoCaso.CasoUrgenteCreado, operadorUrgente, VisibilidadEvento.Interna, en);
                    // Se clasifica como un SOS antes de asignarlo (DP19); el dataset no trae ese paso aparte.
                    if (c.Clasificacion is { } cu)
                    {
                        caso.Clasificar(cu.Categoria, cu.Prioridad, cu.Heridos, operadorUrgente, en.AddSeconds(10));
                        Evento(TipoEventoCaso.CasoClasificado, operadorUrgente, VisibilidadEvento.Interna, en.AddSeconds(10));
                    }
                    break;

                case nameof(TipoEventoCaso.ReporteRegistrado):
                    var r = c.Reporte!;
                    if (r.RegistradoPorOperador is { } registrador)
                    {
                        var operador = Actor(registrador);
                        caso = Caso.RegistrarReporteDeLaCentral(operador, Guid.NewGuid(), c.CategoriaInicial, en);
                        caso.Priorizar(c.Clasificacion!.Prioridad, c.Clasificacion.Heridos);
                        Registrar(caso, c);
                        detalle = Detalle(caso, c, r, operador);
                        db.DecisionesReporte.Add(DecisionReporte.Registrar(caso, TipoDecisionReporte.Admision,
                            Cad.Server.Modulos.Casos.Aplicacion.RegistrarReporte.MotivoAdmisionCentral, null, null, operador, en));
                        Evento(TipoEventoCaso.ReporteRegistrado, operador, VisibilidadEvento.Interna, en);
                        Evento(TipoEventoCaso.ReporteAdmitido, operador, VisibilidadEvento.Interna, en);
                    }
                    else
                    {
                        caso = Caso.RegistrarReporte(Actor(c.Ciudadano!), Guid.NewGuid(), c.CategoriaInicial, en);
                        Registrar(caso, c);
                        detalle = Detalle(caso, c, r, null);
                        // Fotos de muestra (fotos/*.jpg), distintas entre las de un mismo reporte.
                        var primera = int.Parse(c.Id.AsSpan(c.Id.IndexOf('-') + 1));
                        for (var n = 1; n <= r.Fotos; n++)
                            db.FotosReporte.Add(FotoReporte.Registrar(caso, n, fotos[(primera + n) % fotos.Count], en));
                        Evento(TipoEventoCaso.ReporteRegistrado, caso.CiudadanoId!.Value, VisibilidadEvento.Publica, en);
                    }
                    break;

                case nameof(TipoEventoCaso.IntentoContactoRegistrado):
                    var intento = intentos.Dequeue();
                    var resultado = Enum.Parse<ResultadoContacto>(intento.Resultado);
                    db.IntentosContacto.Add(IntentoContacto.Registrar(caso!, Actor(intento.Operador), Guid.NewGuid(), resultado, null, intento.Referencia, en));
                    Evento(TipoEventoCaso.IntentoContactoRegistrado, Actor(e.Actor), VisibilidadEvento.Interna, en);
                    break;

                case nameof(TipoEventoCaso.CasoClasificado):
                    var cl = c.Clasificacion!;
                    caso!.Clasificar(cl.Categoria, cl.Prioridad, cl.Heridos, Actor(e.Actor), en);
                    Evento(TipoEventoCaso.CasoClasificado, Actor(e.Actor), VisibilidadEvento.Interna, en);
                    break;

                case nameof(TipoEventoCaso.RevisionIniciada):
                    caso!.IniciarRevision();
                    Evento(TipoEventoCaso.RevisionIniciada, Actor(e.Actor), VisibilidadEvento.Publica, en);
                    break;

                case nameof(TipoEventoCaso.CategoriaCorregida):
                    var correccion = e.Correccion!;
                    caso!.CorregirCategoria(correccion.Nueva, Actor(e.Actor), en);
                    db.CorreccionesCategoria.Add(CorreccionCategoria.Registrar(caso.Id, correccion.Anterior, correccion.Nueva, Actor(e.Actor), en));
                    Evento(TipoEventoCaso.CategoriaCorregida, Actor(e.Actor), VisibilidadEvento.Interna, en);
                    break;

                case nameof(TipoEventoCaso.ReporteAdmitido) when e.Decision is null:
                    break; // El de un reporte de la central: ya se registró con él.

                case nameof(TipoEventoCaso.ReporteAdmitido):
                    caso!.Decidir(TipoDecisionReporte.Admision);
                    caso.Priorizar(c.Clasificacion!.Prioridad, c.Clasificacion.Heridos);
                    db.DecisionesReporte.Add(DecisionReporte.Registrar(caso, TipoDecisionReporte.Admision, e.Decision.Texto, null, null, Actor(e.Actor), en));
                    Evento(TipoEventoCaso.ReporteAdmitido, Actor(e.Actor), VisibilidadEvento.Publica, en);
                    break;

                case nameof(TipoEventoCaso.ReporteDerivado):
                    var derivacion = e.Decision!;
                    caso!.Decidir(TipoDecisionReporte.Derivacion);
                    db.DecisionesReporte.Add(DecisionReporte.Registrar(caso, TipoDecisionReporte.Derivacion, derivacion.Texto,
                        Catalogo.Texto(DestinosDerivacion.Todos, derivacion.Destino), derivacion.Accion, Actor(e.Actor), en, destinoCodigo: derivacion.Destino));
                    Evento(TipoEventoCaso.ReporteDerivado, Actor(e.Actor), VisibilidadEvento.Publica, en);
                    break;

                case nameof(TipoEventoCaso.ReporteDescartado):
                    var descarteReporte = e.Decision!;
                    caso!.Decidir(TipoDecisionReporte.Descarte);
                    db.DecisionesReporte.Add(DecisionReporte.Registrar(caso, TipoDecisionReporte.Descarte, descarteReporte.Texto, null, null, Actor(e.Actor), en,
                        motivoCodigo: descarteReporte.Motivo));
                    Evento(TipoEventoCaso.ReporteDescartado, Actor(e.Actor), VisibilidadEvento.Publica, en);
                    break;

                case nameof(TipoEventoCaso.SosCancelado):
                case nameof(TipoEventoCaso.ReporteRetirado):
                    var motivo = e.Cancelacion?.Motivo ?? (azar.NextDouble() < 0.6 ? MotivosCancelacion.YaNoLoNecesito : MotivosCancelacion.PorError);
                    caso!.Cancelar();
                    db.CancelacionesSos.Add(CancelacionSos.Registrar(caso, motivo, Catalogo.Texto(MotivosCancelacion.Todos, motivo)!, en));
                    Evento(Enum.Parse<TipoEventoCaso>(e.Tipo), caso.CiudadanoId!.Value, VisibilidadEvento.Publica, en);
                    break;

                case nameof(TipoEventoCaso.CasoDescartado):
                    var descarte = e.Descarte!;
                    var previo = caso!.Estado;
                    caso.Descartar();
                    if (vigente is not null)
                        TerminarAsignacion(en, cancelada: false);
                    db.DescartesCaso.Add(DescarteCaso.Registrar(caso, previo, Actor(e.Actor), descarte.Motivo,
                        descarte.Detalle ?? Catalogo.Texto(MotivosDescarte.Todos, descarte.Motivo)!, en));
                    Evento(TipoEventoCaso.CasoDescartado, Actor(e.Actor), VisibilidadEvento.Interna, en);
                    break;

                case nameof(TipoEventoCaso.CasoAsignado):
                    caso!.Asignar();
                    Asignar(Actor(e.Sereno!), Actor(e.Actor), en);
                    Evento(TipoEventoCaso.CasoAsignado, Actor(e.Actor), VisibilidadEvento.Interna, en);
                    break;

                case nameof(TipoEventoCaso.CasoReasignado):
                    var anterior = vigente!.SerenoId;
                    var nuevo = Actor(e.Sereno!);
                    caso!.Reasignar();
                    TerminarAsignacion(en, cancelada: true);
                    Asignar(nuevo, Actor(e.Actor), en);
                    db.Reasignaciones.Add(Reasignacion.Registrar(caso, anterior, nuevo, Actor(e.Actor), e.Motivo!,
                        Catalogo.Texto(MotivosReasignacion.Todos, e.Motivo)!, en));
                    Evento(TipoEventoCaso.CasoReasignado, Actor(e.Actor), VisibilidadEvento.Interna, en);
                    break;

                case nameof(TipoEventoCaso.DesplazamientoConfirmado):
                    caso!.ConfirmarDesplazamiento();
                    Evento(TipoEventoCaso.DesplazamientoConfirmado, vigente!.SerenoId, VisibilidadEvento.Interna, en);
                    break;

                case nameof(TipoEventoCaso.AtencionIniciada):
                    caso!.IniciarAtencion();
                    var (inicio, actor) = Inicio(caso, detalle, vigente!.SerenoId, e.Inicio!, en);
                    db.IniciosAtencion.Add(inicio);
                    tramo = tramo! with { Llegada = en, Lat = inicio.Latitud ?? tramo.Lat, Lon = inicio.Longitud ?? tramo.Lon };
                    Evento(TipoEventoCaso.AtencionIniciada, actor, VisibilidadEvento.Interna, en);
                    break;

                case nameof(TipoEventoCaso.CasoCerrado):
                    var cierre = e.Cierre!;
                    caso!.Cerrar();
                    var serenoCierre = vigente!.SerenoId;
                    TerminarAsignacion(en, cancelada: false);
                    var notas = cierre.Destino is { } destino ? $"Derivado a: {Catalogo.Texto(DestinosDerivacion.Todos, destino)}." : null;
                    db.CierresCaso.Add(CierreCaso.Registrar(caso, cierre.Categoria, cierre.Resultado, cierre.ResultadoPublico, notas, serenoCierre, en));
                    Evento(TipoEventoCaso.CasoCerrado, serenoCierre, VisibilidadEvento.Interna, en);
                    break;

                default:
                    throw new InvalidDataException($"{c.Id}: evento desconocido «{e.Tipo}».");
            }
            Notificar(en);
        }

        if (tramo is not null)
            Tramar(fin); // El caso sigue abierto al terminar el periodo: el sereno sigue en él.

        if (caso!.Estado.ToString() != c.Estado)
            discrepancias.Add($"{c.Id}: quedó «{caso.Estado}» y el dataset dice «{c.Estado}».");

        if (c.Evaluacion is { } ev && caso.Estado == EstadoCaso.Cerrada)
            db.EvaluacionesCaso.Add(EvaluacionCaso.Registrar(caso, Guid.NewGuid(), ev.Puntuacion, ev.Comentario, T(ev.EvaluadaEn), ev.Motivos));

        // HU31: cada cambio del estado público notifica al titular (lo que hace CadDbContext al guardar un cambio de estado).
        void Notificar(DateTimeOffset en)
        {
            if (caso!.CiudadanoId is null)
                return;
            var ahora = EstadoPublicoCaso.De(caso.Origen, caso.Estado);
            if (publico is not null && ahora != publico && ahora != EstadosPublicos.Cancelado)
            {
                var notificacion = NotificacionCiudadano.PorCambioDeEstado(caso, ahora, en);
                var leida = en.AddMinutes(Math.Exp(3 + azar.NextDouble() * 3)); // entre unos 20 minutos y unas 7 horas
                if (azar.NextDouble() < 0.85 && leida < fin)
                    notificacion.MarcarLeida(leida);
                db.NotificacionesCiudadano.Add(notificacion);
            }
            publico = ahora;
        }

        void Asignar(Guid sereno, Guid operador, DateTimeOffset en)
        {
            vigente = Asignacion.Registrar(caso!, sereno, operador, Guid.NewGuid(), en);
            db.Asignaciones.Add(vigente);
            db.EnviosPush.Add(EnvioPush.Registrar(sereno, ResultadoPush(), en.AddSeconds(1)));
            var (lat, lon) = Punto(caso!, detalle);
            tramo = new Tramo(en, null, fin, lat, lon);
        }

        void TerminarAsignacion(DateTimeOffset en, bool cancelada)
        {
            if (cancelada)
                vigente!.Cancelar(en);
            else
                vigente!.Finalizar(en);
            Tramar(en);
            vigente = null;
        }

        void Tramar(DateTimeOffset hasta)
        {
            var sereno = vigente!.SerenoId;
            if (!tramos.TryGetValue(sereno, out var lista))
                tramos[sereno] = lista = [];
            lista.Add(tramo! with { Hasta = hasta });
            tramo = null;
        }
    }

    private void Registrar(Caso caso, CasoDataset c)
    {
        casos[c.Id] = caso;
        db.Casos.Add(caso);
        // Marcas de la salud del sistema: cuándo apareció en la central y cuándo lo abrió un operador.
        if (c.PresentadoEnCentral is { } presentado)
            db.Entry(caso).Property(x => x.PresentadoEnCentralEn).CurrentValue = T(presentado);
        if (c.AbiertoEnCentral is { } abierto)
            db.Entry(caso).Property(x => x.AbiertoEnCentralEn).CurrentValue = T(abierto);
    }

    private DetalleReporte Detalle(Caso caso, CasoDataset c, ReporteDataset r, Guid? operador)
    {
        var u = r.Ubicacion;
        var detalle = DetalleReporte.Registrar(caso, c.CategoriaInicial, r.Descripcion, T(r.OcurridoAproximadamenteEn), u.Fuente,
            u.Latitud, u.Longitud, u.PrecisionM, u.Referencia, operador, r.Contacto?.Nombre, r.Contacto?.Telefono, r.Canal);
        db.DetallesReporte.Add(detalle);
        return detalle;
    }

    // La última posición del acompañamiento antes de crear el caso urgente, con su hora real (HU43-CA05).
    private (double, double, double, DateTimeOffset)? UltimaPosicion(CasoDataset c, DateTimeOffset en)
    {
        var sesion = datos.Sesiones.FirstOrDefault(s => s.Id == c.Acompanamiento);
        if (sesion?.Posiciones.LastOrDefault(p => T(p.En) <= en) is { } p)
            return (p.Latitud, p.Longitud, p.PrecisionM, T(p.En));
        return c.Ubicacion is { } u ? (u.Latitud, u.Longitud, u.PrecisionM, en) : null;
    }

    private static (double? Lat, double? Lon) Punto(Caso caso, DetalleReporte? detalle) =>
        caso.Origen == OrigenCaso.Reporte ? (detalle?.LatitudHecho, detalle?.LongitudHecho) : (caso.Latitud, caso.Longitud);

    // Dónde estaba el sereno al iniciar la atención, a la distancia del dataset del punto del caso, y si quedó dentro del rango.
    private (InicioAtencion Inicio, Guid Actor) Inicio(Caso caso, DetalleReporte? detalle, Guid sereno, InicioDataset i, DateTimeOffset en)
    {
        if (i.RegistradoPorCentral)
        {
            var operador = Operador(en);
            return (InicioAtencion.RegistrarPorCentral(caso, sereno, operador, "El sereno avisó por radio que llegó al lugar (dato ficticio).", en), operador);
        }
        var (lat, lon) = Punto(caso, detalle);
        if (lat is null || lon is null)
            return (InicioAtencion.Registrar(caso, sereno, null, null, null, null, null, null, en), sereno);
        var precisionCaso = caso.Origen == OrigenCaso.Reporte ? detalle?.PrecisionHechoMetros : caso.PrecisionMetros;
        var precisionSereno = (double)azar.Next(5, 19);
        var distancia = i.DistanciaM ?? azar.Next(10, 90);
        var (latS, lonS) = Patrullaje.Desplazar((lat.Value, lon.Value), distancia, azar.NextDouble() * 2 * Math.PI);
        var radio = RangoAtencion.Radio(precisionCaso, precisionSereno);
        var dentro = i.DentroDelRango ?? distancia <= radio;
        string? motivo = dentro ? null
            : new[] { MotivosInicioFueraDeRango.CiudadanoSeMovio, MotivosInicioFueraDeRango.UbicacionInexacta, MotivosInicioFueraDeRango.OtroPunto }[azar.Next(3)];
        return (InicioAtencion.Registrar(caso, sereno, (latS, lonS, precisionSereno), distancia, radio, dentro, motivo, null, en), sereno);
    }

    // Uno de los dos operadores del turno de esa hora (6–14, 14–22 y 22–6, como en el dataset).
    private Guid Operador(DateTimeOffset en)
    {
        var hora = (en - desplazamiento).ToOffset(TimeSpan.FromHours(-5)).Hour;
        var turno = hora is >= 6 and < 14 ? "Mañana" : hora is >= 14 and < 22 ? "Tarde" : "Noche";
        var delTurno = datos.Personas.Operadores.Where(o => o.Turno == turno).ToList();
        return personas[delTurno[azar.Next(delTurno.Count)].Id].Id;
    }

    private string ResultadoPush() => azar.NextDouble() switch
    {
        < 0.95 => nameof(Cad.Server.Compartido.ResultadoPush.Enviada),
        < 0.98 => nameof(Cad.Server.Compartido.ResultadoPush.NoEnviada),
        < 0.99 => nameof(Cad.Server.Compartido.ResultadoPush.TokenInvalido),
        _ => EnvioPush.SinDispositivo,
    };
}
