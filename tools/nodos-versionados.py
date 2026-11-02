#!/usr/bin/env python3
"""
Cada página y cada medio del contenido versionado tienen que quedar en la base después del import.

    python3 tools/nodos-versionados.py <base sqlite> <carpeta uSync/v9>

Lee la clave (`Key`) de cada `.config` de `Content/` y `Media/` y la busca en `umbracoNode`. Imprime cuántos están
y cuáles faltan, y sale con 1 si falta alguno.

Por qué: el import de uSync cuenta ítems PROCESADOS, no nodos creados. `usync-rebuild-check` comparaba ese número con
los archivos trackeados, así que un ítem que se procesara sin dejar su nodo pasaba en verde y en silencio. Esto mira
la base, que es lo que importa (002-produccion, 2026-11-02).

La base se lee CON su `-wal`, y SQLite lo hace solo si el `-wal` está al lado: copiar sólo el `.db` de una base que
acaba de escribir el import pierde lo último que escribió. Así se midió una vez, por error, que faltaban 11 nodos que
sí estaban.
"""
import pathlib
import sqlite3
import sys
import xml.etree.ElementTree as ET

sys.stdout.reconfigure(encoding='utf-8')

if len(sys.argv) != 3:
    sys.exit('uso: nodos-versionados.py <base sqlite> <carpeta uSync/v9>')

base, usync = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
if not base.is_file():
    sys.exit(f'✗ no existe la base {base}')

esperados = {}
for carpeta in ('Content', 'Media'):
    for archivo in sorted((usync / carpeta).glob('*.config')):
        clave = (ET.parse(archivo).getroot().get('Key') or '').lower()
        if clave:
            esperados[clave] = f'{carpeta}/{archivo.name}'

if not esperados:
    sys.exit(f'✗ no hay ningún .config de contenido ni de media en {usync}: ¿la carpeta es la de uSync/v9?')

con = sqlite3.connect(base)
try:
    en_la_base = {fila[0].lower() for fila in con.execute('select uniqueId from umbracoNode')}
finally:
    con.close()

faltan = sorted(nombre for clave, nombre in esperados.items() if clave not in en_la_base)
print(f'{len(esperados) - len(faltan)}/{len(esperados)} páginas y medios versionados están en la base')
for nombre in faltan:
    print(f'  ✗ falta {nombre}')
sys.exit(1 if faltan else 0)
