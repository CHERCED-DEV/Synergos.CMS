# Cierre de la auditoría de arquitectura (F5)

> Boleta final, backlog priorizado por **riesgo real** (no por olor ni por tamaño), y el
> CODEOWNERS completo como apéndice. Documentos hermanos:
> [01 plan](01-plan-auditoria-arquitectura.md) · [02 boleta SOLID](02-boleta-solid.md) ·
> [03 mapa de segregación](03-mapa-segregacion.md).

## Qué se hizo en esta ola de auditoría

Las cinco fases del plan corrieron de un tirón. Lo que arrojó, en una tabla:

| Fase | Resultado |
|---|---|
| F0 Línea base | Medida: capas limpias (0 usings prohibidos), 21/37 controllers sin test, hotspots identificados |
| F1 Gate de capas | `LayerRuleTests` (4) — el ADR 0002 pasa de costumbre a invariante ejecutable |
| F2 Boleta SOLID | 7 hotspots + 3 controles calificados con evidencia `archivo:línea` |
| F3 Mapa segregación | 14 verticales mapeados; núcleo compartido identificado; Booking≡Travel |
| F4 Refinar | **2 defectos de seguridad cerrados** + código muerto borrado + pared del demo testeada |
| F5 Cierre | este documento |

**Los dos hallazgos que justificaron toda la auditoría** fueron defectos de seguridad vivos que
ningún test cubría, ambos corregidos con pruebas que fijan la propiedad:

1. **IDOR en Shop** (wishlist + mensajería): identidad tomada del cliente. Cerrado —
   `RequireMemberEmail()`, 9 tests. El propio archivo lo declaraba cerrado en `Orders` y lo tenía
   abierto al lado.
2. **Downcast 2FA** (`AccountController:545`): otra impl dejaba al member sin recovery codes en
   silencio. Cerrado — los códigos viajan en el resultado, 2 tests.

## Backlog priorizado por riesgo real

El orden es por **daño si no se hace**, no por esfuerzo ni por lo feo que se ve.

### Alto — toca seguridad, datos o corrección
1. ~~**AdminController: 29 actions con auth repetida a mano.**~~ **HECHO.**
   `RequireRolesAttribute` a nivel de clase; los 29 checks manuales removidos. El default pasa
   de abierto a cerrado. Verificado end-to-end: anónimo contra las 8 rutas reales de admin →
   las 8 deniegan. 9 tests, incluido el que clava que el `[AllowAnonymous]` de la CLASE no
   desactiva el filtro (el bug que casi se cuela: habría abierto las 29 con build verde).
2. ~~**Antiforgery en los POST destructivos de admin.**~~ **HECHO.**
   `[AutoValidateAntiforgeryToken]` a nivel de clase —cubre los 4 verbos inseguros y los POST
   que se agreguen mañana— más `@Html.AntiForgeryToken()` explícito en las 16 formas POST de
   `Views/Admin/*`. Verificado end-to-end contra la app corriendo con un member real con rol
   `admin`: POST sin token → **400**, POST con token → **302** y el efecto aplicado (comentario
   aprobado, bulk de 2, member bloqueado); GET intactos. 3 tests, incluido el que cuenta tokens
   por form. **Los dos defectos que destapó están abajo.**
3. ~~**La semántica 401 vs 403 del dashboard.**~~ **HECHO.** Y el diagnóstico que estaba escrito
   aquí —"redirige al login, bucle para el que ya inició sesión"— **era optimista**. Al medirlo
   contra la app, `Forbid()` emitía un 302 a `/Account/AccessDenied`, ruta que **no existe** en
   este proyecto: caía al "No published content" de Umbraco y respondía **200 OK**. Ni login
   para el anónimo, ni explicación para el autenticado, y un 200 sobre lo que era una
   denegación.

   Ahora el filtro distingue las dos preguntas: **anónimo → `Challenge`**, que el esquema de
   cookies traduce a un redirect al login real con `returnUrl`; **autenticado sin el rol → 403
   de verdad**, renderizando `Views/Shared/AccessDenied.cshtml` con la URL intacta y una salida
   ("entrar con otra cuenta"), que es lo único que le faltaba al que quedaba encerrado.

   | actor | antes | ahora |
   |---|---|---|
   | anónimo, GET | 302 → ruta inexistente → **200** | 302 → `/Account/Login?ReturnUrl=…` → login real |
   | autenticado sin rol, GET | 302 → ruta inexistente → **200** | **403** + página que lo explica |
   | con rol, GET | 200 | 200 |

   Verificado end-to-end con tres actores reales y el round-trip completo del anónimo
   (login → vuelve a `/admin/` → 200). En POST el antiforgery sigue corriendo antes, así que sin
   token es 400 para todos y con token válido el que no tiene rol recibe 403.
   4 Dictionary keys nuevas (es-CO + en-US) para la página; **requieren uSync Import**.

### Lo que destapó el antiforgery — dos defectos vivos, ya cerrados

Ninguno se buscaba. Salieron de mirar en serio el HTML que emite el dashboard, y los dos
llevaban olas rotos sin que ningún test los tocara:

1. **La cola de moderación tenía forms anidados.** El `<ul>` de comentarios vivía DENTRO de
   `#bulk-form`, y HTML no permite formularios anidados: el parser del navegador descarta el de
   adentro. Confirmado en Chromium — con la estructura vieja el DOM tenía **1 solo form** y el
   botón "Aprobar" de cada comentario pertenecía a `#bulk-form`, no al suyo. Es decir:
   **Aprobar / Rechazar / Spam por comentario no funcionaban**; posteaban a
   `/admin/moderation/comments`, que es GET-only. Arreglado sacando la lista del form (los
   checkboxes ya se asociaban por `form="bulk-form"`, el mecanismo de HTML5 para exactamente
   esto). El JS que contaba seleccionados pasó de `querySelectorAll` —solo ve descendientes— a
   `form.elements`, que es la colección real de controles del form.
2. **`/admin/members` mostraba siempre 0 Members.** `UmbracoMemberRosterReader` pasaba
   `memberTypeAlias: string.Empty`; Umbraco lee `null` como "todos los tipos", pero con una
   cadena arma el predicado `ContentTypeAlias == ""`, que no matchea nada. Con la tabla vacía,
   **lock / unlock / reset 2FA / password-reset / roles / delete / GDPR-erase no se podían
   alcanzar desde la UI**. Un carácter. 5 tests nuevos, con mutation check.

   *Precisión sobre la cifra:* al descubrirlo se anotó "0 filas antes, 1 después", que es lo que
   se vio en la DB de desarrollo. Un A/B controlado posterior —misma DB nueva, tres members
   registrados, cambiando SOLO ese argumento— midió **1 de 3 visibles con `string.Empty` y 3 de
   3 con `null`**. El defecto es el mismo y está confirmado; cuántas filas sobreviven depende de
   los datos, no es un 0 fijo. Importa porque el superviviente resultó ser **el primer member
   registrado** — y eso es justo lo que hizo insuficiente la primera versión del gate #7.

La lectura: la auditoría F2 midió los controllers y no vio ninguno de los dos, porque ambos
viven en el HTML emitido y en el borde con la API de Umbraco — superficie que ni los tests ni
la boleta tocaban. El backlog gana un item por eso (Medio #7).

Y un tercero, del item #3: el 302 a una ruta inexistente que devolvía 200. Los tres comparten
la misma forma —**el código estaba bien y el resultado no**— y ninguno se ve leyendo el
controller. Solo aparecen cuando se ejerce la ruta de verdad. Es el argumento más fuerte que
dejó esta auditoría a favor de la verificación end-to-end, por encima de sumar tests unitarios.

### Medio — deuda que crece con cada equipo nuevo
3. ~~**`SeamComposer.cs` → partials por vertical.**~~ **HECHO.** Partido en 10 clases
   parciales por dominio; el archivo principal quedó en 59 LOC (orquestador). Orden de
   registro preservado exacto (por eso los notificadores composite siguen registrándose al
   final). Verificado con huella de las 483 líneas + el gate de reconstrucción arrancando el
   contenedor DI completo. El corte PERFECTO por vertical (sin dominios repartidos en dos
   archivos por el intercalado del original) queda pendiente — necesita reordenar registros,
   seguro solo con un gate de snapshot de registros.
4. ~~**Extraer derivación de copy UI a los `*ContentRules`.**~~ **HECHO.** `BuildSubtitle`/
   `BuildBadges` (Realty) y `BuildStatus`/`BuildBadges`/`BuildSubtitle` (Eventos) viven junto a
   las reglas de su vertical, con 16 tests. `BuildStatus` recibe el reloj por parámetro en vez
   de leer `UtcNow` adentro — esa era la razón por la que no se podía testear el límite
   past/upcoming. El subtítulo de eventos dejó de emitir separador colgante sin ciudad.
5. ~~**`ICommentRepository` (12 métodos) → split lectura/escritura/moderación.**~~ **HECHO.**
   El corte no se eligió por olfato: se midió quién usa qué y de los **cinco** consumidores
   **ninguno** usaba las tres caras. Lo que gana no es orden estético — `ICommentReader` no
   *puede* devolver un comentario sin aprobar ni borrar nada, así que el render público del
   blog perdió por construcción una capacidad que tenía y nadie vigilaba. El doble de test de
   blogs pasó de implementar 12 métodos (para usar 1) a implementar 1. Sin interfaz compuesta,
   y 8 tests de arquitectura lo sostienen.
6. ~~**El `switch` de selección de PSP.**~~ **HECHO, y era peor que un olor.** El `switch` tenía
   un solo brazo vivo y el caso `wompi` comentado: con el router apagado —el default—
   `Provider="Wompi"` **servía el stub en silencio**, contra lo que documenta el propio ajuste.
   Un operador podía creerse cobrando de verdad mientras el checkout devolvía pagos de mentira.
   Una sola fábrica para las dos ramas, fallback al stub conservado pero **con warning**, y 11
   tests sobre la matriz de config.
7. ~~**Nada verifica el HTML que emiten las vistas SSR.**~~ **HECHO** —
   `tools/ssr-dom-check.mjs` + workflow `ssr-dom.yml`. Arranca la aplicación real contra una
   SQLite desechable, se autentica como member con rol y mira lo que llega al navegador:
   estructura del marcado, un token de antiforgery por form POST, que los datos sembrados **se
   vean**, y que denegar sea denegar. Molde del gate de reconstrucción (ADR 0128): DB temporal,
   puerto 0, nunca toca nada real. Sin import de uSync —el dashboard es MVC puro— así que corre
   en ~90s.

   **Se validó por mutación, que es lo único que distingue un gate de un adorno.** Los tres
   defectos se reintrodujeron uno por uno:

   | defecto reintroducido | veredicto |
   |---|---|
   | `<ul>` de vuelta dentro de `#bulk-form` | ✗ falla — 3 comprobaciones independientes |
   | `memberTypeAlias: string.Empty` | ✗ falla — "oculta 2 de 3 members sembrados" |
   | denegación de vuelta a `Forbid()` | ✗ falla — "aterriza en el placeholder de Umbraco" |

   **La prueba de mutación encontró dos fallos en el propio gate**, y ese fue su mayor
   rendimiento:
   - La cola de moderación tiene dos ramas y con DB nueva salía la vacía, así que el gate
     **nunca renderizaba** el marcado que tenía el bug. Se siembra la cola apuntando el store a
     un temp, y se exige que la rama con items aparezca.
   - La comprobación del roster pedía que apareciera *un* member — y con el defecto original
     el que sobrevive es justo el primero registrado, que es con el que el gate se autentica.
     Habría dado verde sobre el defecto que existe para atrapar. Ahora exige el conjunto
     completo, con un member testigo que no es ni el primero ni el de la sesión.
8. ~~**Un test intermitente: `CatalogEngineReplicationTests.Realty_SinTildes_Encuentra`.**~~
   **DIAGNOSTICADO Y CERRADO.**

   La hipótesis que quedó anotada aquí —"`CatalogPropertyCatalogProviderTests` publica
   *Casa en Laureles* al `static`"— **era falsa**: esa clase ejercita
   `CatalogPropertyCatalogProvider`, otra implementación, y no toca el estático del stub. Sirve
   de recordatorio de por qué no se tocó nada mientras la hipótesis no estuviera probada.

   **La causa real.** El único mutador del `static` es
   `StubPropertyCatalogProviderTests.Publish_Happy_AppearsInSearch`, que publica un inmueble con
   `City: "Medellín"`. xUnit corre las clases en paralelo. El test que fallaba hace **dos**
   búsquedas de "Medellín"/"medellin" y compara secuencias de ids: si el publish aterriza justo
   **entre** ambas, la segunda trae un id de más.

   **Por qué era tan esquivo** —y esto es lo que confirma el diagnóstico—: el estático no se
   limpia nunca. Una vez publicado, el listado extra sale en las DOS búsquedas y la igualdad
   vuelve a cumplirse. La ventana de fallo es solo el hueco entre las dos llamadas, lo que
   predice exactamente la rareza observada (~1 de 16).

   **Probado, no supuesto.** Se reprodujo de forma determinista intercalando un publish entre
   las dos búsquedas: rojo.

   **El arreglo es de raíz, no una serialización de tests.** El catálogo y el contador pasan a
   estado de INSTANCIA en los tres stubs con esa forma —Propiedades, Eventos y Cursos—. En
   producción no cambia nada: los tres se registran con `AddSingleton`, así que sigue habiendo
   exactamente uno. En tests, cada `new` arranca limpio. `CatalogStubIsolationTests` (5) fija la
   propiedad, incluido el caso contrario —un provider **sí** ve lo que él mismo publicó, que es
   lo que de verdad importa en producción— y pasa el mutation check: reintroducir el `static`
   pone 3 de los 5 en rojo.

   `StubEventCatalogProvider` tenía la misma bomba sin haber estallado: mismo `static`, y
   `CatalogEngineReplicationTests` también publica eventos.

### Bajo — higiene, sin riesgo
7. **Migración a carpetas por vertical.** **MEDIDA Y DIFERIDA — decisión pendiente.** Ver abajo.
8. ~~**`BlogsController`: `sync-over-async` en bucle.**~~ **HECHO.** Bloqueaba un hilo del pool
   hasta 100 veces por request en el endpoint más público del vertical. Partido en
   `ResolveReactionWeightsAsync` (el I/O) + `ComputeTrending` puro. El efecto secundario es el
   que más vale: la política de ranking —peso base 1, desempate alfabético, tope de 10,
   contar-una-vez-por-post— estaba enterrada detrás de una llamada de I/O y ahora tiene 9 tests.
9. ~~**`IPaymentRouting.cs`: renombrar.**~~ **HECHO.** El archivo no tenía ninguna interfaz. Ese
   prefijo `I-` fue lo que llevó a clasificarlo como interfaz muerta y borrarlo, rompiendo el
   build durante esta misma auditoría.
10. ~~**`IDashboardReadModel`: abstracción prematura.**~~ **MEDIDO — recomendación: dejarla.** Y
    apareció algo que no era la pregunta: la interfaz se justifica diciendo *"la consume tanto el
    `/admin` SSR como la app Angular"*, y **el `/admin` SSR no la inyecta**. Un consumidor, no
    dos; era el plan, no el estado. El comentario ya está corregido. Sobre colapsarla: no envuelve
    una impl, **colapsa cinco seams en una dependencia** — el test del controller sustituye un
    doble en vez de montar cinco. Borrarla no quitaría una capa, movería esas cinco dependencias
    al controller y a su test. Decisión final del arquitecto.

#### El #7, medido antes de hacerlo

| capa | archivos planos |
|---|---|
| `Synergos.CMS.Interfaces/` | 113 |
| `Synergos.CMS.Web/Services/` | 115 |
| `Synergos.CMS.Application/Services/Impl/` | 80 |
| **total** | **308** (+ 164 de tests que arrastran `using`) |

En este repo las carpetas **sí** mapean a namespaces (`Services/Catalog/` →
`…Web.Services.Catalog`), así que la migración no es mover archivos: es renombrar 308
namespaces y arreglar los `using` de cada consumidor. Y su propósito declarado es habilitar un
CODEOWNERS que **este mismo documento dice no activar todavía** ("cuando existan los equipos").

**Recomendación: diferirla hasta que existan los equipos.** Tres razones concretas:

1. **El primer CODEOWNERS no la necesita.** El subconjunto que el apéndice marca como seguro de
   activar hoy —`@synergos/core` sobre `SeamComposer.cs` y `uSync/`— usa rutas que ya existen.
2. **Un movimiento de 308 archivos rompe toda rama en vuelo** y hay que rehacerlo a mano en cada
   una. El costo no es el diff, es el de los demás.
3. **Cuesta el `git blame` de 308 archivos** justo cuando la auditoría demostró que la historia
   es lo que permite entender por qué algo está como está.

Lo que sí conviene decidir ya es **qué dispara la migración**: la propuesta es hacerla el día
que se cree el primer equipo con dueño distinto de `@synergos/core`, y como una ola dedicada con
el repo congelado, no intercalada con feature.

### Explícitamente NO en el backlog
- **Reescribir `DevContentFiller` (4581 LOC).** Tooling tras flag; feo pero sin riesgo. No vale
  el costo mientras haya cualquier item de riesgo Alto abierto.
- **`EhrController`.** Es demo `[DevSeedOnly]` con su pared testeada. Convertirlo a producción
  (auth por rol + portal por pertenencia) es **decisión de producto**, no refactor — el propio
  archivo lo documenta.

## Estado de gates al cierre

| Gate | Estado |
|---|---|
| `dotnet test` | **1674 passing** (0 fallos) |
| `usync-audit` | 0 errores, 0 warnings |
| `usync-rebuild` (ADR 0128) | 880/880 ítems, DB derivable |
| `check-css-parity` | 0 orphans |
| `LayerRuleTests` (F1) | 4/4 — capas limpias vigiladas |
| `ssr-dom` (Medio #7) | 8 páginas · estructura, antiforgery, datos y denegación |

## La lectura honesta de la boleta

**La arquitectura de capas es sólida y ahora está vigilada.** El ADR 0002 se cumple de verdad, y
`LayerRuleTests` lo mantiene así. El patrón seam+stub hizo que 118 archivos de tests de services
fueran baratos de escribir.

**La deuda se concentra donde el código quedó Umbraco-dependiente** — los controllers. No es
casualidad: la arquitectura hizo fácil testear lo puro y dejó lo acoplado sin red. Los dos
agujeros de seguridad vivían exactamente ahí, en superficie HTTP que ningún test tocaba. La
mitigación estructural es el fix #1 del backlog (auth declarativa), que además abre esos
controllers a tests.

**"Grande" resultó un mal predictor.** De los 7 controllers grandes, 3 no violaban SRP (grandes
por DTOs anidados). El peor riesgo —el IDOR— estaba en un endpoint corto al lado de uno correcto.
Por eso la boleta califica por evidencia, no por LOC.

---

## Apéndice — CODEOWNERS (borrador, NO activado)

Activar cuando existan los equipos y (idealmente) tras la migración a carpetas del backlog #7.
Primer subconjunto seguro de activar hoy: solo las líneas de `@synergos/core` sobre
`SeamComposer.cs` y `uSync/`.

```gitignore
# La ÚLTIMA coincidencia gana. @core es catch-all; los verticales lo sobrescriben.
*                                                   @synergos/core

# Plataforma / build / schema
/Synergos.CMS.Web/Program.cs                        @synergos/core
/Synergos.CMS.Web/Composers/                        @synergos/core
/Synergos.CMS.Web/Composers/SeamComposer.cs         @synergos/core
/Synergos.CMS.Web/uSync/                            @synergos/core @synergos/schema
/Synergos.CMS.Tests/Architecture/                   @synergos/core

# Pagos (7 verticales dependen)
/Synergos.CMS.Interfaces/IPayment*.cs               @synergos/core @synergos/pagos
/Synergos.CMS.Web/Controllers/PaymentWebhookController.cs @synergos/pagos @synergos/core

# Verticales (patrón por nombre; frágil hasta la migración a carpetas)
/Synergos.CMS.Web/Controllers/EventosController.cs   @synergos/eventos
/Synergos.CMS.Web/Controllers/RealtyController.cs     @synergos/realty
/Synergos.CMS.Web/Controllers/EhrController.cs        @synergos/salud @synergos/security
/Synergos.CMS.Web/Controllers/HealthcareApiController.cs @synergos/salud @synergos/security
/Synergos.CMS.Web/Controllers/GovController.cs        @synergos/gov
/Synergos.CMS.Web/Controllers/BookingController.cs    @synergos/booking
/Synergos.CMS.Web/Controllers/TravelController.cs     @synergos/viajes
/Synergos.CMS.Web/Controllers/{Blogs,BlogTag,BlogRss,Comments,CommentsModeration}Controller.cs @synergos/social
/Synergos.CMS.Web/Controllers/Shop*.cs               @synergos/shop
/Synergos.CMS.Web/Controllers/AcademyController.cs    @synergos/academy
/Synergos.CMS.Web/Controllers/SearchController.cs     @synergos/search
/Synergos.CMS.Web/Controllers/{Admin,DashboardApi}Controller.cs @synergos/admin
/Synergos.CMS.Web/Controllers/{Account,Member}Controller.cs @synergos/membership
```

> El CODEOWNERS por-archivo completo (Interfaces + Application + Services + Tests de cada
> vertical) lo produjo el mapa de F3; se integra aquí cuando la migración a carpetas lo vuelva
> mantenible. Por patrón de nombre plano, hoy sería un documento de cientos de líneas frágil a
> cada archivo nuevo mal nombrado — de ahí la recomendación de carpetas primero.
