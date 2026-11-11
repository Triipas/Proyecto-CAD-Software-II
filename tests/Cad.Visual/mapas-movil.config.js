import { defineConfig } from '@playwright/test';

// El mapa que lleva la app se verifica con su Leaflet real, sin servidor ni base de datos.
export default defineConfig({
  testDir: './pruebas-movil',
  outputDir: './resultados-movil',
  workers: 1,
  reporter: 'list',
  use: { viewport: { width: 390, height: 500 }, reducedMotion: 'no-preference' },
});
