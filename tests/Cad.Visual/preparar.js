// Preparación global: base desechable, servidor aislado y escenario sembrado. Devuelve el cierre.
import { mkdirSync, writeFileSync } from 'node:fs';
import { chromium } from '@playwright/test';
import { ARCHIVO_SEMILLA, ESTADO } from './soporte/entorno.js';
import { arrancarServidor, compilarServidor, generarSecretos, recrearBaseDatos } from './soporte/servidor.js';
import { sembrar } from './soporte/semilla.js';

export default async function preparar() {
  mkdirSync(ESTADO, { recursive: true });
  await recrearBaseDatos();
  compilarServidor();
  const secretos = generarSecretos();
  const servidor = await arrancarServidor(secretos);

  try {
    const navegador = await chromium.launch();
    try {
      const semilla = await sembrar(navegador, secretos);
      writeFileSync(ARCHIVO_SEMILLA, JSON.stringify(semilla, null, 2));
    } finally {
      await navegador.close();
    }
  } catch (error) {
    servidor.kill();
    throw error;
  }

  return async () => {
    servidor.kill();
  };
}
