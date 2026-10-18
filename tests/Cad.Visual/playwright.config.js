// Revisión visual del panel web (rediseño CAD v2): capturas por rol y ancho, axe-core y reglas del sistema de diseño.
import { defineConfig, devices } from '@playwright/test';
import { URL_BASE } from './soporte/entorno.js';

export default defineConfig({
  testDir: './pruebas',
  outputDir: './resultados',
  globalSetup: './preparar.js',
  // Un solo trabajador: todas las pantallas leen el mismo escenario sembrado.
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 60_000,
  reporter: [['list'], ['html', { outputFolder: 'informe', open: 'never' }]],
  snapshotPathTemplate: '{testDir}/referencias/{projectName}/{arg}{ext}',
  expect: {
    toHaveScreenshot: {
      animations: 'disabled',
      caret: 'hide',
      // Tolerancia mínima para el suavizado de fuentes entre ejecuciones.
      maxDiffPixelRatio: 0.002,
    },
  },
  use: {
    baseURL: URL_BASE,
    locale: 'es-PE',
    timezoneId: 'America/Lima',
    colorScheme: 'light',
    reducedMotion: 'reduce',
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'escritorio-1366', use: { ...devices['Desktop Chrome'], viewport: { width: 1366, height: 768 } } },
    { name: 'escritorio-1920', use: { ...devices['Desktop Chrome'], viewport: { width: 1920, height: 1080 } } },
    { name: 'movil-390', use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true } },
  ],
});
