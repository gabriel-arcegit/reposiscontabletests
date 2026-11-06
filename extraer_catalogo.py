#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script: extraer_catalogo.py
Propósito: Extraer el catálogo de cuentas contables desde 'catalogo.pdf'
utilizando la librería pdfplumber y expresiones regulares.
"""

import json  #revisar sumas y cuadres de estados financieros
import os
import re
import sys
from pathlib import Path

try:
    import pdfplumber
except ImportError:
    print("[ERROR] La librería 'pdfplumber' no está instalada.")
    print("Instálela ejecutando: pip install pdfplumber")
    sys.exit(1)

# Clasificación contable oficial según el primer dígito
MAPA_CLASIFICACION = {
    '1': {'tipo': 'Activo', 'naturaleza': 'Deudora'},
    '2': {'tipo': 'Pasivo', 'naturaleza': 'Acreedora'},
    '3': {'tipo': 'Capital', 'naturaleza': 'Acreedora'},
    '4': {'tipo': 'Ingreso', 'naturaleza': 'Acreedora'},
    '5': {'tipo': 'Costo', 'naturaleza': 'Deudora'},
    '6': {'tipo': 'Gasto', 'naturaleza': 'Deudora'},
}

# Expresión regular para identificar el código de cuenta y su nombre al inicio de cada línea:
# - Código: inicia con 1-6 seguido de dígitos, puntos o guiones opcionales
# - Separador: punto opcional, guion, dos puntos o espacios
# - Nombre: texto descriptivo de la cuenta
PATRON_CUENTA = re.compile(
    r"^\s*([1-6](?:[\d.-]*\d)?|\b[1-6]\b)\.?\s*[-–—:\t]?\s+(.+?)\s*$"
)


def procesar_linea(linea: str) -> dict | None:
    """
    Evalúa una línea de texto mediante expresión regular y retorna un diccionario
    con los datos de la cuenta clasificada, o None si no corresponde a una cuenta.
    """
    linea = linea.strip()
    if not linea:
        return None

    match = PATRON_CUENTA.match(linea)
    if not match:
        return None

    codigo_raw = match.group(1).strip().rstrip('.')
    nombre_raw = match.group(2).strip()

    # Limpieza de puntos suspensivos, guiones o espacios finales en el nombre
    nombre = re.sub(r'[\s._-]+$', '', nombre_raw).strip()
    if not nombre:
        return None

    # Obtener sólo los dígitos para clasificar y contar jerarquía
    solo_digitos = re.sub(r'\D', '', codigo_raw)
    if not solo_digitos:
        return None

    primer_digito = solo_digitos[0]
    if primer_digito not in MAPA_CLASIFICACION:
        return None

    # Clasificar automáticamente tipo y naturaleza según el primer dígito
    clasificacion = MAPA_CLASIFICACION[primer_digito]
    tipo = clasificacion['tipo']
    naturaleza = clasificacion['naturaleza']

    # Asignar permiteMovimientos = true a cuentas con 4 o más dígitos
    permite_movimientos = len(solo_digitos) >= 4

    # Establecer por defecto estaActiva = true para cuentas base y generales
    esta_activa = True

    return {
        "codigo": codigo_raw,
        "nombre": nombre,
        "tipo": tipo,
        "naturaleza": naturaleza,
        "permiteMovimientos": permite_movimientos,
        "estaActiva": esta_activa
    }


def extraer_catalogo(
    pdf_path: str = "catalogo.pdf",
    output_path: str = "catalogo_extraido.json"
) -> list[dict]:
    """
    Abre el archivo PDF, procesa página por página extrayendo las cuentas mediante regex,
    y guarda el resultado estructurado en formato JSON con codificación UTF-8.
    """
    ruta_pdf = Path(pdf_path).resolve()
    ruta_salida = Path(output_path).resolve()

    # Manejo de excepción si el archivo PDF no existe
    if not ruta_pdf.exists() or not ruta_pdf.is_file():
        raise FileNotFoundError(
            f"El archivo PDF no fue encontrado en: {ruta_pdf}"
        )

    cuentas = []
    codigos_vistos = set()

    print(f"Abriendo archivo PDF: {ruta_pdf.name}")

    try:
        with pdfplumber.open(ruta_pdf) as pdf:
            total_paginas = len(pdf.pages)
            print(f"Total de páginas a examinar: {total_paginas}")

            for idx, pagina in enumerate(pdf.pages, start=1):
                # Manejo de excepción por si alguna página no contiene texto extraíble
                try:
                    texto = pagina.extract_text()
                    if not texto or not texto.strip():
                        print(f"[ADVERTENCIA] La página {idx} de {total_paginas} no contiene texto extraíble o está vacía. Omitiendo.")
                        continue
                except Exception as err_pag:
                    print(f"[ADVERTENCIA] Error al extraer texto de la página {idx}: {err_pag}. Omitiendo.")
                    continue

                # Procesar líneas de la página
                for linea in texto.splitlines():
                    cuenta = procesar_linea(linea)
                    if cuenta and cuenta["codigo"] not in codigos_vistos:
                        codigos_vistos.add(cuenta["codigo"])
                        cuentas.append(cuenta)

    except Exception as err_pdf:
        print(f"[ERROR] Error durante la lectura del PDF: {err_pdf}")
        raise

    # Guardar en archivo JSON formateado con codificación UTF-8
    try:
        with open(ruta_salida, 'w', encoding='utf-8') as f_out:
            json.dump(cuentas, f_out, ensure_ascii=False, indent=2)
        print(f"\n[ÉXITO] Extracción finalizada con éxito.")
        print(f"Total de cuentas extraídas: {len(cuentas)}")
        print(f"Archivo generado: {ruta_salida}")
    except Exception as err_io:
        print(f"[ERROR] No se pudo guardar el archivo JSON de salida: {err_io}")
        raise

    return cuentas


def main():
    directorio_raiz = Path(__file__).resolve().parent
    ruta_pdf = directorio_raiz / "catalogo.pdf"
    ruta_salida = directorio_raiz / "catalogo_extraido.json"

    try:
        extraer_catalogo(str(ruta_pdf), str(ruta_salida))
    except FileNotFoundError as err:
        print(f"\n[EXCEPCIÓN] {err}")
        print("Asegúrese de colocar el archivo 'catalogo.pdf' en la raíz del proyecto antes de ejecutar.")
        sys.exit(1)
    except Exception as err:
        print(f"\n[EXCEPCIÓN INESPERADA] {err}")
        sys.exit(1)


if __name__ == "__main__":
    main()

