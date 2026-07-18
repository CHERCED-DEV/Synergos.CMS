# ADR 0109 — Almacén de ficheros PRIVADOS + subida/descarga autorizada de documentos (T6, piloto Gobierno)

- **Status:** Accepted
- **Date:** 2026-07-18
- **Deciders:** Arquitecto + agente. El arquitecto firmó la forma en la investigación previa de T6 (**"dos almacenes: biblioteca de Umbraco para lo público + almacén PRIVADO servido por un endpoint que autoriza"**, y **"Gobierno primero"**), y pidió ejecutar la ola de forma autónoma sin consultar cada decisión. El agente decidió el resto con los precedentes del proyecto (cifrado at-rest por el molde de ADR 0098; input inline + `FormData` en vez de anidar el custom-element `file-uploader`). Verificado en vivo contra el CMS corriendo, con una **query de control** que descarta el falso positivo.
- **Relacionados:** ADR 0098 (`IPhiStore` — el precedente de "dato sensible cifrado at-rest con Data Protection + escritura atómica"; este ADR **calca su mecánica** pero para bytes arbitrarios), ADR 0105 (`IJsonEntityStore` — el reparto que se respeta: la metadata de negocio vive en el agregado del vertical, el almacén solo guarda contenido), ADR 0103 (identidad server-trusted; la subida ya heredaba `RequireMember` + `DenyIfForeignCase`), ADR 0002 (Application sin AspNetCore — por eso el seam recibe `byte[]` y no `IFormFile`), ADR 0037 (audit append-only de la subida), ADR 0013 (creación perezosa de directorios: cero I/O en boot), ADR 0075 (tests por seam). Regla de oro doc 25: ninguna capacidad transversal se implementa dos veces.

---

## Context

El doc 25 daba T6 (uploads) por medio construido: *"Uploads/media reales → biblioteca Umbraco | **rutas 404** | `IDocumentUploadService` listo"*. **Los tres puntos eran falsos**, y conviene dejarlos escritos porque son el tipo de premisa que se vuelve a asumir:

1. **No había "rutas 404" porque no había rutas.** `grep IFormFile` en todo el proyecto daba **CERO**: nada en el CMS recibía un binario. No existía multipart, ni límite de tamaño de request, ni validación de content-type, en ningún sitio.
2. **`IDocumentUploadService` no estaba "listo": recibía `(caseId, name)` — dos cadenas.** Su propio xmldoc lo admitía: *"el binario real NO viaja por este seam"*.
3. **La UI tiraba el fichero.** `onDocumentFile` hacía `uploadName.set(files[0].name)` y soltaba el objeto `File`; el cliente mandaba `{applicationId, name}` en JSON.

El resultado era la **misma familia del email que logueaba "enviado" sin enviar** (ADR 0106): el ciudadano adjuntaba el escaneo de su cédula, el API respondía `accepted`, quedaba una fila en el expediente… y no existía ningún fichero. El flujo **tenía éxito y no producía nada**.

Dos hechos del terreno decidieron el diseño:

- **`wwwroot/media` es PÚBLICO por construcción.** La biblioteca de Umbraco vive físicamente dentro de `wwwroot/`, así que la sirve `UseStaticFiles` sin credenciales (verificado: 200 sobre una imagen de producto). Sirve para fotos de producto; **no** para la cédula de un ciudadano.
- **`App_Data/` no lo alcanza ningún middleware de estáticos.** Es donde ya viven los stores durables (`syn-orders`, `syn-gov-cases`, `syn-healthcare`…).

T6 estuvo **bloqueado a propósito** hasta cerrar T2-Gobierno: con `GovController` sin guardas, `?citizen=<email>` y un radicado **secuencial** (`SG-2026-000001`), meter bytes reales habría significado que la cédula de un ciudadano se descargaba **contando**. Construir T6 antes no habría avanzado: habría invertido el signo. Con T2 completo (`e45a6c2`, `5551a81`), la subida ya heredaba autenticación + ownership, y la descarga tenía contra qué autorizar.

## Decision

### 1. Un almacén de ficheros privados, seam nuevo (`IPrivateFileStore`)

Se crea el **primer seam del proyecto que maneja bytes**. Guarda contenido y devuelve un **id opaco**; no sabe de dominio (a qué expediente pertenece un fichero lo sabe el agregado del vertical, igual que con `IJsonEntityStore`).

Tres propiedades lo hacen privado, y **las tres importan**:

1. **Fuera de `wwwroot`** (bajo `App_Data/`, configurable): ningún middleware de estáticos llega. Es lo único que lo separa de `/media`.
2. **Id opaco generado por el almacén** (`Guid` "N"), no por el llamador — que podría elegirlo adivinable o pisar un fichero ajeno. Al revés que el radicado, que se enumera contando.
3. **Cifrado at-rest** con `IDataProtector` (purpose `Synergos.PrivateFiles.v1`), calcando `FileSystemEncryptedPhiStore`: quien lea el disco no lee la cédula.

**El content-type y el nombre original viajan DENTRO del sobre cifrado**, no en el nombre del fichero: `{json}\n{bytes}`. Guardarlos en el nombre los dejaría en claro, y el nombre suele delatar el contenido (`cedula-juan.pdf`).

**El id opaco NO es la autorización.** Es lo que evita la enumeración; el permiso lo comprueba quien sirve los bytes, en cada descarga.

### 2. La subida pasa a multipart real — el primer `IFormFile`

`POST /api/gov/document` deja de aceptar `{applicationId, name}` en JSON y recibe **multipart** (`IFormFile file` + `applicationId`). El orden de las comprobaciones es deliberado:

1. `RequireMember()` → 401. **Se autentica antes de leer el fichero**: no se sube a memoria el input de un anónimo.
2. Tamaño (`RequestSizeLimit` + chequeo explícito) y **allowlist** de content-type (PDF/JPG/PNG) — allowlist, no lista de prohibidos: lo que no está, no entra.
3. `DenyIfForeignCase()` → 403 (ownership del expediente).
4. **Solo entonces** se lee el stream a memoria y se llama al seam.

`Path.GetFileName` sobre el nombre recibido: un cliente puede mandar `../../algo.pdf` en el multipart, y ese nombre se guarda y se devuelve al descargar.

**El seam recibe `byte[]`, no `IFormFile`** (ADR 0002): leer el multipart es trabajo del controller; a Application solo llegan primitivas.

### 3. En el seam, los BYTES primero y la metadata después

`StubDocumentUploadService` guarda el contenido en el almacén y **solo si eso funciona** adjunta la referencia al expediente. Al revés, un fallo del almacén dejaría en el expediente un documento **listable pero no descargable** — exactamente la mentira que T6 vino a cerrar. Un adjunto vacío lanza en vez de responder `accepted`.

El audit va al final y es **best-effort**: el documento ya está guardado y adjunto; un audit caído no puede tumbar la subida.

### 4. Descarga autorizada: dueño **o** funcionario

Endpoint nuevo `GET /api/gov/document/{caseId}/{docId}`: `RequireMember()` → 401; luego **dueño del expediente** o **rol de funcionario** (`funcionario,admin`) → si no, 403. Responde con `File(bytes, contentType, fileName)` (molde de `DashboardApiController.ExportCsv`) más `X-Content-Type-Options: nosniff` y `Content-Disposition: attachment`: el navegador lo descarga, nunca lo interpreta como documento activo en el origen del sitio.

El funcionario entra por rol y no por ownership **porque lo necesita para decidir** el expediente — es su trabajo.

### 5. `downloadUrl` solo existe si hay binario detrás

El DTO emite `downloadUrl` **solo** cuando el documento tiene fichero. Los adjuntos sembrados antes de T6 son metadata sin bytes: llegan sin URL y la UI los pinta como texto, en vez de ofrecer una descarga que daría 404. El modo degradado (mock) tampoco inventa `downloadUrl`, por la misma razón.

## Consequences

**Positivas:**

- **El adjunto existe de verdad.** Los bytes se persisten cifrados, sobreviven un reinicio y se recuperan idénticos (verificado con round-trip binario, no solo texto).
- **Un documento sensible ya no puede caer en una carpeta pública por descuido**: el default de `StorageRoot` es `App_Data/`, y el POCO documenta que apuntarlo a `wwwroot/` los haría públicos a todos.
- **Aislamiento verificado con control**, no por ausencia: se creó un fichero real en `App_Data/syn-files/` (404 por HTTP) contra uno en `wwwroot/` (200 con su contenido). Sin el control, el 404 habría sido un falso positivo — la carpeta ni existía.
- **El almacén es reutilizable**: `scope` es un parámetro, así que Healthcare (resultados de laboratorio) o Propiedades (escrituras) lo consumen sin tocarlo. La regla de oro se respeta: esta capacidad no se implementará dos veces.
- La biblioteca de Umbraco **sigue siendo la respuesta para lo público** (fotos, avatares): este ADR no la reemplaza, la delimita.

**Negativas o trade-offs:**

- **Se lee el fichero entero a memoria** (`MemoryStream`) antes de guardarlo. Con el tope de 10 MB es aceptable; para ficheros grandes habría que hacer streaming al almacén. **Criterio de reapertura:** si se sube el tope por encima de ~25 MB o si se observa presión de memoria bajo subidas concurrentes.
- **El content-type se valida por lo que declara el cliente**, no por los magic bytes. Un PDF renombrado pasa el filtro. Se acepta porque la descarga es `attachment` + `nosniff` (no se interpreta en el origen) y el almacén no ejecuta nada; **el sniffing de cabecera queda pendiente** y es la primera mejora si se abre el allowlist a más tipos.
- **Sin escaneo antivirus.** El adapter de producción (blob storage + escaneo) implementa la misma seam; el stub no lo simula porque simularlo sería otra promesa vacía.
- **El tope de subida es una `const`**, no configuración: `RequestSizeLimit` exige constante de compilación. El POCO tiene el valor para el almacén, pero el atributo no puede leerlo.
- **Cambio incompatible en dos contratos**: `IDocumentUploadService.UploadAsync` y `UploadDocumentRequest` de la UI. Es deliberado — dejar la firma vieja habría permitido seguir "subiendo" sin bytes.

**Notas de implementación:**

- Un bug que la ola destapó y que **ningún build verde atrapa**: el test del controller reventaba con `NullReferenceException` al escribir la cabecera `nosniff`, porque un controller construido a mano no tiene `HttpContext`. Se le da un `DefaultHttpContext` y de paso el test **afirma la cabecera** — el control de seguridad quedó cubierto en vez de eliminado.
- El `Content-Type` del `fetch` de subida **no se fija a mano**: lo pone el navegador junto al `boundary` del `FormData`. Escribirlo rompe el parseo del multipart en el servidor. El test lo afirma explícitamente.
- Tests: 9 del almacén real (incluido **"en disco no queda el plaintext"** y "el contenido manipulado se ignora"), 6 del seam, 5 de la autorización de descarga, 3 de la UI. Todos verificados por mutación.
- La carpeta del almacén se crea **perezosamente** en la primera subida (ADR 0013: cero I/O en boot).

## Alternatives considered

- **Subir a la biblioteca de medios de Umbraco (`/media`).** Rechazado: es pública por construcción (vive en `wwwroot`). Habría hecho descargable la cédula de cualquier ciudadano con la URL, que es justo el problema. Sigue siendo la respuesta correcta para imágenes públicas.
- **Guardar los bytes sin cifrar, confiando solo en el gate del endpoint.** Rechazado: el proyecto ya decidió que el dato sensible en reposo va cifrado (ADR 0098), y la mecánica estaba probada. El gate protege el acceso por HTTP; el cifrado protege del acceso al disco (backup extraviado, contenedor compartido).
- **Base64 dentro del JSON existente**, sin tocar el contrato. Rechazado: infla ~33%, obliga a subir todo a memoria dos veces y deja el binario en logs y en el store de metadata.
- **Reutilizar el custom-element `file-uploader`** (ya publicado, con XHR y progreso real). Rechazado **para este corte**: anidar un Angular Element dentro de otro trae los gotchas de hidratación ya documentados, sin ganancia funcional aquí (un solo fichero, formulario simple). Queda como refinamiento natural cuando se quiera barra de progreso.
- **Meter los bytes en `IJsonEntityStore`.** Rechazado: es un store de JSON sin cifrar, pensado para PII de compra; mezclar binarios ahí rompería su contrato y su ruta.
- **Un `IFileStore` genérico que sirviera también lo público.** Rechazado por prematuro: hay un solo consumidor y la biblioteca de Umbraco ya cubre lo público. Se crea cuando haya un segundo caso real.

## References

- `Synergos.CMS.Interfaces/IPrivateFileStore.cs` — el seam y las tres propiedades que lo hacen privado.
- `Synergos.CMS.Web/Services/FileSystemPrivateFileStore.cs` — cifrado, sobre y escritura atómica.
- `Synergos.CMS.Application/Configuration/PrivateFileStoreSettings.cs` — `StorageRoot` y por qué su default es la propiedad de seguridad más importante del POCO.
- `Synergos.CMS.Web/Controllers/GovController.cs` — subida multipart (§2) y descarga autorizada (§4).
- `Synergos.CMS.Application/Services/Impl/StubDocumentUploadService.cs` — bytes primero, metadata después.
- ADR 0098 (PHI cifrado), ADR 0105 (`IJsonEntityStore`), ADR 0103 (identidad server-trusted), ADR 0106 (la familia del "éxito que no produce nada").
