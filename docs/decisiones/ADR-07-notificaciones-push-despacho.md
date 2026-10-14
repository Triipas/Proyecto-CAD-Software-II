# ADR-07 — Notificación push del despacho al sereno (HU21-CA01)

- **Fecha:** 13/10/2026
- **Estado:** decidida por el Product Owner (planificación del Sprint 2, respuesta a la decisión E10).
- **Relación:** modifica la exclusión de «notificaciones push (FCM)» del Plan de Arquitectura (alcance excluido y alternativas descartadas) y matiza la «limitación de plataforma» del Plan Scrum (1.5). No modifica RNF11 ni la nota técnica de SPIKE01.

## Contexto

- HU21-CA01: «Dada una asignación válida, cuando el operador la registra, entonces el sereno asignado recibe la notificación».
- Propuesta inicial del equipo de desarrollo: aviso en tiempo real (SignalR) solo con la app abierta, porque la arquitectura excluía las notificaciones push y SPIKE01 (preliminar) mostró que, con la app en segundo plano, Android no garantiza ni la ubicación ni la red.
- Decisión del PO: el sereno debe recibir una **notificación push** al asignarle un caso, para atenderlo lo antes posible aunque no tenga la app abierta.

## Decisión

1. Al confirmarse una asignación (después del COMMIT, como los avisos de HU11), el servidor envía una notificación push al dispositivo del sereno asignado mediante **Firebase Cloud Messaging (FCM)**, en su plan gratuito.
2. Con la app abierta, el aviso llega además por el canal en tiempo real.
3. La notificación solo anuncia que hay un despacho y abre la app. **No lleva datos del caso**: los datos se consultan dentro de la app, con la sesión del sereno y los límites de DP06 (RNF12).
4. La asignación sigue siendo consultable al abrir la app: si la notificación no llega, el sereno ve igualmente su despacho.

## Consecuencias

- **No es una entrega garantizada:** FCM y Android pueden retrasar o descartar notificaciones (ahorro de batería, permiso de notificaciones denegado en Android 13 o superior, dispositivo sin servicios de Google). La interfaz no afirma que el sereno «ya fue avisado»; el operador ve el estado real del caso.
- **No cambia la condición validada de SPIKE01:** la transmisión de la ubicación del sereno (HU18) sigue requiriendo la app en primer plano y con conexión. Esta decisión solo afecta al aviso del despacho.
- **Requisitos previos del equipo** (el asistente no crea cuentas ni concede permisos): crear un proyecto de Firebase, registrar la app Android (`pe.cadmvp.movil`) y generar la credencial de servidor.
- **Secretos:** la credencial de servidor de FCM nunca entra al repositorio; se configura como variable de entorno en Render (como `Jwt__ClaveFirma`). La configuración de cliente de Firebase de la app tampoco se versiona; se documentará cómo obtenerla.
- **Datos personales y registros:** el token del dispositivo se trata como dato sensible: no se registra en los logs (se añadirá a la auditoría de registros, S2-T16).
- **Permisos de la app:** `POST_NOTIFICATIONS` (Android 13+), solicitado con una explicación previa, como el de ubicación en HU06.
- **Estimación:** puede requerir reestimar HU21 (3 SP). La reestimación corresponde al equipo; no se fija aquí.
- **Implementación:** bloque 7 del Sprint 2 (S2-T09). Hasta entonces no se añade ninguna dependencia de Firebase.
