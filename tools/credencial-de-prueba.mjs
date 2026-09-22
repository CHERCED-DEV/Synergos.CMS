//
// La credencial del administrador que necesitan los gates que ARRANCAN la app.
//
// ─────────────────────────────────────────────────────────────────────────────
// UNA DECLARACIÓN, CUATRO CONSUMIDORES — y por eso vive acá.
//
// Los cuatro gates que levantan el CMS de verdad (`usync-rebuild-check`,
// `humo-portada`, `humo-conectado`, `ssr-dom-check`) arrancan con
// `ASPNETCORE_ENVIRONMENT=Docker` contra una base DESECHABLE, así que pasan por
// la instalación desatendida de Umbraco y necesitan las tres claves de
// `Umbraco:CMS:Unattended`.
//
// Desde el #150 el correo y la clave ya NO están en `appsettings.Docker.json` —
// ese fichero es el perfil con el que corre producción y la clave estaba
// versionada en un repo público. Así que los gates tienen que ponerlas ellos, y
// escribirlas cuatro veces sería meter cuatro literales nuevos por la puerta de
// atrás el mismo día que se quitó uno.
//
// SE GENERA, NO SE ESCRIBE. La clave es aleatoria por corrida: una constante acá
// volvería a ser una credencial versionada, y el argumento «pero es sólo para los
// tests» es exactamente el que dejó la de producción en el repo. No hace falta
// recordarla: la base vive en un directorio temporal que el gate borra al
// terminar, y ningún gate entra al backoffice.
// ─────────────────────────────────────────────────────────────────────────────
import { randomBytes } from 'node:crypto';

/**
 * Las tres claves de `Umbraco:CMS:Unattended`, listas para el `env` de un
 * `spawn`. El nombre lo trae el perfil; se repite acá igual, porque un gate que
 * dependiera de que el perfil lo traiga se rompería en silencio el día que
 * alguien lo saque — y «en silencio» es literal: con las tres ausentes Umbraco
 * instala igual y deja un administrador al que nadie puede entrar.
 */
export function credencialDesatendida() {
  return {
    Umbraco__CMS__Unattended__UnattendedUserName: 'admin',
    Umbraco__CMS__Unattended__UnattendedUserEmail: 'gate@synergos.invalid',
    // 24 bytes en base64 pasan el mínimo de Umbraco con holgura, y el `Aa1!`
    // garantiza mayúscula, minúscula, dígito y símbolo pase lo que pase con el
    // azar: una corrida que fallara una vez cada tantas por una política de
    // contraseñas es un gate que enseña a ignorar el rojo.
    Umbraco__CMS__Unattended__UnattendedUserPassword: `Aa1!${randomBytes(24).toString('base64url')}`,
  };
}
