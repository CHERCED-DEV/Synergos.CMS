#!/usr/bin/env node
/**
 * Crea el par certificado + llave del endpoint HTTPS de desarrollo (#137).
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * POR QUÉ EXISTE, Y ES LA MITAD QUE FALTABA.
 *
 * `appsettings.Development.json` apuntaba a `C:\LOCAL_CDN\synergos-dev.crt`
 * desde siempre, y NADA en el repo lo creaba: medido con un grep de
 * `synergos-dev`, `dev-certs` y `openssl` sobre el árbol entero, las únicas dos
 * menciones eran las que lo consumen. Ni script, ni paso del onboarding, ni
 * nota. O sea una dependencia obligatoria sin camino para obtenerla — que es
 * peor que la ruta equivocada, porque la ruta se ve en un fichero y esto no se
 * ve en ninguno.
 *
 * POR QUÉ openssl Y NO `dotnet dev-certs`.
 *
 * `dotnet dev-certs https` sólo emite para `localhost`, y este repo sirve
 * `synergos.local` —lo nombran el Kestrel de Development, el
 * `UmbracoApplicationUrl`, el `launchUrl` de los dos perfiles y los diez
 * hostnames de vertical que siembra `DevContentFiller`—. Un certificado sin ese
 * SAN hace que el navegador rechace la conexión, que es el mismo fallo con más
 * pasos.
 *
 * openssl viene en Linux, en macOS y en Git para Windows, que este repo ya
 * exige. Si no está en el PATH se busca donde Git lo instala, porque en
 * PowerShell no está: ésa es la diferencia entre una herramienta que funciona en
 * la máquina del arquitecto y una que funciona.
 * ─────────────────────────────────────────────────────────────────────────────
 */
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const RAIZ = join(dirname(fileURLToPath(import.meta.url)), '..');
const DESTINO = join(RAIZ, 'certs');
const CRT = join(DESTINO, 'synergos-dev.crt');
const KEY = join(DESTINO, 'synergos-dev.key');

/**
 * Los hostnames que el certificado tiene que cubrir.
 *
 * Se DERIVAN del appsettings y del seeder en vez de escribirse acá: con la lista
 * a mano, el vertical once nace con el navegador rechazando su hostname y nadie
 * relaciona las dos cosas. El comodín `*.synergos.local` cubre los verticales
 * sin tener que enumerarlos, y `synergos.local` va aparte porque un comodín NO
 * cubre el dominio desnudo.
 */
const HOSTS = ['synergos.local', '*.synergos.local', 'localhost'];

function openssl() {
  const candidatos = [
    'openssl',
    'C:\\Program Files\\Git\\usr\\bin\\openssl.exe',
    'C:\\Program Files (x86)\\Git\\usr\\bin\\openssl.exe',
  ];
  for (const c of candidatos) {
    try {
      execFileSync(c, ['version'], { stdio: 'ignore' });
      return c;
    } catch { /* el siguiente */ }
  }
  console.error(
    'No se encontró `openssl`.\n' +
    '  · Windows: viene con Git para Windows. Abrí «Git Bash» y corré esto ahí,\n' +
    '    o añadí C:\\Program Files\\Git\\usr\\bin al PATH.\n' +
    '  · Linux/macOS: instalalo con el gestor de paquetes del sistema.');
  process.exit(1);
}

const forzar = process.argv.includes('--forzar');

if (existsSync(CRT) && existsSync(KEY) && !forzar) {
  // No PISA lo que ya está, que es la forma de idempotencia que importa acá:
  // regenerar el certificado invalida el que el sistema ya tenga confiado, y el
  // síntoma es «el navegador dejó de confiar» sin relación aparente con esto.
  console.log(`✓ ya existe — ${CRT}`);
  console.log('  (para reemplazarlo: --forzar, y tocará volver a confiarlo)');
  process.exit(0);
}

const exe = openssl();
mkdirSync(DESTINO, { recursive: true });

// El SAN va en un fichero de configuración y no en `-addext`: esa bandera no
// existe en las openssl 1.0.x que todavía trae alguna imagen, y el fallo sería
// un certificado SIN SAN — que los navegadores modernos rechazan ignorando por
// completo el Common Name, o sea el defecto de arriba otra vez.
const cnf = join(DESTINO, 'openssl.cnf');
writeFileSync(cnf, [
  '[req]',
  'distinguished_name = dn',
  'x509_extensions = v3',
  'prompt = no',
  '[dn]',
  'CN = synergos.local',
  '[v3]',
  'basicConstraints = critical, CA:FALSE',
  'keyUsage = critical, digitalSignature, keyEncipherment',
  'extendedKeyUsage = serverAuth',
  `subjectAltName = ${HOSTS.map(h => `DNS:${h}`).join(', ')}`,
  '',
].join('\n'));

try {
  execFileSync(exe, [
    'req', '-x509', '-nodes', '-newkey', 'rsa:2048', '-sha256',
    '-days', '825',   // el techo que aceptan los navegadores para un cert de servidor
    '-keyout', KEY, '-out', CRT, '-config', cnf,
  ], { stdio: 'inherit' });
} finally {
  rmSync(cnf, { force: true });
}

console.log(`\n✓ certificado  ${CRT}`);
console.log(`✓ llave        ${KEY}`);
console.log(`  cubre: ${HOSTS.join(', ')}`);
console.log('\nFalta CONFIARLO, o el navegador lo rechaza igual:');
console.log('  · Windows (PowerShell como administrador):');
console.log('      Import-Certificate -FilePath certs\\synergos-dev.crt \\');
console.log('        -CertStoreLocation Cert:\\LocalMachine\\Root');
console.log('  · macOS: security add-trusted-cert -d -k /Library/Keychains/System.keychain \\');
console.log('      certs/synergos-dev.crt');
console.log('  · Linux: copialo a /usr/local/share/ca-certificates/ y corré update-ca-certificates');
console.log('\nY que `synergos.local` resuelva a 127.0.0.1 en el fichero hosts del sistema.');
