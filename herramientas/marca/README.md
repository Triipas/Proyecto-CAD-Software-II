# Marca Avisa

Identidad aplicada: **Aviso urgente**, el globo con exclamación y dos señales salientes. Nombre de la app: **Avisa**. Panel: **Avisa Central**. Eslogan: **Seguridad ciudadana a un toque.**

La marca combina carmesí `#B51F3B`, rojo señal `#EF3340`, tinta `#1B2437` y blanco. Los colores operativos de los SOS, reportes y acompañamientos conservan su significado.

## Originales de producción

- `src/Cad.Server/wwwroot/img/marca/`: firma para fondos claros y oscuros, símbolos e icono.
- `src/Cad.Server/wwwroot/favicon.svg`: pestaña del navegador.
- `src/Cad.Movil/Resources/Images/avisa_*.svg`: bienvenida, ingreso institucional e inicio ciudadano.
- `src/Cad.Movil/Resources/AppIcon/`: fondo y primer plano del icono Android; `ForegroundScale="0.65"` en el proyecto mantiene el símbolo dentro de la máscara adaptativa.
- `src/Cad.Movil/Resources/Splash/splash.svg`: arranque Android sobre carmesí.
- `src/Cad.Movil/Platforms/Android/Resources/drawable/avisa_notificacion.xml`: silueta monocroma de notificaciones locales y FCM.

Se regeneran desde la raíz con:

```powershell
python herramientas/marca/aplicar-avisa.py
```

Requiere Python con `fontTools` y las fuentes Public Sans del proyecto (OFL). Las letras del SVG se convierten a trazados: no hay fuentes externas, mapas de bits incrustados, filtros ni máscaras.

La fuente de los recursos que consume el sistema es `aplicar-avisa.py`. Las exploraciones gráficas anteriores y las capturas de validación se conservan como material local y no forman parte de los recursos publicados.

Se mantiene `pe.cadmvp.movil` como identificador Android para permitir actualizar la app instalada conservando sus datos al firmar con la misma clave. El nombre que ve la persona es Avisa; los espacios de nombres, rutas y claves técnicas CAD siguen siendo compatibles.

## Compilación y pruebas

```powershell
dotnet test tests/Cad.Movil.Tests/Cad.Movil.Tests.csproj
dotnet build src/Cad.Movil/Cad.Movil.csproj -f net10.0-android -t:Rebuild -p:EmbedAssembliesIntoApk=true -m:1
```

La compilación Android requiere la carga de trabajo `maui-android` y produce el APK en `src/Cad.Movil/bin/Debug/net10.0-android/`. La reconstrucción limpia evita reutilizar bibliotecas de una arquitectura de una compilación anterior.

Los mapas HTML de la app se prueban con `npm run revisar:mapas-movil` en `tests/Cad.Visual`, tras `npm ci` y `npx playwright install chromium`; no requieren servidor ni base de datos.

Los APK son artefactos locales, no se versionan. Una instalación anterior firmada con otra clave no admite una actualización directa: se debe compilar y firmar con la clave original para conservar esa instalación.
