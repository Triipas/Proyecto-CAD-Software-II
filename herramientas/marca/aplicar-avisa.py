"""Genera la marca Avisa elegida (Aviso urgente) para web y MAUI.

Uso: python herramientas/marca/aplicar-avisa.py
Los SVG contienen geometría simple y Public Sans convertido a trazados.
No usa el servicio de imágenes ni modifica la configuración de la aplicación.
"""
from html import escape
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parents[2]
WEB = ROOT / "src/Cad.Server/wwwroot/img/marca"
MOBILE = ROOT / "src/Cad.Movil/Resources"
CRIMSON, SIGNAL, NAVY, WHITE = "#B51F3B", "#EF3340", "#1B2437", "#FFFFFF"

# Globo protector, exclamación calada y dos señales salientes. Cuadrícula de 128.
BUBBLE = "M34 28H77Q83 28 88 33L99 44Q104 49 104 56V85Q104 106 83 106H43L17 121Q14 123 15 119L20 102Q8 97 8 81V54Q8 28 34 28Z"
ALERT = "M47 43H62Q67 43 66 48L60 76Q59 80 55 80H48Q44 80 44 76L44 48Q44 43 47 43Z"
DOT = "M60 92A7 7 0 1 1 46 92A7 7 0 1 1 60 92Z"


def number(value):
    return f"{value:.3f}".rstrip("0").rstrip(".") or "0"


def symbol(ink=CRIMSON, signal=SIGNAL):
    return (
        f'<path fill="{ink}" fill-rule="evenodd" d="{BUBBLE}{ALERT}{DOT}"/>'
        f'<g fill="{signal}"><rect x="96" y="3" width="11" height="27" rx="3" transform="rotate(32 101.5 16.5)"/>'
        '<rect x="113" y="17" width="11" height="27" rx="3" transform="rotate(38 118.5 30.5)"/></g>'
    )


def place(body, x, y, size):
    return f'<g transform="translate({number(x)} {number(y)}) scale({number(size / 128)})">{body}</g>'


def svg(width, height, body, title):
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{number(width)}" height="{number(height)}" '
        f'viewBox="0 0 {number(width)} {number(height)}" role="img" aria-label="{escape(title, quote=True)}">'
        f'<title>{escape(title)}</title>{body}</svg>\n'
    )


def wordmark():
    font = TTFont(MOBILE / "Fonts/PublicSans-Bold.ttf")
    glyphs, cmap = font.getGlyphSet(), font.getBestCmap()
    scale = 78 / font["OS/2"].sCapHeight
    position, paths = 0, []
    kerning = {("A", "v"): -0.055, ("v", "i"): -0.015, ("s", "a"): -0.02}
    for index, char in enumerate("Avisa"):
        glyph = cmap[ord(char)]
        pen = SVGPathPen(glyphs, number)
        glyphs[glyph].draw(TransformPen(pen, (scale, 0, 0, -scale, position, 0)))
        paths.append(pen.getCommands())
        position += font["hmtx"][glyph][0] * scale
        if index < 4:
            position += kerning.get((char, "Avisa"[index + 1]), -0.012) * font["head"].unitsPerEm * scale
    return "".join(paths), position


def main():
    WEB.mkdir(parents=True, exist_ok=True)
    letters, width = wordmark()
    for name, ink, accent in [("claro", NAVY, CRIMSON), ("oscuro", WHITE, WHITE)]:
        body = symbol(accent) + f'<path fill="{ink}" transform="translate(144 107)" d="{letters}"/>'
        (WEB / f"avisa-logo-{name}.svg").write_text(svg(149 + width, 128, body, "Avisa"), encoding="utf-8")
    (WEB / "avisa-simbolo.svg").write_text(svg(128, 128, symbol(), "Avisa"), encoding="utf-8")
    (WEB / "avisa-simbolo-blanco.svg").write_text(svg(128, 128, symbol(WHITE), "Avisa"), encoding="utf-8")
    icon = '<rect width="128" height="128" rx="27" fill="' + CRIMSON + '"/>' + place(symbol(WHITE, WHITE), 17, 13, 94)
    (WEB / "avisa-icono.svg").write_text(svg(128, 128, icon, "Avisa, icono de aplicación"), encoding="utf-8")
    # El favicon no tiene texto: conserva el aviso y la silueta reconocible a 16 px.
    favicon = '<rect width="32" height="32" rx="7" fill="' + CRIMSON + '"/>' + place(symbol(WHITE, WHITE), 3, 2, 26)
    (ROOT / "src/Cad.Server/wwwroot/favicon.svg").write_text(svg(32, 32, favicon, "Avisa"), encoding="utf-8")
    for source, destination in [("avisa-logo-oscuro.svg", "avisa_logo_blanco.svg"), ("avisa-simbolo.svg", "avisa_simbolo.svg")]:
        (MOBILE / "Images" / destination).write_text((WEB / source).read_text(encoding="utf-8"), encoding="utf-8")
    # El ForegroundScale=0.65 del proyecto reduce este primer plano para la máscara de Android.
    background = svg(456, 456, f'<rect width="456" height="456" fill="{CRIMSON}"/>', "Avisa, fondo del icono")
    foreground = svg(456, 456, place(symbol(WHITE, WHITE), 64, 54, 328), "Avisa, primer plano del icono")
    splash = svg(456, 456, place(symbol(WHITE, WHITE), 96, 84, 264), "Avisa")
    (MOBILE / "AppIcon/appicon.svg").write_text(background, encoding="utf-8")
    (MOBILE / "AppIcon/appiconfg.svg").write_text(foreground, encoding="utf-8")
    (MOBILE / "Splash/splash.svg").write_text(splash, encoding="utf-8")
    # Android necesita una silueta monocroma para sus notificaciones, no el icono con fondo.
    notification = (
        '<vector xmlns:android="http://schemas.android.com/apk/res/android" android:width="24dp" android:height="24dp" '
        'android:viewportWidth="128" android:viewportHeight="128">'
        f'<path android:fillColor="#FFFFFFFF" android:fillType="evenOdd" android:pathData="{BUBBLE}{ALERT}{DOT}"/>'
        '<group android:rotation="32" android:pivotX="101.5" android:pivotY="16.5">'
        '<path android:fillColor="#FFFFFFFF" android:pathData="M99 3H104Q107 3 107 6V27Q107 30 104 30H99Q96 30 96 27V6Q96 3 99 3Z"/></group>'
        '<group android:rotation="38" android:pivotX="118.5" android:pivotY="30.5">'
        '<path android:fillColor="#FFFFFFFF" android:pathData="M116 17H121Q124 17 124 20V41Q124 44 121 44H116Q113 44 113 41V20Q113 17 116 17Z"/></group>'
        '</vector>\n'
    )
    drawable = ROOT / "src/Cad.Movil/Platforms/Android/Resources/drawable"
    drawable.mkdir(parents=True, exist_ok=True)
    (drawable / "avisa_notificacion.xml").write_text(notification, encoding="utf-8")
    print("Avisa: originales web y MAUI, favicon, icono adaptativo, splash y notificación generados.")


if __name__ == "__main__":
    main()
