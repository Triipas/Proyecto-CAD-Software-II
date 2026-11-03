"""Dataset simulado de 4000 casos en José Luis Bustamante y Rivero (Arequipa) para la demostración del Sistema Integrado CAD.

Todos los datos son ficticios, pero sus proporciones salen de fuentes reales (ver README.md): la victimización por tipo de
delito del INEI (ENAPRES), la mezcla de intervenciones de un serenazgo municipal, la población del distrito (censo 2017) y
su geografía real de OpenStreetMap (límite, urbanizaciones, avenidas, mercados, paraderos, parques, colegios).

Uso:  python generar.py            (escribe salida/; misma semilla, mismo resultado)
Solo usa la biblioteca estándar de Python 3.10+.
"""

import csv
import json
import math
import random
import statistics
from collections import Counter, defaultdict
from datetime import datetime, timedelta, timezone
from pathlib import Path

RAIZ = Path(__file__).parent
FUENTES = RAIZ / "fuentes"
SALIDA = RAIZ / "salida"

SEMILLA = 20261027
rng = random.Random(SEMILLA)

LIMA = timezone(timedelta(hours=-5))
# Fin del periodo simulado. Al cargar, todas las fechas se desplazan para que este momento coincida con «ahora» (README).
FIN = datetime(2026, 11, 30, 20, 0, tzinfo=LIMA)
DIAS = 91  # 13 semanas: alcanza para «7 días», «30 días», el periodo anterior y las cohortes semanales.
INICIO = FIN - timedelta(days=DIAS)
TOTAL_CASOS = 4000  # unos 44 por día: el orden de las intervenciones de un serenazgo de un distrito parecido (README)

# ───────────────────────────── Catálogos del sistema (mismos códigos que Cad.Contratos) ─────────────────────────────
CATEGORIAS = {
    "violencia": ("0199e2b0-0000-7000-8000-000000000001", "Violencia familiar"),
    "robo": ("0199e2b0-0000-7000-8000-000000000002", "Robo o hurto"),
    "drogas": ("0199e2b0-0000-7000-8000-000000000003", "Consumo o venta de drogas"),
    "otros": ("0199e2b0-0000-7000-8000-000000000004", "Otros"),
    "acoso": ("0199e2b0-0000-7000-8000-000000000005", "Acoso en la vía pública"),
    "extorsion": ("0199e2b0-0000-7000-8000-000000000006", "Extorsión o amenazas"),
    "alcohol": ("0199e2b0-0000-7000-8000-000000000007", "Consumo de alcohol en la vía pública"),
    "pelea": ("0199e2b0-0000-7000-8000-000000000008", "Peleas o riñas"),
    "sospechoso": ("0199e2b0-0000-7000-8000-000000000009", "Persona o vehículo sospechoso"),
    "vandalismo": ("0199e2b0-0000-7000-8000-000000000010", "Daños o vandalismo"),
    "accidente": ("0199e2b0-0000-7000-8000-000000000011", "Accidente de tránsito"),
    "ruido": ("0199e2b0-0000-7000-8000-000000000012", "Ruidos molestos o escándalo"),
    "riesgo": ("0199e2b0-0000-7000-8000-000000000013", "Persona en riesgo"),
}

# ───────────────────────────── Supuestos (cada uno con su fuente o su razón en el README) ─────────────────────────────
# Origen: el SOS es para lo urgente; el reporte, para lo demás (y la central registra los que llegan por teléfono).
PROPORCION_SOS = 0.30
PROPORCION_REPORTE_DE_LA_CENTRAL = 0.28  # de los reportes: llamadas, presencial, radio o redes

# Reportes: mezcla de las intervenciones del serenazgo (licor, ruidos, sospechosos, drogas, accidentes) y de lo que el
# ciudadano sufre (INEI: robo 12,3; intento 5,4; vehículo 4,0; amenazas 1,9; maltrato y ofensa sexual 1,6; extorsión 1,2).
PESOS_REPORTE = {
    "robo": 21, "alcohol": 14, "sospechoso": 12, "ruido": 11, "drogas": 8, "pelea": 6, "accidente": 6,
    "vandalismo": 5, "acoso": 4, "violencia": 4, "extorsion": 3, "riesgo": 3, "otros": 3,
}
# SOS: lo que pone a alguien en peligro ahora (la categoría la fija el operador al clasificar).
PESOS_SOS = {
    "robo": 36, "violencia": 17, "pelea": 11, "acoso": 9, "riesgo": 7, "accidente": 6, "extorsion": 5,
    "sospechoso": 4, "drogas": 2, "otros": 3,
}

# Dónde ocurre cada categoría: mezcla de tipos de lugar (OpenStreetMap) y su peso.
LUGARES_POR_CATEGORIA = {
    "robo": {"mercado": 30, "paradero": 25, "avenida": 20, "comercio": 15, "residencial": 10},
    "violencia": {"residencial": 100},
    "drogas": {"parque": 60, "residencial": 25, "avenida": 15},
    "acoso": {"paradero": 35, "colegio": 25, "avenida": 25, "parque": 15},
    "extorsion": {"comercio": 50, "mercado": 40, "residencial": 10},
    "alcohol": {"parque": 50, "noche": 30, "residencial": 20},
    "pelea": {"noche": 50, "parque": 20, "avenida": 20, "mercado": 10},
    "sospechoso": {"residencial": 60, "parque": 20, "colegio": 20},
    "vandalismo": {"parque": 50, "residencial": 30, "colegio": 20},
    "accidente": {"avenida": 85, "comercio": 15},
    "ruido": {"residencial": 60, "noche": 40},
    "riesgo": {"mercado": 30, "parque": 30, "avenida": 20, "residencial": 20},
    "otros": {"residencial": 40, "avenida": 30, "parque": 30},
}

# Horas pico de cada categoría (hora, peso) y cuánto sube el fin de semana (viernes y sábado; domingo a medias).
HORAS = {
    "robo": ([(8, 0.6), (13, 0.5), (19.5, 1.0), (22, 0.6)], 1.15),
    "violencia": ([(21, 1.0), (23.5, 0.8), (14, 0.3)], 1.4),
    "drogas": ([(21, 1.0), (1, 0.6)], 1.3),
    "acoso": ([(7.5, 0.8), (13, 0.6), (19, 1.0)], 1.1),
    "extorsion": ([(10, 1.0), (16, 0.6)], 0.8),
    "alcohol": ([(21, 1.0), (0.5, 0.9)], 2.0),
    "pelea": ([(23, 1.0), (2, 0.8)], 1.8),
    "sospechoso": ([(22, 1.0), (3, 0.6), (15, 0.3)], 1.0),
    "vandalismo": ([(1, 1.0), (22, 0.6)], 1.4),
    "accidente": ([(8, 1.0), (13, 0.6), (18.5, 1.0)], 0.9),
    "ruido": ([(23, 1.0), (1.5, 0.9)], 2.2),
    "riesgo": ([(11, 1.0), (17, 0.8)], 1.0),
    "otros": ([(12, 1.0), (18, 0.8)], 1.0),
}

# Personas. Población 2017 ajustada: 84 251 (INEI vía citypopulation.de); una app municipal nueva llega a una fracción.
CIUDADANOS = 1300
SERENOS = 36  # 12 por turno, 3 por sector; estimación (no hay cifra pública del distrito, ver README)
OPERADORES = 6  # 2 por turno
SESIONES_ACOMPANAMIENTO = 360
URGENTES_DE_ACOMPANAMIENTO = 24  # sesiones que terminan en un caso urgente

TURNOS = {"Mañana": (6, 14), "Tarde": (14, 22), "Noche": (22, 6)}

# ───────────────────────────── Geografía ─────────────────────────────
limite = json.loads((FUENTES / "limite-jlbyr.geojson").read_text(encoding="utf-8"))
ANILLO = limite["coordinates"][0]  # [lon, lat]
lugares = json.loads((FUENTES / "lugares.json").read_text(encoding="utf-8"))
vias = json.loads((FUENTES / "vias.json").read_text(encoding="utf-8"))


def dentro(lat, lon):
    """Punto dentro del límite del distrito (ray casting)."""
    adentro = False
    j = len(ANILLO) - 1
    for i in range(len(ANILLO)):
        xi, yi = ANILLO[i]
        xj, yj = ANILLO[j]
        if (yi > lat) != (yj > lat) and lon < (xj - xi) * (lat - yi) / (yj - yi) + xi:
            adentro = not adentro
        j = i
    return adentro


CENTRO = (statistics.mean(p[1] for p in ANILLO), statistics.mean(p[0] for p in ANILLO))


def metros(a, b):
    dlat = (a[0] - b[0]) * 111_320
    dlon = (a[1] - b[1]) * 111_320 * math.cos(math.radians(a[0]))
    return math.hypot(dlat, dlon)


# Sectores de patrullaje: cuatro cuadrantes alrededor del centro del distrito.
def sector_de(lat, lon):
    angulo = math.degrees(math.atan2(lat - CENTRO[0], lon - CENTRO[1]))
    return 1 if 45 <= angulo < 135 else 2 if -45 <= angulo < 45 else 3 if -135 <= angulo < -45 else 4


TIPOS_LUGAR = {
    "mercado": {"marketplace"},
    "paradero": {"bus_stop"},
    "noche": {"bar", "pub", "nightclub", "restaurant", "fast_food", "alcohol"},
    "parque": {"park", "sports_centre", "stadium"},
    "colegio": {"school", "university"},
    "comercio": {"bank", "convenience", "supermarket", "mall", "fuel"},
}
por_tipo = {k: [p for p in lugares if p["tipo"] in v and dentro(p["lat"], p["lon"])] for k, v in TIPOS_LUGAR.items()}
# El mercado y la plataforma Andrés Avelino Cáceres son el gran foco comercial del distrito: pesan más.
pesos_mercado = [6 if p["nombre"] and "Avelino" in p["nombre"] else 1 for p in por_tipo["mercado"]]

puntos_avenida = [(lat, lon, v["nombre"]) for v in vias if v["tipo"] in ("primary", "secondary", "tertiary") for lat, lon in v["puntos"] if dentro(lat, lon)]
puntos_residencial = [(lat, lon, v["nombre"]) for v in vias if v["tipo"] == "residential" for lat, lon in v["puntos"] if dentro(lat, lon)]
todas_las_vias = [(lat, lon, v["nombre"]) for v in vias for lat, lon in v["puntos"]]
urbanizaciones = [p for p in lugares if p["tipo"] in ("neighbourhood", "quarter", "suburb") and p["nombre"] and dentro(p["lat"], p["lon"])]


def via_cercana(lat, lon):
    return min(todas_las_vias, key=lambda v: (v[0] - lat) ** 2 + (v[1] - lon) ** 2)[2]


def urbanizacion_cercana(lat, lon):
    return min(urbanizaciones, key=lambda u: (u["lat"] - lat) ** 2 + (u["lon"] - lon) ** 2)["nombre"]


def desplazar(lat, lon, sigma_m):
    for _ in range(30):
        nlat = lat + rng.gauss(0, sigma_m) / 111_320
        nlon = lon + rng.gauss(0, sigma_m) / (111_320 * math.cos(math.radians(lat)))
        if dentro(nlat, nlon):
            return nlat, nlon
    return lat, lon


def elegir(pesos):
    claves = list(pesos)
    return rng.choices(claves, weights=[pesos[k] for k in claves])[0]


def ubicar(categoria):
    """Un punto del hecho: cerca de un lugar real del tipo que corresponde a la categoría, y una referencia en palabras."""
    tipo = elegir(LUGARES_POR_CATEGORIA[categoria])
    if tipo == "avenida":
        lat, lon, via = rng.choice(puntos_avenida)
        lat, lon = desplazar(lat, lon, 25)
        return lat, lon, f"{via}, cuadra {rng.randint(1, 12)}", tipo
    if tipo == "residencial":
        lat, lon, via = rng.choice(puntos_residencial)
        lat, lon = desplazar(lat, lon, 30)
        return lat, lon, f"{via}, {urbanizacion_cercana(lat, lon)}", tipo
    candidatos = por_tipo[tipo]
    lugar = rng.choices(candidatos, weights=pesos_mercado)[0] if tipo == "mercado" else rng.choice(candidatos)
    lat, lon = desplazar(lugar["lat"], lugar["lon"], 60 if tipo in ("mercado", "parque") else 40)
    via = via_cercana(lat, lon)
    if not lugar["nombre"]:
        cerca = {"paradero": "del paradero", "parque": "del parque", "colegio": "del colegio", "noche": "de un local nocturno",
                 "comercio": "de un comercio", "mercado": "del mercado"}[tipo]
        return lat, lon, f"{via}, cerca {cerca}", tipo
    nombre = lugar["nombre"]
    prefijo = {"paradero": "Paradero", "parque": "Parque", "colegio": "Frente al colegio", "noche": "Cerca de",
               "comercio": "Frente a", "mercado": "Mercado"}[tipo]
    referencia = nombre if tipo == "mercado" and "ercado" in nombre or tipo == "parque" and "arque" in nombre else f"{prefijo} {nombre}"
    return lat, lon, f"{referencia}, {via}", tipo


# ───────────────────────────── Tiempo ─────────────────────────────
def perfil_horario(picos):
    pesos = []
    for h in range(24):
        valor = 0.06
        for centro, peso in picos:
            d = min(abs(h + 0.5 - centro), 24 - abs(h + 0.5 - centro))
            valor += peso * math.exp(-(d ** 2) / (2 * 2.2 ** 2))
        pesos.append(valor)
    return pesos


PERFILES = {k: perfil_horario(v[0]) for k, v in HORAS.items()}
DIAS_SEMANA = [0.92, 0.9, 0.95, 1.0, 1.12, 1.22, 1.0]  # lunes a domingo


FORZAR_MOMENTO = None  # para los casos de las últimas horas


def momento(categoria):
    """Fecha y hora del hecho: días con más peso el fin de semana y hacia el final del periodo (la app gana usuarios)."""
    if FORZAR_MOMENTO is not None:
        return FORZAR_MOMENTO
    picos, fin_de_semana = HORAS[categoria]
    pesos_dia = []
    for d in range(DIAS):
        dia = INICIO + timedelta(days=d)
        peso = DIAS_SEMANA[dia.weekday()] * (0.75 + 0.5 * d / DIAS)
        if dia.weekday() in (4, 5):
            peso *= fin_de_semana
        elif dia.weekday() == 6:
            peso *= 1 + (fin_de_semana - 1) / 2
        pesos_dia.append(peso)
    d = rng.choices(range(DIAS), weights=pesos_dia)[0]
    h = rng.choices(range(24), weights=PERFILES[categoria])[0]
    base = (INICIO + timedelta(days=d)).replace(hour=0, minute=0, second=0)
    return base + timedelta(hours=h, minutes=rng.uniform(0, 60))


def turno_de(instante):
    h = instante.hour
    return "Mañana" if 6 <= h < 14 else "Tarde" if 14 <= h < 22 else "Noche"


def lognormal(mediana, sigma):
    return mediana * math.exp(rng.gauss(0, sigma))


def iso(t):
    return t.astimezone(LIMA).isoformat(timespec="seconds")


# ───────────────────────────── Personas ficticias ─────────────────────────────
NOMBRES_F = ["María", "Rosa", "Carmen", "Ana", "Luz", "Silvia", "Patricia", "Gladys", "Milagros", "Lucía", "Valeria", "Camila",
             "Fiorella", "Daniela", "Andrea", "Ximena", "Karla", "Sonia", "Norma", "Elena", "Yesenia", "Lourdes", "Diana", "Sofía",
             "Roxana", "Maribel", "Jacqueline", "Mariela", "Pamela", "Alejandra", "Nathaly", "Brenda", "Teresa", "Olga", "Juana", "Mayra"]
NOMBRES_M = ["José", "Luis", "Juan", "Carlos", "Jorge", "Miguel", "Víctor", "César", "Raúl", "Pedro", "Marco", "Diego",
             "Renzo", "Bruno", "Álvaro", "Edwin", "Wilber", "Freddy", "Hugo", "Alonso", "Gonzalo", "Sebastián", "Iván", "Manuel",
             "Percy", "Elmer", "Rolando", "Fernando", "Ricardo", "Javier", "Wilfredo", "Rubén", "Óscar", "Mauricio", "Fabricio", "Nilton"]
APELLIDOS = ["Quispe", "Mamani", "Huamán", "Condori", "Flores", "Chávez", "Rodríguez", "Gutiérrez", "Apaza", "Ccama", "Torres",
             "Vargas", "Zúñiga", "Cornejo", "Delgado", "Paredes", "Medina", "Salas", "Bustíos", "Linares", "Pinto", "Ramos",
             "Valdivia", "Herrera", "Chirinos", "Llerena", "Talavera", "Benavides", "Zegarra", "Málaga", "Núñez", "Cáceres",
             "Huanqui", "Ticona", "Choque", "Pacheco", "Rivera", "Begazo", "Carpio", "Manrique", "Oviedo", "Portugal", "Rondón", "Velarde"]
PARENTESCOS = ["Madre", "Padre", "Hermano/a", "Esposo/a", "Hijo/a", "Pareja", "Tío/a", "Amigo/a"]

usados = set()


def persona(femenina=None):
    femenina = rng.random() < 0.5 if femenina is None else femenina
    while True:
        nombre = rng.choice(NOMBRES_F if femenina else NOMBRES_M)
        apellidos = " ".join(rng.sample(APELLIDOS, 2))
        if (nombre, apellidos) not in usados:
            usados.add((nombre, apellidos))
            return nombre, apellidos, femenina


def sin_tildes(texto):
    return texto.translate(str.maketrans("áéíóúÁÉÍÓÚñÑ", "aeiouAEIOUnN")).lower().replace(" ", "")


def telefono():
    # Prefijo 9000: celulares ficticios (no se asignan a líneas reales en este dataset).
    return f"9000{rng.randint(0, 99999):05d}"


documentos = set()


def documento():
    # DNI ficticio: empieza con 00 para que no se confunda con uno real. Único, como exige el sistema.
    while (numero := f"00{rng.randint(0, 999999):06d}") in documentos:
        pass
    documentos.add(numero)
    return numero


ciudadanos = []
for i in range(CIUDADANOS):
    nombre, apellidos, femenina = persona(rng.random() < 0.58)  # más mujeres: el acompañamiento y el acoso pesan
    # Registro: 55 % antes del periodo (la app ya existía); el resto, creciendo durante el periodo.
    if rng.random() < 0.55:
        registrado = INICIO - timedelta(days=rng.uniform(5, 120))
    else:
        registrado = INICIO + timedelta(days=DIAS * math.sqrt(rng.random()))
    hogar = rng.choice(puntos_residencial)
    edad = max(18, min(78, int(rng.gauss(36, 13))))
    contacto, ap_contacto, _ = persona()
    ciudadanos.append({
        "id": f"CIU-{i + 1:04d}",
        "nombres": nombre, "apellidos": apellidos,
        "correo": f"{sin_tildes(nombre)}.{sin_tildes(apellidos.split()[0])}{i + 1}@correo.test",
        "telefono": telefono(),
        "registrado_en": iso(registrado),
        "perfil": {
            "tipo_documento": "DNI", "numero_documento": documento(),
            "fecha_nacimiento": (FIN.date() - timedelta(days=edad * 365 + rng.randint(0, 364))).isoformat(),
            "direccion": f"{hogar[2]} {rng.randint(100, 999)}, {urbanizacion_cercana(hogar[0], hogar[1])}",
            "contacto_nombre": f"{contacto} {ap_contacto.split()[0]}",
            "contacto_parentesco": rng.choice(PARENTESCOS), "contacto_telefono": telefono(),
        },
        "_hogar": (hogar[0], hogar[1]), "_femenina": femenina, "_registrado": registrado,
    })

serenos = []
for i in range(SERENOS):
    nombre, apellidos, femenina = persona(rng.random() < 0.22)
    turno = list(TURNOS)[i // 12]
    sector = (i % 12) // 3 + 1
    serenos.append({"id": f"SER-{i + 1:02d}", "nombres": nombre, "apellidos": apellidos,
                    "correo": f"sereno{i + 1:02d}@munijlbyr.test", "telefono": telefono(),
                    "turno": turno, "sector": sector, "unidad": "Moto" if i % 3 else "Camioneta"})

operadores = []
for i in range(OPERADORES):
    nombre, apellidos, _ = persona()
    operadores.append({"id": f"OPE-{i + 1}", "nombres": nombre, "apellidos": apellidos,
                       "correo": f"operador{i + 1}@munijlbyr.test", "turno": list(TURNOS)[i // 2]})

# Días de trabajo: cada sereno descansa un día por semana (rotativo).
turnos_trabajados = []
descanso = {s["id"]: i % 7 for i, s in enumerate(serenos)}
for d in range(-1, DIAS + 1):
    dia = (INICIO + timedelta(days=d)).date()
    for s in serenos:
        if dia.weekday() != descanso[s["id"]]:
            turnos_trabajados.append({"sereno": s["id"], "dia": dia.isoformat(), "turno": s["turno"], "sector": s["sector"]})
trabaja = {(t["sereno"], t["dia"]) for t in turnos_trabajados}


def en_turno(instante, sector):
    """Serenos de turno a esa hora en el sector (el turno de noche empieza el día anterior)."""
    turno = turno_de(instante)
    dia = (instante - timedelta(hours=6)).date() if turno == "Noche" and instante.hour < 6 else instante.date()
    return [s for s in serenos if s["turno"] == turno and s["sector"] == sector and (s["id"], dia.isoformat()) in trabaja]


ocupado_hasta = defaultdict(lambda: INICIO - timedelta(days=1))


def operador_de(instante):
    turno = turno_de(instante)
    return rng.choice([o for o in operadores if o["turno"] == turno])["id"]


# ───────────────────────────── Textos ficticios ─────────────────────────────
DESCRIPCIONES = {
    "robo": ["Me arrancharon el celular y huyeron en una moto lineal.", "Dos sujetos le quitaron la cartera a una señora.",
             "Intentaron robarme la mochila; logré escapar.", "Robaron el espejo y la llanta de repuesto de un auto estacionado.",
             "Me sacaron el celular del bolsillo dentro de la combi.", "Forzaron la puerta de una tienda durante la madrugada."],
    "violencia": ["Se escuchan gritos y golpes en la casa del vecino.", "Una mujer pide ayuda; su pareja la está agrediendo.",
                  "Pelea fuerte en una vivienda; hay menores en la casa.", "Mi vecina salió llorando y dice que la amenazaron."],
    "drogas": ["Venden droga en la esquina del parque todas las noches.", "Grupo de jóvenes consumiendo en la losa deportiva.",
               "Movimiento sospechoso de personas que entran y salen de una casa."],
    "acoso": ["Un hombre sigue a las estudiantes a la salida del colegio.", "Me tocaron en el paradero y el sujeto se fue caminando.",
              "Un sujeto en auto les dice cosas a las chicas que pasan.", "Me vienen siguiendo desde la avenida."],
    "extorsion": ["Llegó una carta pidiendo cupo al negocio.", "Me llaman para amenazarme si no pago.",
                  "Dejaron un mensaje intimidatorio en la puerta del local."],
    "alcohol": ["Grupo tomando en el parque desde la tarde.", "Personas bebiendo en la vía pública frente a la tienda.",
                "Están tomando en un auto estacionado con la música alta."],
    "pelea": ["Pelea entre varios jóvenes a la salida de una discoteca.", "Dos personas se están golpeando en la calle.",
              "Riña entre comerciantes del mercado."],
    "sospechoso": ["Un auto sin placa está estacionado hace horas.", "Dos personas miran las casas y toman fotos.",
                   "Merodean por los techos de las viviendas."],
    "vandalismo": ["Rompieron las bancas del parque.", "Hicieron pintas en la fachada del colegio.", "Se robaron la tapa del buzón."],
    "accidente": ["Choque entre un taxi y una moto, sin heridos graves.", "Una combi chocó con un auto en el cruce.",
                  "Atropello leve a un peatón; está consciente."],
    "ruido": ["Fiesta con música muy alta pasada la medianoche.", "Escándalo en la vía pública, gritos y botellas rotas.",
              "Un local sigue con música a todo volumen."],
    "riesgo": ["Un adulto mayor desorientado no sabe dónde vive.", "Un niño perdido en el mercado busca a su mamá.",
               "Una persona tirada en la vereda no responde bien."],
    "otros": ["Un perro agresivo suelto ataca a los transeúntes.", "Cables caídos en la vereda.", "Basura quemándose en la calle."],
}
# Más variantes por categoría.
for categoria, textos in {
    "robo": ["Me robaron la billetera en la cola del banco.", "Se llevaron la bicicleta que estaba amarrada afuera de la tienda.", "Un sujeto en moto me jaló el celular mientras hablaba.", "Entraron a la cochera y se llevaron herramientas.", "Le quitaron la mochila a un escolar a la salida del colegio.", "Robaron la batería de un auto estacionado en la calle."],
    "violencia": ["Un señor está golpeando a su esposa en la calle.", "Se oye a una niña llorar y a un adulto gritándole.", "Mi hermana me llamó: su pareja no la deja salir de la casa.", "Un hombre ebrio amenaza a su familia con un cuchillo."],
    "drogas": ["Dejan bolsitas y jeringas en el parque infantil.", "Un auto se detiene y entrega paquetes a varias personas.", "Fuerte olor a marihuana en el pasaje todas las tardes."],
    "acoso": ["Un hombre se exhibe frente a las chicas en el paradero.", "Me fotografiaron sin permiso en la combi.", "Un conductor de taxi le insiste a una joven para que suba.", "Siguen a mi hija desde el colegio hasta la casa."],
    "extorsion": ["Me escribieron por WhatsApp pidiendo dinero para no hacerle daño a mi familia.", "Dejaron una nota amenazante en la puerta de la bodega.", "Cobran «protección» a los mototaxis del paradero."],
    "alcohol": ["Toman en la losa deportiva y dejan botellas rotas.", "Un grupo bebe en la puerta del colegio en la noche.", "Personas ebrias duermen en la vereda y molestan a los vecinos."],
    "pelea": ["Pelea con botellas afuera de un bar.", "Dos barras se enfrentan en el parque después del partido.", "Discusión entre choferes que terminó en golpes."],
    "sospechoso": ["Un hombre revisa las puertas de los autos estacionados.", "Una camioneta pasa varias veces sin luces por la cuadra.", "Desconocidos preguntan por los horarios de los vecinos."],
    "vandalismo": ["Rompieron las lunas de un auto estacionado.", "Destruyeron los juegos del parque infantil.", "Quemaron un tacho de basura de la municipalidad."],
    "accidente": ["Un motociclista se cayó en el cruce y no se levanta.", "Un auto se subió a la vereda y chocó con un poste.", "Una bicicleta y un mototaxi chocaron; hay discusión."],
    "ruido": ["Ensayan con banda y parlantes hasta la madrugada.", "Un taller trabaja con amoladora de noche.", "Un auto con parlantes a todo volumen está estacionado hace horas."],
    "riesgo": ["Una señora se desmayó en la cola del mercado.", "Un joven está sentado en el borde del puente y no responde.", "Una persona con discapacidad está sola y desorientada en la avenida."],
    "otros": ["Un poste está a punto de caerse.", "Un buzón sin tapa en plena vereda.", "Un perro atropellado en medio de la pista."],
}.items():
    DESCRIPCIONES[categoria] += textos

# Lo que escribe la central al decidir y lo que comenta el ciudadano: varía de un caso a otro.
TEXTOS_ADMISION = ["Se envía un sereno a verificar.", "Se confirma con el ciudadano y se envía un sereno.",
                   "Coincide con otros avisos de la zona; se envía un sereno.", "Se admite y se prioriza por la hora.",
                   "Se envía la unidad del sector."]
TEXTOS_DESCARTE = {
    "Duplicado": ["Ya hay un caso abierto por el mismo hecho.", "El mismo hecho lo reportó otro vecino minutos antes."],
    "NoVerificable": ["No hay datos suficientes para ubicar el hecho.", "El ciudadano no responde y la referencia no es clara."],
    "FalsoOMalintencionado": ["Se verificó que el hecho no ocurrió.", "El reporte contiene información falsa."],
    "FueraDeAlcance": ["El hecho ocurrió fuera del distrito.", "No corresponde al serenazgo: es un reclamo administrativo."],
    "Otro": ["Se orientó al ciudadano por teléfono.", "El hecho ya fue atendido por la Policía."],
}
TEXTOS_DERIVACION = ["Corresponde a otra entidad; se le pasó el caso.", "Requiere la intervención de la entidad competente.",
                     "Se orientó al ciudadano y se comunicó a la entidad."]
ACCIONES_DERIVACION = ["Se comunicó por teléfono", "Se envió por correo", "Se coordinó por radio", "Se orientó al ciudadano"]
DETALLES_DESCARTE_SOS = ["El ciudadano indicó que no hay emergencia.", "Se activó por error dentro del bolsillo.",
                         "Un niño jugaba con el celular.", "El ciudadano confirmó que ya está a salvo y no necesita apoyo."]
COMENTARIOS_BUENOS = ["Gracias por la ayuda.", "Llegaron rápido.", "Me atendieron bien.", "El sereno fue muy amable.",
                      "Me mantuvieron informado.", "Excelente servicio, gracias."]
COMENTARIOS_MALOS = ["Tardaron bastante.", "Nadie me informó qué pasó.", "No encontraron a nadie.", "Llegaron cuando ya se habían ido.",
                     "Me llamaron varias veces y no vinieron.", "El sereno no quiso tomar mis datos."]

RESULTADOS_PUBLICOS = {
    "ResueltoEnElLugar": "El sereno atendió la situación en el lugar y la normalizó.",
    "EntregadoAPnp": "Se puso el caso en manos de la Policía Nacional.",
    "Derivado": "Se derivó a la entidad que corresponde y se orientó a la persona.",
    "SinHallazgo": "El sereno llegó al lugar y no encontró a los involucrados.",
    "FalsaAlarma": "Se verificó en el lugar que no había una emergencia.",
}
RESULTADOS = {
    "robo": {"ResueltoEnElLugar": 25, "SinHallazgo": 45, "EntregadoAPnp": 20, "Derivado": 7, "FalsaAlarma": 3},
    "violencia": {"ResueltoEnElLugar": 35, "EntregadoAPnp": 30, "Derivado": 30, "SinHallazgo": 5},
    "alcohol": {"ResueltoEnElLugar": 75, "SinHallazgo": 20, "FalsaAlarma": 5},
    "ruido": {"ResueltoEnElLugar": 75, "SinHallazgo": 20, "FalsaAlarma": 5},
    "accidente": {"ResueltoEnElLugar": 50, "Derivado": 25, "EntregadoAPnp": 20, "SinHallazgo": 5},
    "riesgo": {"ResueltoEnElLugar": 70, "Derivado": 20, "SinHallazgo": 10},
    "sospechoso": {"SinHallazgo": 55, "ResueltoEnElLugar": 35, "EntregadoAPnp": 10},
    "_": {"ResueltoEnElLugar": 45, "SinHallazgo": 28, "EntregadoAPnp": 12, "Derivado": 8, "FalsaAlarma": 7},
}
DESTINOS = {"violencia": {"Cem": 60, "Pnp": 40}, "extorsion": {"Pnp": 70, "Fiscalia": 30}, "accidente": {"Pnp": 60, "Samu": 40},
            "riesgo": {"Samu": 50, "Municipalidad": 50}, "_": {"Municipalidad": 50, "Pnp": 35, "Otro": 15}}
MOTIVOS_DESCARTE = {"Duplicado": 32, "NoVerificable": 32, "FalsoOMalintencionado": 16, "FueraDeAlcance": 14, "Otro": 6}


# ───────────────────────────── Casos ─────────────────────────────
def prioridad_de(categoria, heridos):
    if heridos == "Si" or categoria in ("violencia", "riesgo"):
        return "Alta"
    if categoria in ("robo", "pelea", "acoso", "extorsion", "accidente"):
        return "Media"
    return "Baja"


def sereno_para(instante, sector):
    """Un sereno libre de turno en el sector; si todos están ocupados, uno del sector vecino (y se nota en los tiempos)."""
    for s_sector in [sector, sector % 4 + 1, (sector + 2) % 4 + 1]:
        libres = [s for s in en_turno(instante, s_sector) if ocupado_hasta[s["id"]] <= instante]
        if libres:
            return rng.choice(libres), s_sector != sector
    todos = en_turno(instante, sector) or [s for s in serenos if s["turno"] == turno_de(instante)]
    return min(todos, key=lambda s: ocupado_hasta[s["id"]]), True


def pendiente(caso, desde, categoria, eventos, urgente):
    """La atención se simula después, en orden cronológico, para que la ocupación de cada sereno sea coherente."""
    caso["eventos"], caso["estado_final"] = eventos, "Cerrada"
    caso["_atencion"] = (desde, categoria, urgente)


def atender(caso, desde, categoria, eventos, urgente):
    """Asignación, salida, atención y cierre. Devuelve el instante de cierre."""
    sector = caso["sector"]
    sereno, lejos = sereno_para(desde, sector)
    asignado = desde
    if ocupado_hasta[sereno["id"]] > asignado:  # esperar a que se libere
        asignado = ocupado_hasta[sereno["id"]] + timedelta(minutes=rng.uniform(1, 4))
    eventos.append({"tipo": "CasoAsignado", "en": asignado, "actor": caso["_operador"], "sereno": sereno["id"]})
    # Reasignación: el sereno no responde u otro está más cerca (6 %).
    if rng.random() < 0.06:
        otro, _ = sereno_para(asignado + timedelta(minutes=3), sector)
        if otro["id"] != sereno["id"]:
            asignado += timedelta(minutes=rng.uniform(2, 5))
            eventos.append({"tipo": "CasoReasignado", "en": asignado, "actor": caso["_operador"], "sereno": otro["id"],
                            "motivo": rng.choice(["SerenoNoResponde", "OtroMasCercano", "SerenoOcupado"])})
            sereno = otro
    en_camino = asignado + timedelta(minutes=lognormal(1.2 if urgente else 2.5, 0.5))
    eventos.append({"tipo": "DesplazamientoConfirmado", "en": en_camino, "actor": sereno["id"]})
    # Trayecto: moto o camioneta en el distrito (≈11 km²); más lento en hora punta y desde otro sector.
    hora = en_camino.hour
    factor = (1.35 if hora in (7, 8, 13, 18, 19) else 1.15 if hora >= 22 or hora < 5 else 1.0) * (1.6 if lejos else 1.0)
    llegada = en_camino + timedelta(minutes=lognormal(7.0 if urgente else 9.0, 0.6) * factor)
    # A veces el sereno se demora (tráfico, otra intervención en el camino, dirección confusa): la cola larga de los tiempos.
    if rng.random() < 0.08:
        llegada += timedelta(minutes=rng.uniform(8, 25))
    dentro_rango = rng.random() < 0.88
    por_central = rng.random() < 0.06
    eventos.append({"tipo": "AtencionIniciada", "en": llegada, "actor": sereno["id"],
                    "inicio": {"distancia_m": round(rng.uniform(5, 140) if dentro_rango else rng.uniform(160, 600)),
                               "dentro_del_rango": dentro_rango,
                               "registrado_por_central": por_central}})
    duracion = lognormal({"violencia": 45, "accidente": 40, "riesgo": 35, "robo": 25}.get(categoria, 18), 0.45)
    cierre = llegada + timedelta(minutes=duracion)
    resultado = elegir(RESULTADOS.get(categoria, RESULTADOS["_"]))
    eventos.append({"tipo": "CasoCerrado", "en": cierre, "actor": sereno["id"],
                    "cierre": {"resultado": resultado, "resultado_publico": RESULTADOS_PUBLICOS[resultado],
                               "categoria": CATEGORIAS[categoria][0],
                               "destino": elegir(DESTINOS.get(categoria, DESTINOS["_"])) if resultado == "Derivado" else None}})
    ocupado_hasta[sereno["id"]] = cierre
    caso["_sereno"] = sereno["id"]
    caso["_respuesta"] = (llegada - caso["_registro"]).total_seconds() / 60
    caso["_resultado"] = resultado
    return cierre


def nuevo_caso(n, origen, categoria, registro, ciudadano, lat, lon, referencia, tipo_lugar):
    return {"id": f"CASO-{n:04d}", "origen": origen, "categoria_inicial": CATEGORIAS[categoria][0],
            "ciudadano": ciudadano["id"] if ciudadano else None, "registrado_en": registro,
            "sector": sector_de(lat, lon) if lat else sector_de(*CENTRO),
            "_registro": registro, "_categoria": categoria, "_operador": operador_de(registro), "_lugar": tipo_lugar,
            "_lat": lat, "_lon": lon, "_referencia": referencia}


def ciudadano_para(instante, femenina=None):
    candidatos = [c for c in ciudadanos if c["_registrado"] <= instante and (femenina is None or c["_femenina"] == femenina)]
    return rng.choice(candidatos or ciudadanos)


def generar_sos(n):
    categoria = elegir(PESOS_SOS)
    lat, lon, referencia, tipo_lugar = ubicar(categoria)
    hecho = momento(categoria)
    ciudadano = ciudadano_para(hecho, True if categoria in ("acoso",) and rng.random() < 0.85 else None)
    caso = nuevo_caso(n, "Sos", categoria, hecho, ciudadano, lat, lon, referencia, tipo_lugar)
    # Ubicación del dispositivo: GPS bueno casi siempre; a veces impreciso o sin permiso.
    r = rng.random()
    precision = None if r < 0.03 else round(rng.uniform(40, 180)) if r < 0.13 else round(rng.uniform(4, 25))
    caso["ubicacion"] = None if precision is None else {"latitud": round(lat, 6), "longitud": round(lon, 6), "precision_m": precision}
    eventos = [{"tipo": "SosEmitido", "en": hecho, "actor": ciudadano["id"]}]
    caso["presentado_en_central"] = hecho + timedelta(seconds=lognormal(2.2, 0.4))
    noche = hecho.hour >= 23 or hecho.hour < 6
    caso["abierto_en_central"] = caso["presentado_en_central"] + timedelta(seconds=lognormal(35 if not noche else 55, 0.6))
    t = caso["abierto_en_central"]
    # Cancelación por el ciudadano en el primer minuto (7 %).
    if rng.random() < 0.07:
        t = hecho + timedelta(seconds=rng.uniform(20, 150))
        eventos.append({"tipo": "SosCancelado", "en": t, "actor": ciudadano["id"],
                        "cancelacion": {"motivo": elegir({"PorError": 60, "YaNoLoNecesito": 25, "MeAyudaronOtros": 15})}})
        caso["eventos"], caso["estado_final"] = eventos, "Cancelada"
        return caso
    # Intentos de contacto: la mayoría contesta al primero.
    intentos = []
    for k in range(3):
        t += timedelta(seconds=lognormal(35, 0.5))
        resultado = "Contactado" if rng.random() < (0.72 if k == 0 else 0.55) else rng.choice(["SinRespuesta", "SinRespuesta", "NoFuePosibleComunicar"])
        intentos.append({"en": t, "operador": caso["_operador"], "resultado": resultado,
                         "referencia": referencia if resultado == "Contactado" else None})
        eventos.append({"tipo": "IntentoContactoRegistrado", "en": t, "actor": caso["_operador"], "resultado": resultado})
        if resultado == "Contactado":
            break
    caso["intentos_contacto"] = intentos
    # Descarte: activación accidental o falso (5 %).
    if rng.random() < 0.05:
        t += timedelta(minutes=rng.uniform(0.5, 2))
        eventos.append({"tipo": "CasoDescartado", "en": t, "actor": caso["_operador"],
                        "descarte": {"motivo": elegir({"ActivacionAccidental": 60, "FalsoOMalintencionado": 40}),
                                     "detalle": rng.choice(DETALLES_DESCARTE_SOS)}})
        caso["eventos"], caso["estado_final"] = eventos, "Descartada"
        return caso
    heridos = elegir({"No": 80, "NoSeSabe": 12, "Si": 8})
    t += timedelta(seconds=lognormal(50, 0.5))
    caso["clasificacion"] = {"categoria": CATEGORIAS[categoria][0], "prioridad": prioridad_de(categoria, heridos), "heridos": heridos}
    eventos.append({"tipo": "CasoClasificado", "en": t, "actor": caso["_operador"]})
    pendiente(caso, t + timedelta(seconds=lognormal(40, 0.5)), categoria, eventos, urgente=True)
    return caso


def generar_reporte(n):
    categoria = elegir(PESOS_REPORTE)
    lat, lon, referencia, tipo_lugar = ubicar(categoria)
    hecho = momento(categoria)
    de_la_central = rng.random() < PROPORCION_REPORTE_DE_LA_CENTRAL
    # Demora del hecho al reporte: lo que molesta ahora se reporta al toque; un robo, al llegar a casa.
    demora = lognormal({"robo": 70, "vandalismo": 240, "extorsion": 600, "sospechoso": 25, "violencia": 20}.get(categoria, 12), 0.9)
    registro = hecho + timedelta(minutes=demora)
    ciudadano = None if de_la_central else ciudadano_para(registro, True if categoria == "acoso" and rng.random() < 0.85 else None)
    caso = nuevo_caso(n, "Reporte", categoria, registro, ciudadano, lat, lon, referencia, tipo_lugar)
    canal = elegir({"Llamada": 70, "Presencial": 14, "RedesSociales": 10, "Radio": 6}) if de_la_central else "App"
    fuente = elegir({"Mapa": 55, "PosicionActual": 30, "Texto": 15}) if not de_la_central else elegir({"Mapa": 75, "Texto": 25})
    con_punto = fuente != "Texto"
    caso["reporte"] = {
        "descripcion": rng.choice(DESCRIPCIONES[categoria]),
        "ocurrido_aproximadamente_en": hecho, "canal": canal,
        "registrado_por_operador": caso["_operador"] if de_la_central else None,
        "contacto": None if not de_la_central else {"nombre": " ".join(persona()[:2]), "telefono": telefono()},
        "ubicacion": {"fuente": fuente, "latitud": round(lat, 6) if con_punto else None, "longitud": round(lon, 6) if con_punto else None,
                      "precision_m": round(rng.uniform(5, 30)) if fuente == "PosicionActual" else None,
                      "referencia": referencia if fuente == "Texto" or rng.random() < 0.5 else None},
        "fotos": 0 if de_la_central else (rng.choices([0, 1, 2, 3], weights=[62, 22, 11, 5])[0]),
    }
    if de_la_central:
        caso["presentado_en_central"] = registro
        caso["abierto_en_central"] = registro
    eventos = [{"tipo": "ReporteRegistrado", "en": registro, "actor": caso["ciudadano"] or caso["_operador"]}]
    if de_la_central:
        # La central lo registra ya admitido: pasa directo a la asignación.
        caso["clasificacion"] = {"categoria": CATEGORIAS[categoria][0], "prioridad": prioridad_de(categoria, "No"), "heridos": "No"}
        eventos.append({"tipo": "ReporteAdmitido", "en": registro + timedelta(seconds=30), "actor": caso["_operador"]})
        pendiente(caso, registro + timedelta(minutes=lognormal(4, 0.6)), categoria, eventos, urgente=False)
        return caso
    # Retiro por el ciudadano antes de la revisión (3 %).
    if rng.random() < 0.03:
        eventos.append({"tipo": "ReporteRetirado", "en": registro + timedelta(minutes=rng.uniform(2, 20)), "actor": caso["ciudadano"]})
        caso["eventos"], caso["estado_final"] = eventos, "Cancelada"
        return caso
    noche = registro.hour >= 23 or registro.hour < 6
    revision = registro + timedelta(minutes=lognormal(14 if not noche else 30, 0.7))
    eventos.append({"tipo": "RevisionIniciada", "en": revision, "actor": caso["_operador"]})
    categoria_final = categoria
    if rng.random() < 0.08:  # el operador corrige la categoría
        categoria_final = rng.choice([k for k in PESOS_REPORTE if k != categoria])
        eventos.append({"tipo": "CategoriaCorregida", "en": revision + timedelta(minutes=1), "actor": caso["_operador"],
                        "correccion": {"anterior": CATEGORIAS[categoria][0], "nueva": CATEGORIAS[categoria_final][0]}})
    decision_en = revision + timedelta(minutes=lognormal(4, 0.6))
    tipo = elegir({"Admision": 66, "Derivacion": 13, "Descarte": 21})
    if categoria_final in ("violencia", "extorsion") and rng.random() < 0.5:
        tipo = "Derivacion"
    if tipo == "Descarte":
        motivo = elegir(MOTIVOS_DESCARTE)
        eventos.append({"tipo": "ReporteDescartado", "en": decision_en, "actor": caso["_operador"],
                        "decision": {"tipo": "Descarte", "motivo": motivo, "texto": rng.choice(TEXTOS_DESCARTE[motivo])}})
        caso["eventos"], caso["estado_final"] = eventos, "Descartada"
        return caso
    if tipo == "Derivacion":
        destino = elegir(DESTINOS.get(categoria_final, DESTINOS["_"]))
        eventos.append({"tipo": "ReporteDerivado", "en": decision_en, "actor": caso["_operador"],
                        "decision": {"tipo": "Derivacion", "destino": destino, "accion": rng.choice(ACCIONES_DERIVACION),
                                     "texto": rng.choice(TEXTOS_DERIVACION)}})
        caso["eventos"], caso["estado_final"] = eventos, "Derivada"
        return caso
    heridos = elegir({"No": 88, "NoSeSabe": 9, "Si": 3})
    caso["clasificacion"] = {"categoria": CATEGORIAS[categoria_final][0], "prioridad": prioridad_de(categoria_final, heridos), "heridos": heridos}
    eventos.append({"tipo": "ReporteAdmitido", "en": decision_en, "actor": caso["_operador"],
                    "decision": {"tipo": "Admision", "texto": rng.choice(TEXTOS_ADMISION)}})
    pendiente(caso, decision_en + timedelta(minutes=lognormal(6, 0.7)), categoria_final, eventos, urgente=False)
    return caso


# Estado de un caso en un instante: lo que dicen sus eventos hasta ese momento.
ESTADO_TRAS = {"SosEmitido": "Pendiente", "IntentoContactoRegistrado": None, "CasoClasificado": None, "CasoAsignado": "Asignada",
               "CasoReasignado": "Asignada", "DesplazamientoConfirmado": "EnCamino", "AtencionIniciada": "EnAtencion",
               "CasoCerrado": "Cerrada", "ReporteRegistrado": "Recibido", "RevisionIniciada": "EnRevision", "CategoriaCorregida": None,
               "ReporteAdmitido": "Pendiente", "ReporteDerivado": "Derivada", "ReporteDescartado": "Descartada",
               "ReporteRetirado": "Cancelada", "SosCancelado": "Cancelada", "CasoDescartado": "Descartada", "CasoUrgenteCreado": "Pendiente"}


def recortar(caso):
    """Lo que aún no ocurrió al final del periodo queda fuera: así el último día tiene casos abiertos en cada etapa."""
    eventos = [e for e in caso["eventos"] if e["en"] <= FIN]
    caso["eventos"] = eventos
    estado = None
    for e in eventos:
        estado = ESTADO_TRAS.get(e["tipo"]) or estado
    caso["estado"] = estado
    if caso.get("abierto_en_central") and caso["abierto_en_central"] > FIN:
        caso["abierto_en_central"] = None
    return caso


EN_CURSO = 12  # casos de la última hora y cuarto: al cargar, la central y «Ahora» muestran casos abiertos en cada etapa

casos = []
n = 0
while len(casos) < TOTAL_CASOS - URGENTES_DE_ACOMPANAMIENTO - EN_CURSO:  # los urgentes salen de los acompañamientos
    n += 1
    caso = generar_sos(n) if rng.random() < PROPORCION_SOS else generar_reporte(n)
    if caso["registrado_en"] < FIN - timedelta(hours=3):
        casos.append(caso)
while len(casos) < TOTAL_CASOS - URGENTES_DE_ACOMPANAMIENTO:
    n += 1
    FORZAR_MOMENTO = FIN - timedelta(minutes=rng.uniform(3, 75))
    caso = generar_sos(n) if rng.random() < 0.45 else generar_reporte(n)
    if caso["registrado_en"] < FIN:
        casos.append(caso)
FORZAR_MOMENTO = None

# ───────────────────────────── Acompañamientos ─────────────────────────────
sesiones = []
for i in range(SESIONES_ACOMPANAMIENTO):
    ciudadana = ciudadano_para(INICIO + timedelta(days=rng.uniform(0, DIAS)), rng.random() < 0.8)
    dia = momento("acoso").replace(minute=0)
    inicio = dia + timedelta(minutes=rng.uniform(0, 59))
    if inicio < ciudadana["_registrado"]:
        inicio = ciudadana["_registrado"] + timedelta(hours=rng.uniform(2, 48))
    if inicio >= FIN - timedelta(minutes=35):
        inicio = FIN - timedelta(minutes=rng.uniform(40, 600))
    # Recorrido por la red vial: del paradero o la avenida hacia su casa, una posición cada ~20 s.
    origen = rng.choice(puntos_avenida)
    destino = ciudadana["_hogar"]
    distancia = metros(origen[:2], destino)
    minutos = min(29, max(6, distancia / 75 + rng.uniform(1, 5)))  # caminando ~4,5 km/h
    pasos = int(minutos * 3)
    posiciones = []
    for k in range(pasos + 1):
        f = k / pasos
        lat = origen[0] + (destino[0] - origen[0]) * f + rng.gauss(0, 12) / 111_320
        lon = origen[1] + (destino[1] - origen[1]) * f + rng.gauss(0, 12) / 111_320
        posiciones.append({"en": iso(inicio + timedelta(seconds=k * 20)), "latitud": round(lat, 6), "longitud": round(lon, 6),
                           "precision_m": round(rng.uniform(5, 20))})
    estado = elegir({"Finalizada": 86, "Vencida": 14})
    cierre = inicio + timedelta(minutes=minutos if estado == "Finalizada" else 30)
    avisos = []
    if rng.random() < 0.18:
        regla = elegir({"ConfirmacionSinRespuesta": 60, "UbicacionDesactualizada": 40})
        generado = inicio + timedelta(minutes=rng.uniform(5, minutos))
        avisos.append({"regla": regla, "generado_en": iso(generado), "estado": "Resuelto",
                       "resuelto_en": iso(generado + timedelta(minutes=rng.uniform(1, 6))),
                       "actuacion": rng.choice(["Llamada a la ciudadana: está bien.", "Se contactó al contacto de emergencia.",
                                                "Llamada sin respuesta; se envió un sereno a la zona."])})
    sesiones.append({"id": f"SES-{i + 1:03d}", "ciudadano": ciudadana["id"], "iniciada_en": iso(inicio),
                     "vence_en": iso(inicio + timedelta(minutes=30)), "cerrada_en": iso(min(cierre, FIN)) if cierre <= FIN else None,
                     "estado": estado if cierre <= FIN else "Activa", "posiciones": [p for p in posiciones if p["en"] <= iso(FIN)],
                     "confirmaciones_bienestar": [iso(inicio + timedelta(minutes=m)) for m in range(10, int(minutos), 10)],
                     "avisos": avisos})

# Algunos acompañamientos terminan en un caso urgente: la ciudadana pidió ayuda o un aviso no se resolvió.
# Solo sesiones sin otro aviso: el sistema no admite dos avisos de la misma regla por la misma confirmación.
for k, sesion in enumerate(rng.sample([s for s in sesiones if s["cerrada_en"] and not s["avisos"]], URGENTES_DE_ACOMPANAMIENTO)):
    n += 1
    ultima = sesion["posiciones"][len(sesion["posiciones"]) // 2]
    instante = datetime.fromisoformat(ultima["en"])
    ciudadana = next(c for c in ciudadanos if c["id"] == sesion["ciudadano"])
    caso = nuevo_caso(n, "Acompanamiento", "acoso", instante, ciudadana, ultima["latitud"], ultima["longitud"],
                      f"Recorrido de acompañamiento, {via_cercana(ultima['latitud'], ultima['longitud'])}", "avenida")
    caso["ubicacion"] = {"latitud": ultima["latitud"], "longitud": ultima["longitud"], "precision_m": ultima["precision_m"]}
    caso["acompanamiento"] = sesion["id"]
    eventos = [{"tipo": "CasoUrgenteCreado", "en": instante + timedelta(seconds=40), "actor": caso["_operador"]}]
    caso["clasificacion"] = {"categoria": CATEGORIAS["acoso"][0], "prioridad": "Alta", "heridos": "NoSeSabe"}
    pendiente(caso, instante + timedelta(minutes=1), "acoso", eventos, urgente=True)
    sesion["avisos"].append({"regla": "ConfirmacionSinRespuesta", "generado_en": iso(instante), "estado": "Resuelto",
                             "resuelto_en": iso(instante + timedelta(minutes=2)), "actuacion": "Se creó un caso urgente.",
                             "caso": caso["id"]})
    casos.append(caso)

# Atención en orden cronológico (cada sereno atiende un caso a la vez) y, al final, el recorte al fin del periodo.
for caso in sorted((c for c in casos if "_atencion" in c), key=lambda c: c["_atencion"][0]):
    desde, categoria, urgente = caso["_atencion"]
    atender(caso, desde, categoria, caso["eventos"], urgente)
casos = [recortar(c) for c in casos]
casos.sort(key=lambda c: c["registrado_en"])
# Los números de caso siguen el orden de registro, como en el sistema.
for i, caso in enumerate(casos):
    viejo = caso["id"]
    caso["id"] = f"CASO-{i + 1:04d}"
    for s in sesiones:
        for a in s["avisos"]:
            if a.get("caso") == viejo:
                a["caso"] = caso["id"]

# ───────────────────────────── Evaluaciones ─────────────────────────────
for caso in casos:
    if caso["estado"] != "Cerrada" or not caso["ciudadano"] or caso["origen"] == "Acompanamiento" and rng.random() < 0.5:
        continue
    if rng.random() > 0.42:  # parte de los casos cerrados que el ciudadano evalúa
        continue
    respuesta = caso.get("_respuesta", 15)
    lento = respuesta > (18 if caso["origen"] != "Reporte" else 50)
    # Lo que el ciudadano espera: un SOS, en minutos; un reporte, en menos de una hora.
    esperado = 15 if caso["origen"] != "Reporte" else 45
    media = 4.6 - max(0, respuesta - esperado) * 0.05 - (0.6 if caso["_resultado"] == "SinHallazgo" else 0)
    puntuacion = max(1, min(5, round(rng.gauss(media, 0.8))))
    cerrado = next(e["en"] for e in caso["eventos"] if e["tipo"] == "CasoCerrado")
    evaluada = cerrado + timedelta(hours=lognormal(2, 1.2))
    if evaluada > FIN:
        continue
    if puntuacion <= 3:
        motivos = ["Tardaron"] if lento else []
        motivos += rng.sample(["NoResolvieron", "NoMeInformaron", "MalTrato"], k=rng.choice([0, 1, 1, 2]))
        motivos = motivos or ["NoResolvieron"]
    else:
        motivos = (["LlegaronRapido"] if not lento and respuesta < (12 if caso["origen"] != "Reporte" else 35) else []) + rng.sample(["BuenTrato", "Resolvieron", "MeInformaron"], k=rng.choice([1, 1, 2]))
    caso["evaluacion"] = {"puntuacion": puntuacion, "motivos": sorted(set(motivos)), "evaluada_en": evaluada,
                          "comentario": rng.choice([None, None] + (COMENTARIOS_MALOS if puntuacion <= 3 else COMENTARIOS_BUENOS))}

# Días de uso de la app por ciudadano: cuando tuvo un caso o un acompañamiento, más aperturas sueltas.
actividad = defaultdict(set)
for caso in casos:
    if caso["ciudadano"]:
        actividad[caso["ciudadano"]].add(caso["registrado_en"].date())
for sesion in sesiones:
    actividad[sesion["ciudadano"]].add(datetime.fromisoformat(sesion["iniciada_en"]).date())
for c in ciudadanos:
    desde = max(c["_registrado"], INICIO)
    for _ in range(rng.choice([0, 1, 2, 3, 5, 8])):
        dia = desde + timedelta(days=rng.uniform(0, max(0.1, (FIN - desde).days)))
        actividad[c["id"]].add(dia.date())

# ───────────────────────────── Casos en curso al cargar ─────────────────────────────
# Para la presentación: casos abiertos de los últimos minutos en cada etapa, sobre el dataset base (que no cambia: llevan su
# propia semilla). Junto con los pocos que ya quedan abiertos en el periodo, al cargar hay 15 SOS y 40 reportes sin cerrar. Pocos con sereno,
# para que en la central queden serenos disponibles a los que asignar en vivo.
# (origen, etapa, cuántos, minutos en esa etapa al terminar el periodo)
EN_CURSO_AL_CARGAR = [
    ("Sos", "SinAbrir", 3, (0.3, 2.5)),       # recién llegado: nadie lo abrió todavía
    ("Sos", "EnContacto", 3, (0.3, 1.5)),     # abierto, la central intenta comunicarse
    ("Sos", "Clasificado", 6, (1, 9)),        # clasificado, espera sereno
    ("Reporte", "Recibido", 12, (3, 55)),     # nadie lo revisó todavía
    ("Reporte", "EnRevision", 6, (1, 6)),
    ("Reporte", "Pendiente", 14, (4, 35)),    # admitido, espera sereno
    ("Reporte", "PendienteCentral", 5, (2, 25)),  # registrado por la central (llamada, radio…), espera sereno
]
# El evento con el que el caso entra en la etapa: el historial termina ahí.
ENTRA_CON = {"SinAbrir": "SosEmitido", "EnContacto": "IntentoContactoRegistrado", "Clasificado": "CasoClasificado",
             "Recibido": "ReporteRegistrado", "EnRevision": "RevisionIniciada", "Pendiente": "ReporteAdmitido",
             "PendienteCentral": "ReporteAdmitido", "Asignada": "CasoAsignado", "EnCamino": "DesplazamientoConfirmado"}
CON_SERENO = ("Asignada", "EnCamino")
# Cuánto antes del evento empieza el caso, más o menos: para que se genere cerca de su hora y con los serenos de ese turno.
ANTICIPO = {"Sos": 4, "Reporte": 25}

rng = random.Random(SEMILLA + 1)


def desplazado(valor, delta):
    if isinstance(valor, datetime):
        return valor + delta
    if isinstance(valor, dict):
        return {k: desplazado(v, delta) for k, v in valor.items()}
    if isinstance(valor, (list, tuple)):
        return type(valor)(desplazado(v, delta) for v in valor)
    return valor


def sirve(caso, etapa):
    if caso["ciudadano"] and caso["ciudadano"] in con_caso_abierto:
        return False  # un ciudadano, un caso abierto a la vez
    if caso["origen"] == "Sos" or etapa in CON_SERENO:
        return "_atencion" in caso
    if etapa in ("Recibido", "EnRevision"):
        return any(e["tipo"] == "RevisionIniciada" for e in caso["eventos"])
    return "_atencion" in caso and (caso["ciudadano"] is None) == (etapa == "PendienteCentral")


def en_curso(origen, etapa, edad):
    global n, FORZAR_MOMENTO
    while True:
        n += 1
        FORZAR_MOMENTO = FIN - timedelta(minutes=edad + ANTICIPO[origen])
        caso = generar_sos(n) if origen == "Sos" else generar_reporte(n)
        if not sirve(caso, etapa):
            continue
        if etapa not in CON_SERENO:
            break
        # Solo serenos del turno que termina el periodo (el caso se corre hasta ahí; un reporte puede llegar horas después del
        # hecho) y que lleven un rato libres: así, aunque el caso se corra unos minutos, no se cruza con su caso anterior.
        antes = dict(ocupado_hasta)
        desde, categoria, urgente = caso["_atencion"]
        atender(caso, desde, categoria, caso["eventos"], urgente)
        asignados = {e["sereno"] for e in caso["eventos"] if e.get("sereno")}
        if asignados <= de_turno_al_final and antes.get(caso["_sereno"], INICIO) <= FIN - timedelta(minutes=30):
            caso["_respuesta"] = None
            ocupado_hasta[caso["_sereno"]] = FIN + timedelta(days=1)  # sigue en este caso
            break
        ocupado_hasta.clear()
        ocupado_hasta.update(antes)
    FORZAR_MOMENTO = None
    caso.pop("_atencion", None)
    # El historial termina en el evento que lo deja en la etapa, y todo el caso se corre para que ese evento ocurra «edad»
    # minutos antes del final del periodo.
    corte = max(i for i, e in enumerate(caso["eventos"]) if e["tipo"] in (ENTRA_CON[etapa], "CasoReasignado" if etapa == "Asignada" else ""))
    caso["eventos"] = caso["eventos"][:corte + 1]
    if etapa == "SinAbrir":
        caso["abierto_en_central"] = None
    caso = desplazado(caso, FIN - timedelta(minutes=edad) - caso["eventos"][-1]["en"])
    con_caso_abierto.add(caso["ciudadano"])
    return caso


de_turno_al_final = {s["id"] for k in range(1, 5) for s in en_turno(FIN - timedelta(minutes=1), k)}
con_caso_abierto = {c["ciudadano"] for c in casos if c["estado"] not in ("Cerrada", "Descartada", "Derivada", "Cancelada")}
nuevos = [en_curso(origen, etapa, rng.uniform(*edades)) for origen, etapa, cuantos, edades in EN_CURSO_AL_CARGAR for _ in range(cuantos)]
for caso in nuevos:
    recortar(caso)
    if caso["ciudadano"]:
        actividad[caso["ciudadano"]].add(caso["registrado_en"].date())
# Un sereno no atiende dos casos a la vez: el de un caso en curso terminó lo anterior antes de que se lo asignaran.
for caso in nuevos:
    if caso.get("_sereno"):
        asignado = next(e["en"] for e in caso["eventos"] if e.get("sereno") == caso["_sereno"])
        otros = [e["en"] for c in casos for e in c["eventos"] if e["tipo"] == "CasoCerrado" and c.get("_sereno") == caso["_sereno"]]
        abiertos = [c["id"] for c in casos + nuevos if c is not caso and c.get("_sereno") == caso["_sereno"] and c["estado"] != "Cerrada"]
        assert not abiertos and all(t <= asignado for t in otros), f"{caso['_sereno']} quedaría en dos casos a la vez"
        assert all(s in {x["id"] for k in range(1, 5) for x in en_turno(e["en"], k)} for e in caso["eventos"] if (s := e.get("sereno"))),             f"{caso['_sereno']} no está de turno cuando se le asigna {caso['id']}"
casos += nuevos
casos.sort(key=lambda c: c["registrado_en"])
numero = {caso["id"]: f"CASO-{i + 1:04d}" for i, caso in enumerate(casos)}
for caso in casos:
    caso["id"] = numero[caso["id"]]
for s in sesiones:
    for a in s["avisos"]:
        if a.get("caso"):
            a["caso"] = numero[a["caso"]]
libres = [s["id"] for k in range(1, 5) for s in en_turno(FIN - timedelta(minutes=1), k) if ocupado_hasta[s["id"]] <= FIN]

# ───────────────────────────── Salida ─────────────────────────────
SALIDA.mkdir(exist_ok=True)


def limpio(valor):
    if isinstance(valor, dict):
        return {k: limpio(v) for k, v in valor.items() if not k.startswith("_")}
    if isinstance(valor, list):
        return [limpio(v) for v in valor]
    if isinstance(valor, datetime):
        return iso(valor)
    return valor


def escribir(nombre, datos):
    (SALIDA / nombre).write_text(json.dumps(limpio(datos), ensure_ascii=False, indent=1), encoding="utf-8")


escribir("personas.json", {"ciudadanos": ciudadanos, "serenos": serenos, "operadores": operadores})
escribir("casos.json", casos)
escribir("acompanamientos.json", sesiones)
escribir("turnos.json", turnos_trabajados)
escribir("actividad.json", {k: sorted(d.isoformat() for d in v) for k, v in sorted(actividad.items())})
escribir("meta.json", {"fin_del_periodo": FIN, "inicio_del_periodo": INICIO, "dias": DIAS, "semilla": SEMILLA,
                       "nota": "Datos ficticios. Al cargar, desplazar todas las fechas en (ahora - fin_del_periodo)."})

with (SALIDA / "casos.csv").open("w", newline="", encoding="utf-8") as f:
    w = csv.writer(f)
    w.writerow(["id", "origen", "categoria", "estado", "registrado_en", "hora", "dia_semana", "latitud", "longitud", "sector",
                "lugar", "referencia", "prioridad", "sereno", "respuesta_min", "resultado", "puntuacion"])
    nombres_categoria = {v[0]: v[1] for v in CATEGORIAS.values()}
    for c in casos:
        cat = (c.get("clasificacion") or {}).get("categoria") or c["categoria_inicial"]
        w.writerow([c["id"], c["origen"], nombres_categoria[cat], c["estado"], iso(c["registrado_en"]), c["registrado_en"].hour,
                    c["registrado_en"].strftime("%a"), round(c["_lat"], 6), round(c["_lon"], 6), c["sector"], c["_lugar"],
                    c["_referencia"], (c.get("clasificacion") or {}).get("prioridad"), c.get("_sereno"),
                    round(c["_respuesta"], 1) if c.get("_respuesta") and c["estado"] == "Cerrada" else None,
                    c.get("_resultado") if c["estado"] == "Cerrada" else None, (c.get("evaluacion") or {}).get("puntuacion")])

# ───────────────────────────── Resumen ─────────────────────────────
def pct(parte, total):
    return f"{100 * parte / total:.1f} %" if total else "—"


def percentil(valores, p):
    valores = sorted(valores)
    if not valores:
        return None
    k = (len(valores) - 1) * p
    i = int(k)
    return valores[i] + (valores[min(i + 1, len(valores) - 1)] - valores[i]) * (k - i)


nombres_categoria = {v[0]: v[1] for v in CATEGORIAS.values()}
total = len(casos)
lineas = [f"# Resumen del dataset (generado; semilla {SEMILLA})", "",
          f"Periodo simulado: {INICIO:%d/%m/%Y} – {FIN:%d/%m/%Y} ({DIAS} días). Todas las fechas se desplazan al cargar.", "",
          f"- Casos: **{total}** · ciudadanos: {CIUDADANOS} · serenos: {SERENOS} · operadores: {OPERADORES} · acompañamientos: {len(sesiones)}",
          f"- Casos por día: {total / DIAS:.1f} en promedio", ""]
lineas += ["## Por origen", "", "| Origen | Casos | % |", "|---|---:|---:|"]
for k, v in Counter(c["origen"] for c in casos).most_common():
    lineas.append(f"| {k} | {v} | {pct(v, total)} |")
lineas += ["", "## Por categoría (la final, tras clasificar o corregir)", "", "| Categoría | SOS | Reportes | Total |", "|---|---:|---:|---:|"]
por_cat = defaultdict(Counter)
for c in casos:
    cat = (c.get("clasificacion") or {}).get("categoria") or c["categoria_inicial"]
    por_cat[nombres_categoria[cat]][c["origen"]] += 1
for cat, cnt in sorted(por_cat.items(), key=lambda x: -sum(x[1].values())):
    lineas.append(f"| {cat} | {cnt['Sos'] + cnt['Acompanamiento']} | {cnt['Reporte']} | {sum(cnt.values())} |")
lineas += ["", "## Estado al final del periodo", "", "| Estado | Casos |", "|---|---:|"]
for k, v in Counter(c["estado"] for c in casos).most_common():
    lineas.append(f"| {k} | {v} |")
abiertos = [c for c in casos if c["estado"] not in ("Cerrada", "Descartada", "Derivada", "Cancelada")]
lineas += ["", "## Abiertos al cargar", "", "| Origen | Estado | Casos | Desde hace |", "|---|---|---:|---|"]
for (origen, estado), grupo in sorted(defaultdict(list, {k: [c for c in abiertos if (c["origen"], c["estado"]) == k]
                                                          for k in {(c["origen"], c["estado"]) for c in abiertos}}).items()):
    edades = sorted((FIN - c["eventos"][-1]["en"]).total_seconds() / 60 for c in grupo)
    lineas.append(f"| {origen} | {estado} | {len(grupo)} | {edades[0]:.0f}–{edades[-1]:.0f} min |")
lineas += ["", f"Serenos de turno sin caso al cargar: {len(libres)} (para asignar en vivo)."]
respuestas = [c["_respuesta"] for c in casos if c.get("_respuesta") and c["estado"] == "Cerrada" and c["origen"] != "Reporte"]
respuestas_rep = [c["_respuesta"] for c in casos if c.get("_respuesta") and c["estado"] == "Cerrada" and c["origen"] == "Reporte"]
lineas += ["", "## Tiempos de respuesta (registro → inicio de la atención)", "",
           f"- SOS y urgentes: mediana {percentil(respuestas, .5):.1f} min · p90 {percentil(respuestas, .9):.1f} min ({len(respuestas)} casos)",
           f"- Reportes: mediana {percentil(respuestas_rep, .5):.1f} min · p90 {percentil(respuestas_rep, .9):.1f} min ({len(respuestas_rep)} casos)"]
evaluados = [c["evaluacion"]["puntuacion"] for c in casos if c.get("evaluacion")]
cerrados_app = sum(1 for c in casos if c["estado"] == "Cerrada" and c["ciudadano"])
lineas += ["", "## Evaluaciones", "", f"- {len(evaluados)} evaluaciones ({pct(len(evaluados), cerrados_app)} de los casos cerrados con ciudadano) · promedio {statistics.mean(evaluados):.2f} de 5",
           "- Distribución: " + " · ".join(f"{p}★ {v}" for p, v in sorted(Counter(evaluados).items()))]
lineas += ["", "## Hora del día (todos los casos)", "", "```"]
horas = Counter(c["registrado_en"].hour for c in casos)
maximo = max(horas.values())
for h in range(24):
    lineas.append(f"{h:02d} h {'█' * round(40 * horas[h] / maximo)} {horas[h]}")
lineas += ["```", "", "## Día de la semana", "", "| Día | Casos |", "|---|---:|"]
dias_es = ["lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo"]
semana = Counter(c["registrado_en"].weekday() for c in casos)
for d in range(7):
    lineas.append(f"| {dias_es[d]} | {semana[d]} |")
lineas += ["", "## Dónde (tipo de lugar de OpenStreetMap)", "", "| Lugar | Casos |", "|---|---:|"]
for k, v in Counter(c["_lugar"] for c in casos).most_common():
    lineas.append(f"| {k} | {v} |")
lineas += ["", "## Sector de patrullaje", "", "| Sector | Casos |", "|---|---:|"]
for k, v in sorted(Counter(c["sector"] for c in casos).items()):
    lineas.append(f"| Sector {k} | {v} |")
sin_punto = sum(1 for c in casos if (c.get("ubicacion") is None and c["origen"] != "Reporte") or (c["origen"] == "Reporte" and c["reporte"]["ubicacion"]["latitud"] is None))
lineas += ["", f"Sin ubicación en el mapa: {sin_punto} casos (SOS sin GPS y reportes con solo una referencia en texto).", ""]
(SALIDA / "RESUMEN.md").write_text("\n".join(lineas), encoding="utf-8")
print("\n".join(lineas[:12]))
print(f"Escrito en {SALIDA}")
