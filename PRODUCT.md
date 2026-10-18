# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Cuatro roles, cada uno con su propia superficie:

- **Operador de central** (panel web, escritorio): recibe SOS y reportes en tiempo real, verifica y contacta al ciudadano, clasifica, asigna serenos, supervisa acompañamientos y consulta casos. Trabaja en turnos largos frente a la pantalla y bajo presión cuando entra un SOS.
- **Administrador** (panel web): crea, modifica y desactiva las cuentas institucionales de operadores y serenos. Uso poco frecuente.
- **Ciudadano** (app Android): emite un SOS con un toque, reporta incidencias no urgentes, activa un acompañamiento temporal de hasta 30 minutos, sigue sus casos y los evalúa. Puede estar asustado, en la calle y con una sola mano libre.
- **Sereno** (app Android): activa su estado operativo, recibe despachos, avanza el caso (en camino → en atención) y lo cierra con tipificación y resultado. Usa el teléfono en movimiento.

## Product Purpose

Sistema Integrado CAD: prototipo académico (Ingeniería de Software II, USMP) de alertas SOS georreferenciadas y despacho para la seguridad ciudadana de una municipalidad tipo B. Éxito: que el caso llegue a la central, se verifique y se despache a un sereno lo antes posible, y que el ciudadano sepa en todo momento qué pasa con su caso.

El rediseño se optimiza **por igual** para la demostración en la Sprint Review (docente y equipo) y para el uso real por operadores y ciudadanos.

## Operating Context

- Panel web (ASP.NET Core 10, Razor Pages) con tiempo real por SignalR y mapas Leaflet/OpenStreetMap; probado a 1366 y 1920 px y adaptable a 390 px.
- App .NET MAUI para Android (ciudadano y sereno) con ubicación, notificaciones push y servicio en primer plano durante el acompañamiento.
- Trabajo por sprints Scrum con historias de usuario HU01–HU43, trazabilidad por sprint en `docs/` y demostraciones guiadas (`docs/demo-sprint*.md`).
- Ubicación simulada en el centro de Lima; idioma es-PE; zona horaria de Lima.

## Capabilities and Constraints

- Flujos: SOS (emisión sin confirmación, verificación, intentos de contacto, clasificación, asignación, atención, cierre, cancelación, evaluación), reportes (recepción, revisión, corrección de categoría, admisión, derivación, descarte, atención), acompañamiento (sesión de 30 min, confirmación de bienestar, avisos, caso urgente, conservación del recorrido), consultas (casos con filtros, historial, mapa de concentración, tiempos) y cuentas institucionales.
- La app Android comparte la **identidad visual del panel web** (decisión del usuario): misma paleta, tipografía y componentes; solo se respetan los gestos y controles del sistema.
- Las pruebas del servidor verifican el HTML (atributos `data-*`, `id` y textos); los scripts de `wwwroot/js` dependen de los mismos atributos. El rediseño no puede romperlos.
- Sin dependencias de CDN: fuentes, iconos y librerías se alojan en el proyecto.
- Terminología fija: SOS, reporte, acompañamiento, sereno, central, operador, despacho, caso, tipificación, derivar, descartar.

## Brand Commitments

- Nombres: «Central CAD» (panel web) y «CAD Móvil» (app).
- Aviso obligatorio y visible: «MVP académico · datos simulados · no es un canal oficial de emergencias».
- El SOS se identifica siempre en rojo, con icono de sirena y el texto «SOS».
- Voz: español formal y claro, de tú con el ciudadano; precisa sin tecnicismos.

## Evidence on Hand

- Catálogo de capturas del estado actual: `docs/capturas/` (84 web, 67 móvil).
- Todos los datos son ficticios (Ana Ficticia, Olga Operadora Ficticia, Sara Serena Ficticia, Pedro Suplente Ficticio). No existen testimonios, métricas de uso real ni clientes: no se deben inventar.

## Product Principles

1. **Lo urgente se impone.** Un SOS pendiente nunca queda oculto ni compite en igualdad con lo demás.
2. **Siempre se sabe el siguiente paso.** Cada rol ve en qué estado está el caso y qué le toca hacer.
3. **Honestidad del prototipo.** Nada sugiere que es un canal oficial; los datos simulados se declaran.
4. **Privacidad visible.** Se dice cuándo y por qué se comparte la ubicación.

## Accessibility & Inclusion

WCAG 2.2 AA como mínimo: texto ≥ 4.5:1, bordes de controles y foco ≥ 3:1, objetivos táctiles de 44 px (web) y 48 dp (app), estados siempre con color + icono + texto, operable con teclado y lectores de pantalla, y respeto de la preferencia de movimiento reducido.
