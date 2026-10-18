# Revisión visual del panel (Cad.Visual)

Revisión automatizada del panel web para el rediseño CAD v2 ([plan](../../docs/plan-rediseno.md)). En cada ejecución:

1. **Borra y recrea** la base desechable `cad_visual` (nunca toca `cad` ni `cad_pruebas`).
2. **Arranca un servidor aislado** en `http://localhost:5240` con secretos generados en el momento (JWT y administrador).
3. **Siembra un escenario ficticio**: Olga Operadora, Sara Serena y Pedro Suplente creados por el administrador desde el panel; Ana Ficticia con un SOS cerrado de principio a fin, un SOS pendiente y tres reportes (admitido, en revisión y recibido); Beto Ficticio con un acompañamiento activo.
4. **Recorre 18 pantallas** del administrador y del operador a **1366, 1920 y 390 px** y ejecuta tres comprobaciones:

| Prueba | Qué verifica | Falla si… |
|---|---|---|
| `recorridos.spec.js` | Captura de página completa contra la referencia aprobada (`pruebas/referencias/`) | Cambia algún píxel fuera de la tolerancia |
| `accesibilidad.spec.js` | axe-core, WCAG 2.0/2.1/2.2 A y AA (a 1366 y 390 px) | Hay una infracción que no figura en `deudas.js` |
| `sistema.spec.js` | Public Sans cargada, sin desplazamiento horizontal, objetivos ≥ 24 px, sin animaciones con movimiento reducido | Se incumple alguna; además **anota** controles < 44 px y colores fuera de `tokens.css` |

Para que la comparación sea estable, las teselas de OpenStreetMap se sustituyen por una imagen vacía, y las horas, fechas, duraciones e identificadores se reemplazan por valores fijos antes de capturar.

## Requisitos

- PostgreSQL local levantado (`docker compose up -d`) y el `.env` de la raíz, el mismo que usan las pruebas.
- Node 20 o superior. La primera vez: `npm install` y `npx playwright install chromium` en esta carpeta.
- El puerto 5240 libre (o cambia el puerto con `CAD_VISUAL_PUERTO`).

## Comandos

Desde `tests/Cad.Visual`:

```bash
npm run revisar
```
Ejecuta la revisión completa: unas 160 pruebas en 2–3 minutos.

```bash
npm run informe
```
Abre el informe HTML, con capturas, diferencias, resultados de axe y anotaciones.

```bash
npm run aprobar
```
Acepta como nueva referencia las capturas actuales. Úsalo solo después de revisar las diferencias en el informe.

```bash
npm run detectar
```
Ejecuta el detector de la skill `impeccable` sobre `panel.css`, `tokens.css` y el HTML de la última revisión.

Para una sola pantalla o un solo ancho: `npx playwright test -g central --project=escritorio-1366`.

## Cómo se usa durante el rediseño

1. Antes de tocar una vista, `npm run revisar` debe pasar: la referencia es el estado aprobado.
2. Tras el cambio, `npm run revisar` falla en las capturas de esa vista: en `npm run informe` se comparan antes, después y diferencia.
3. Si el cambio es el buscado, `npm run aprobar` y se confirman las referencias nuevas junto con el código.
4. Al corregir una deuda de `pruebas/deudas.js`, se borra de la lista: si reaparece, la revisión falla.

## Archivos

- `preparar.js`: base desechable, servidor y siembra; devuelve el cierre del servidor.
- `soporte/`: entorno y conexión, servidor, semilla y ayudantes de página.
- `pruebas/pantallas.js`: catálogo de pantallas. Añade aquí las vistas nuevas.
- `pruebas/deudas.js`: deudas conocidas del diseño anterior, con su motivo y la fase que las corrige.
- `.estado/`, `informe/`, `resultados/`: generados en cada ejecución e ignorados por git. `.estado/` guarda las credenciales generadas de esa ejecución.

La app MAUI queda fuera: Playwright solo maneja navegadores.
