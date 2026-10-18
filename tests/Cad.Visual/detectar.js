// Ejecuta el detector de la skill impeccable sobre las hojas de estilo del panel y el HTML renderizado
// que guardó la última revisión (.estado/html). Si la skill no está instalada, lo indica y termina sin error.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { ESTADO, RAIZ_REPO } from './soporte/entorno.js';

const carpeta = join(homedir(), '.claude', 'skills', 'impeccable', 'scripts');
const lanzador = join(carpeta, process.platform === 'win32' ? 'impeccable.cmd' : 'impeccable');
if (!existsSync(lanzador)) {
  console.log('No se encontró la skill impeccable en ~/.claude/skills: se omite el detector.');
  process.exit(0);
}

const objetivos = [join(RAIZ_REPO, 'src', 'Cad.Server', 'wwwroot', 'css')];
const html = join(ESTADO, 'html');
if (existsSync(html)) objetivos.push(html);
else console.log('Aún no hay HTML renderizado: ejecuta antes `npm run revisar`.');

// En Windows el lanzador es un .cmd: se ejecuta a través de cmd.exe en lugar de con shell: true.
const [programa, argumentos] = process.platform === 'win32'
  ? ['cmd.exe', ['/d', '/c', lanzador, 'detect', '--json', ...objetivos]]
  : [lanzador, ['detect', '--json', ...objetivos]];
let salida;
try {
  salida = execFileSync(programa, argumentos, { cwd: RAIZ_REPO, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
} catch (error) {
  // El detector termina con código distinto de 0 cuando encuentra problemas; su JSON sigue en stdout.
  salida = error.stdout;
  if (!salida) throw error;
}

mkdirSync(ESTADO, { recursive: true });
writeFileSync(join(ESTADO, 'detector.json'), salida);
const resultado = JSON.parse(salida);
const hallazgos = Array.isArray(resultado) ? resultado : resultado.findings ?? [];
console.log(`Detector impeccable: ${hallazgos.length} hallazgos (detalle en .estado/detector.json).`);
const porRegla = new Map();
for (const h of hallazgos) {
  const regla = h.rule ?? h.id ?? h.antipattern ?? 'otro';
  const lugar = `${String(h.file ?? h.target ?? '').replace(RAIZ_REPO, '').replace(/^[\\/]/, '')}${h.line ? ':' + h.line : ''}`;
  const grupo = porRegla.get(regla) ?? { mensaje: h.message ?? h.description ?? '', lugares: new Set() };
  grupo.lugares.add(lugar);
  porRegla.set(regla, grupo);
}
for (const [regla, { mensaje, lugares }] of [...porRegla].sort((a, b) => b[1].lugares.size - a[1].lugares.size)) {
  console.log(`\n[${regla}] ${lugares.size} lugar(es) — ${mensaje.split('. ')[0]}.`);
  for (const lugar of [...lugares].slice(0, 6)) console.log(`  · ${lugar}`);
  if (lugares.size > 6) console.log(`  · … y ${lugares.size - 6} más`);
}
