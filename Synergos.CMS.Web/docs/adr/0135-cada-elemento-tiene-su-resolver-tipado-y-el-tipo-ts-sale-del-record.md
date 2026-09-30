# ADR 0135 — Cada elemento tiene su resolver tipado: un record C# declara lo que viaja y el tipo TS sale de él

- **Estado:** Aceptado (2026-09-30) — el arquitecto la ratificó con los seis cambios que pidió el piloto
  de cinco elementos (#173), que pasan a ser parte de la decisión (ver «Resultado del piloto»)
- **Fecha:** 2026-09-29
- **Propone:** la síntesis de la auditoría de reutilización (informe 20 §5.C.1, «el corazón del
  refinado»). El arquitecto fijó número y estado el 2026-09-29.
- **Parte de:** [#172](../../../../../issues/172) · épica [#139](../../../../../issues/139)
- **Enmienda:** ADR 0015 §1 (todo `elementSyn*` compone `compIntegration` → `configOverride`) y
  ADR 0096 §1 (module-mount hereda `configOverride`), sólo para las funcionalidades
- **Depende de:** ADR 0134 (funcionalidad / pieza). **La usan:** ADR 0136 y 0137

## Contexto

> Marcas de certeza (informe 20): ✔ comprobado en el disco · ◐ medido por un agente con dos
> derivaciones o ejecutando, sin re-derivar · ○ cifra de un agente sin verificar. «Re-leído» = línea
> abierta al escribir esta ADR (CMS `9ec00d81`, UI `98b9233`).

### El defecto: el SSR pinta bien y la hidratación lo borra

**43 elementos colocables tiran lo que escribió el editor al hidratar** ◐. Se midió ejecutando el
sanitizador de cada elemento contra lo que emite su vista SynHost, y en tres se comprobó en vivo con
una consulta de control (informe 10 §5):

| elemento | con el `config` exacto que manda el CMS | con las claves que lee (control) |
|---|---|---|
| `kpi-card` | «— Sin dato» | «VENTAS DEL MES 1.234 ↑ +12 % vs. agosto» |
| `tag` | **desaparece** | «Oferta» |
| `rating-stars` | **«0 de 5 estrellas»** (el editor puso 4) | «4 de 5» |

El mecanismo, en `kpi-card`: la vista emite `kpiLabel`, `kpiValue`, `kpiTrend`, `kpiDelta`,
`kpiPeriod` (`Views/Partials/SynHost/KpiCard.cshtml:27-31`, re-leído) y el elemento lee `label`,
`value`, `trend`… ✔. El propio elemento afirma lo contrario en su cabecera: *«every CMS property is a
TypeScript input with the same alias»* (`kpi-card.ts:24`, re-leído). En `dropdown`, el CMS manda
`optionsJson` y el elemento lee `options`: botón gris sin opciones ◐ (ejecutando el sanitizador, y
`TS2339` al tiparlo, informe 13 §0).

**36 de los 43 son piezas** ◐ (informe 16 §7.2): el refinado de la ADR 0134 no los borra. Los tiene
que cerrar un contrato.

### Por qué nada lo caza hoy

- **No hay resolver tipado**: cada vista arma un diccionario libre ✔
  (`new Dictionary<string, object?> { ["kpiLabel"] = … }`). `SynHostEmitRequest` es genérico —
  `BlockAlias`, `Props`, `ConfigOverrideJson`, `Culture`, `FallbackHtml`
  (`Synergos.CMS.Interfaces/ISynHostEmitter.cs:57-62`, re-leído)— y el emitter no conoce la forma.
- **Lo que el CMS manda, clasificado:** 91 vistas SynHost emiten 266 claves — 128 contenido · 73
  decisión del editor · 53 técnica o de negocio · 12 microcopia ◐ (informe 16 §3).
- **Una puerta que pisa todo:** `configOverride`, JSON libre del editor, en 90 de 91 vistas ◐; se
  fusiona **encima** de las props y, si no parsea, se descarta en silencio
  (`DefaultSynHostEmitter.cs:113-131`, re-leído).
- **Lo que viaja y nadie lee:** `culture` se añade a las 91 (`DefaultSynHostEmitter.cs:104`,
  re-leído) y ningún elemento la lee; 7 leen `locale`, que nadie manda ◐.
- **El tipo generado describe otra cosa:** los `Syn*Schema` de
  `vitals/contracts/src/elements-syn.contract.ts` se generan del **ElementType de uSync**, no de lo que
  emite la vista. Meten las **pestañas** como propiedades en 89/90 interfaces y no coinciden con lo
  emitido en 23/91 ◐. La regex que lo hace, `PROP_ALIAS_RE = /<Alias>…<\/Alias>/g`, casa también el
  `<Alias>` de las pestañas (`tools/cms-sync.mjs:108`, re-leído).
- **La única señal era un aviso amarillo permanente** (`[W4] … missing from ELEMENT_CONFIG_FIELDS`
  de `cms:validate`) ○. Y los tests no la dan: los 7 specs de `dropdown` fijan `optionsJson` como
  input suelto, que el CMS nunca manda, así que D1 pasa en verde (informe 13 §1).

### Lo que ya existe y sirve de molde

- **`SeatMapProjection.BuildProps`** (`Synergos.CMS.Web/Services/SeatMapProjection.cs:164`,
  re-leído) arma las props de un elemento en C#, fuera de Razor, con `ISeatMapProvider` resolviendo
  el inventario en el servidor (ADR 0127) y con sus propios tests (`SeatMapProjectionTests`). Es el
  embrión exacto del resolver.
- **Un gate por nombre de campo C# ↔ contrato** ya existe para el bridge:
  `ContractsIndexTests.Lo_que_el_bridge_EMITE_y_lo_que_el_contrato_DOCUMENTA_son_lo_mismo`.

### Lo que hace NewShore

Cada macro llama a un **resolver tipado** (`IParametersResolver<T>`: 104 + 37 ◐) que junta el
diccionario por secciones ✔, el JSON de negocio de la funcionalidad ✔, ajustes de entorno, los
parámetros del editor ✔ y el orden de envío ✔; la vista Razor es un **host tonto** que serializa ✔
(informe 20 §3). Su punto flaco: si el resolver falla, la vista emite `data-initial-value=''`, el
`JSON.parse` lanza y **el widget desaparece en silencio** (informe 15 D5).

## Decisión

### 1. Un `record` por elemento, que declara lo que viaja

En `Synergos.CMS.Interfaces`, atado por un atributo al **`name` del registry** (la identidad de un
elemento es ese nombre), con su tipo (funcionalidad / pieza, ADR 0134) y sus secciones de diccionario
(ADR 0136):

```csharp
[ElementoSynHost("kpi-card", Tipo.Pieza, Diccionario = ["Synhost.Kpi"])]
public sealed record KpiCardProps(string? Label, string? Value, string? Trend, string? Delta, string? Period);
```

(Ilustrativo: los nombres del atributo no están decididos. La sección sí existe en uSync:
`Synhost.Kpi.Trend.*`, la que hoy sólo usa el respaldo SSR de la vista.)

uSync sigue siendo la fuente de verdad de **lo que el editor edita** (ADR 0008, 0083 principio 2).
El record pasa a ser la de **lo que viaja** hacia el elemento, que hoy no la tiene.

### 2. Un resolver por elemento

Lee las decisiones del editor del `IPublishedElement`, la configuración de negocio (ADR 0137) y el
runtime de la petición, y devuelve el record. Vive donde vive `SeatMapProjection`, se prueba sin
Razor, y **falla ruidosamente**: log + marca en el host, nunca un widget vacío en silencio.

### 3. La vista SynHost queda en dos líneas

Resolver → emitir. El diccionario libre desaparece de la vista.

### 4. El tipo TS se genera del record

Reemplaza a `elements-syn.contract.ts`. El sanitizador de cada elemento se tipa
`(raw: unknown) => KpiCardProps`: **leer una clave que el CMS no manda deja de compilar**. D1 se caza
al compilar.

### 5. Dos gates

- **C# ↔ TS por nombre de campo**, con el patrón de `ContractsIndexTests`.
- **Uno que ejecuta el sanitizador** de cada elemento con lo que emite su vista y exige que cada
  clave mueva la salida (informe 20 §5.D.3). Es el que ve lo que el tipado no ve: una clave que el
  CMS manda y el elemento acepta pero no usa.

### 6. `configOverride` fuera de las funcionalidades

Es la puerta por la que la configuración técnica entra desde el editor y pisa al record. En las
funcionalidades sale (o queda detrás de `DevSeed`, ADR 0013). En las piezas, qué hacer con él lo
decide el piloto.

### 7. El formato del cable no cambia

El emitter sigue escribiendo `<synergos-x config='{json}'>` más los scripts del registry y el import
map. Ni la CDN, ni Preact, ni el runtime se enteran. Cambia **quién arma el JSON y quién declara su
forma**. Lo que no está en el record deja de viajar (`culture` incluida: el bridge ya la lleva).

## Alternativas consideradas

| alternativa | por qué no |
|---|---|
| **Tipar los sanitizadores con el `Syn*Schema` que ya se genera de uSync** (la «forma barata» del informe 10 §5) | Ese schema describe el ElementType, no lo que viaja: 89/90 con propiedades fantasma y 23/91 con otro juego de claves ◐. Ataría 23 elementos a una forma equivocada. |
| **Dejar los diccionarios libres y endurecer el aviso de `cms:validate`** | Un aviso amarillo permanente convivió con los 43. Una advertencia que nadie puede poner en verde se deja de leer. |
| **Generar el record desde el TS** (la UI como fuente, que es como las ADR 0126/0127 leen la 0083: la UI elige el nombre de la clave) | No se descarta: el piloto decide la dirección. La síntesis eligió C# → TS porque quien llena el record es el resolver, que vive en el CMS. El nombre lo sigue eligiendo lo que el elemento lee; el record es el sitio donde queda escrito para que lo comprueben los dos compiladores. |
| **Copiar NewShore tal cual** | Se toma el resolver tipado. No su tabla de despacho (18 entradas apuntan a módulos que no existen, informe 15 D6), ni `data-module-path` atado a Angular, ni el shim `Html.Action` por reflexión con `.Result`, ni el fallo silencioso. El registry de Synergos ya resuelve mejor qué bundle monta cada nombre. |

## Consecuencias

**A favor**

- D1 deja de poder existir en los elementos migrados: la divergencia no compila.
- La vista deja de ser el sitio donde se decide la forma del payload.
- S11 obtiene, por elemento, la respuesta a *«¿qué dato pide, y es el mío?»* leyendo un tipo, no
  abriendo una vista.

**En contra**

- **Un fichero más por elemento**, y un resolver. Para una pieza de dos props es ceremonia; el piloto
  tiene que medir si compensa o si las piezas triviales se resuelven con un resolver genérico por
  record.
- **Cruza dos repos.** El CI del CMS no clona el hermano (lo dicen los `<remarks>` de
  `CifrasDeClaudeMdTests.La_cuenta_de_elementos_del_CDN_vive_en_un_solo_sitio`). Dónde vive el gate
  C# ↔ TS —en el CMS con el TS generado versionado en `docs/contracts/`, o en el UI leyendo un
  manifiesto— no está resuelto.
- **Migrar 91 vistas** es trabajo real, y durante la transición conviven dos formas de emitir.
- **Quitar `configOverride` de un ElementType es cambio de schema** (uSync, ADR 0008): lo importa el
  arquitecto, y el contenido que ya lo usa hay que medirlo antes (no medido: está en la base).

**Qué la vigilaría:** los dos gates de §5, más el existente `ContractsIndexTests` como molde. Ninguno
está construido.

## Qué hace falta para aceptarla (el piloto)

Cinco elementos, los de la ola 1 del plan de la auditoría: `kpi-card`, `dropdown`, `rating-stars`,
`tag`, `carousel`. Se mide:

1. **D1 cerrado en los cinco**, con la misma prueba que lo encontró: sanitizador ejecutado con el
   payload real de la vista, y en vivo **con consulta de control**.
2. **Los dos gates mutados**: renombrar un campo en el record → rojo; en el TS → rojo; una clave que
   se emite y no mueve la salida → rojo. Y comprobar que cada mutación entró.
3. **El HTML emitido no cambia** salvo las claves corregidas (el cable es el mismo).
4. **Cuánto cuesta un elemento nuevo** con el patrón, comparado con hoy.
5. **La dirección C# → TS o TS → C#**, y dónde vive el gate entre repos.

Si el piloto sale bien, la ADR pasa a Aceptado y enmienda las ADR 0015 §1 y 0096 §1 en lo que toca a
`configOverride` en las funcionalidades.

## Resultado del piloto (#173, 2026-09-29)

Piloto en `kpi-card`, `tag`, `rating-stars`, `dropdown` y `carousel` (los cinco con D1), en ramas
homónimas de CMS y UI.

**Los cinco criterios, cumplidos:**

1. **D1 cerrado en los cinco**, con la misma prueba que lo encontró —el sanitizador ejecutado con el
   `config` que emite la vista— **y en vivo con control**: con el `config` viejo de cada vista sale el
   defecto («— Sin dato», el tag sin chip, «0 de 5», el dropdown gris sin opciones, el carrusel oculto);
   con el nuevo, lo que escribió el editor.
2. **Los gates, mutados en los dos lados**: renombrar en el record, en el JSON o en el TS → rojo; una
   clave que viaja y ningún sanitizador lee → rojo; una clave que se lee y no viaja → `TS2339`. 21
   mutaciones, todas en rojo.
3. **El cable no cambió**: mismo tag, mismos scripts, mismo respaldo SSR. El HTML emitido cambia sólo en
   las claves corregidas y en que los campos vacíos ya no viajan como `null`.
4. **Coste medido**: el primero ≈ 57 min de agente (≈ 40 en lo genérico, que se hace una vez); los otros
   cuatro, **5-9 min** y **20-70 líneas de código** cada uno (record 3-6, resolver 10-35, vista 6; el
   resto son sus 4 tests por seam). El patrón no cambió entre el 2º y el 5º. Para las **31** piezas con D1
   que faltan (36 piezas con D1 menos las 5 del piloto; el informe del piloto decía 33, una resta mal
   hecha): ≈ 4-5 h de agente. Las 7 funcionalidades con D1 dependen antes de la ADR 0137.
5. **Dirección C# → TS**: `Synergos.CMS.Interfaces/SynHost/*Props.cs` →
   `docs/contracts/elementos-synhost.json` (derivado por el CMS) → `elementos-synhost.contract.ts`
   (generado por el UI con `npm run contratos:synhost`).

**Los cambios que el piloto pidió a esta ADR** (ratificados por el arquitecto el 2026-09-30; son parte
de la decisión):

1. **§6 — en las piezas, `configOverride` sólo pisa campos que el record declara**
   (`SolicitudSynHost.SoloLoDeclarado`). Antes de migrar las demás hay que **medir en la base** qué
   contenido usa `configOverride` con claves no declaradas: el piloto no tocó la SQLite.
   **Medido el 2026-09-30**, sobre una copia de la base local (la única que existe: el despliegue no
   está creado, #21), por dos caminos y con control: **ningún bloque, en ninguna versión, lleva
   `configOverride` ni `config`** (0 de 1.755 bloques recorridos en el JSON de 165 BlockGrid/BlockList
   actuales o publicados; 0 filas que lo mencionen en texto crudo, todas las versiones). El mismo
   recorrido sí encuentra `itemsJson` en 717 bloques y `heading` en 143. La regla no borra nada que un
   editor haya puesto.
2. **§4 — el `ejemplo` es parte del contrato**, y lo emite el CMS con su resolver y su emitter reales
   (`ContratoSynHostTests`); el gate del UI lo ejecuta. Tipar sólo caza la clave que se lee de más, no
   la que se tira.
3. **§5 — son dos gates**: records ↔ JSON en el CMS (en cada build) y JSON ↔ TS en el UI (G-11, con el
   CMS al lado, como la hipoteca). No va en el `npm test` del UI, que corre sin el hermano.
4. **§2 — registro por descubrimiento**, con gate de «exactamente un resolver por record» (un resolver
   sin registrar es un 500 que el build no ve).
5. **No hace falta un resolver genérico** para las piezas triviales: 20 líneas y su test.
6. **§7 — `culture`**: decidir aparte; hoy el gate la exime como envoltura.

Hallazgos del piloto que van a ticket aparte (H1-H9 del informe del piloto): `rating-stars` colocado es
interactivo por defecto y nadie escucha su evento; `carousel` promete un autoplay y enlaces que no
tiene; `tagColor` es texto libre donde debería ser un selector; `kpiTrend` describe «neutral» y el
DataType dice `flat`; el emitter escribe los nulos de un diccionario pese a `WhenWritingNull`.

## Relación con otras ADRs

- **0015** — se conserva el patrón SynHost y el atributo `config`; cambia quién arma su contenido.
- **0083** — el record no es un paquete compartido entre repos: se genera un fichero, como hoy
  `cms-sync` genera los `Syn*Schema`. Principio 2 intacto: uSync sigue mandando sobre el schema.
- **0096** — module-mount también hereda `configOverride`; misma enmienda para las funcionalidades.
- **0127** — `SeatMapProjection` es el precedente directo.
- **0134** — el record declara el tipo de colocable. **0136** — declara las secciones de diccionario.
  **0137** — el resolver es el único camino por el que la configuración de negocio llega al elemento.

## Referencias

- Informes locales de la auditoría: 20 §2.2-2.3 y §5.C, 16 §3-§7, 13 §0-§1, 15 §10.b, 10 §5.
