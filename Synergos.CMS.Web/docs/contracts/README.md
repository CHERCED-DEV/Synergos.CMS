# Synergos contracts — CMS ↔ UI alignment

- **Status:** Living document — cap-220 (Olas 211-220).
- **Audience:** Both Synergos.CMS y Synergos.UI maintainers.
- **Scope:** Contratos de integración entre el CMS host (Razor SSR)
  y los Web Components del UI (Angular custom elements).

## Premisa

CMS y UI viven en repos potencialmente separados. La única superficie
de acoplamiento son los **contratos** documentados aquí. Cada lado
implementa contra estos contratos sin importar código del otro.

```
┌──────────────────────────────────┐         ┌──────────────────────────────────┐
│  Synergos.CMS (Umbraco + Razor)  │         │  Synergos.UI (Angular elements)  │
│                                  │         │                                  │
│  Emits HTML con <synergos-X>     │ ──HTTP─►│  Bundles publicados al CDN       │
│  Inyecta window.synergos bridge  │         │  Custom elements hidratan        │
│  Lee bundle registry             │ ◄──────│  Emiten CustomEvents al host     │
│  Renderiza tokens CSS            │         │  Consumen tokens via :root       │
└──────────────────────────────────┘         └──────────────────────────────────┘
                ▲                                         ▲
                │                                         │
                └──────── Contratos (esta carpeta) ───────┘
```

## Los 5 contratos

| # | Doc | Propósito | Owner del schema |
|---|---|---|---|
| 1 | [`cdn-bundle-structure.md`](cdn-bundle-structure.md) | La forma de las rutas del CDN —`{tag}/{framework}/{versión}/`— que `IBundleRegistryClient` resuelve. | Joint (el CDN produce, el CMS consume) |
| 2 | [`dom-events.md`](dom-events.md) | CustomEvents que los `<synergos-*>` emiten + payload schemas. CMS escucha si necesita. | UI team |
| 3 | [`css-tokens.md`](css-tokens.md) | Las `--syn-*` custom properties que el CMS publica vía `<head>` y que el UI puede asumir. UI declara fallbacks. | CMS host (source of truth) |
| 4 | [`i18n-bridge.md`](i18n-bridge.md) | `window.synergos.i18n.t(key, fallback)` global que el UI consume. CMS popula via Razor partial. | CMS (server-side resolution) |
| 5 | [`host-bridge.md`](host-bridge.md) | Big picture: cómo los 4 anteriores se conectan en runtime. Init order + lifecycle. | Joint |

> **Dónde se verifica cada uno, porque no es donde parece.** El harness Vitest de
> `tests/` cubre **cuatro** —tokens CSS, eventos DOM, puente de host, i18n—, que son
> los de comportamiento en el navegador. El **primero no está ahí y no le falta**:
> es una forma de rutas, y quien la verifica es el lado C# que las resuelve
> (`HttpBundleRegistryClientTests`, `FileSystemBundleRegistryClientTests`,
> `BundleRegistryProbeTests`).
>
> Se dice porque «los 5 contratos + un harness» se lee como «los cinco están en el
> harness», y entonces alguien abre `tests/`, cuenta cuatro, y va a escribir un
> spec que duplicaría lo que ya cubre la suite.

## Los fixtures — lo que los dos árboles EJECUTAN

Los cinco de arriba son documentos. Acá viven además los ficheros de datos que **las
dos implementaciones corren**, que es otra cosa: un documento lo lee una persona, un
fixture lo ejecuta un gate de cada lado.

| Fichero | Qué cruza | Quién lo ejecuta |
|---|---|---|
| [`mortgage-vectors.json`](mortgage-vectors.json) | La calculadora de hipoteca del vertical Propiedades: las dos implementaciones tienen que dar **la misma cuota al centavo** para el mismo cuerpo. | CMS: `HipotecaVectoresTests` (por el borde, con su conversión) · UI: el spec de `mortgage.calc` |
| [`service-fee-vectors.json`](service-fee-vectors.json) | La comisión de servicio de Eventos (ADR 0137): el carrito que la **muestra** y los dos motores que la **cobran** tienen que dar la misma al centavo, con el redondeo de la casa (al par). | CMS: `NegocioDeEventosTests` (el motor en proceso) · orquestador: `ComisionDeServicioTests` · UI: el spec de la comisión del elemento `eventos` |
| [`minor-units-vectors.json`](minor-units-vectors.json) | Las **unidades menores** de un importe (#196, G-13): todo campo `*Minor` de una API va en las unidades menores de SU moneda (COP: centavos), con la tabla de ISO-4217 ESCRITA en los dos lados —no leída de `Intl`, que cambia según el motor— y el redondeo de la casa (al par). Lo que el servidor emite y lo que la UI divide para pintar tienen que dar lo mismo. | CMS: `UnidadesMenoresTests` (`UnidadesMenores`, con el que emiten gov y salud) · UI: `tools/vectores-unidades-menores.mjs` (`aMenores`/`desdeMenores` de `vitals/core/src/formato`, compilados) |
| [`elementos-synhost.json`](elementos-synhost.json) | **Lo que viaja** en el `config` de cada elemento con resolver tipado (ADR 0135): sus campos con nombre del cable, tipo y si son contenido, decisión o negocio, sus secciones de diccionario, y el `config` EXACTO que emite su vista para una muestra autorada. **Generado** de los records `[ElementoSynHost]`: no se edita a mano. | CMS: `ContratoSynHostTests` (records ↔ fichero, y el ejemplo emitido por el resolver y el emitter reales) · UI: `tools/contrato-synhost.mjs --check` (fichero ↔ tipo TS generado) y el spec que **ejecuta** el sanitizador de cada elemento con el ejemplo |

> **Por qué existe el primero, y por qué no era un documento.** `IMortgageCalculator` afirmaba en
> su `<remarks>` que «el cálculo base es el mismo en cliente y servidor» y era **falso**
> desde que existe el endpoint (#167): las dos eran la misma fórmula con la tasa a
> **100×** de distancia —el borde la leía como fracción y la app la mandaba en
> porcentaje— así que `POST /api/realty/mortgage` contestaba **240.000.000** al mes
> donde la pantalla pinta **2.642.606,72**. Una frase en prosa no lo habría cambiado:
> la frase ya estaba escrita. Lo que hacía falta es que las dos lo **ejecuten**.
>
> Y sus expectativas **no salen de ninguna de las dos**: se derivaron de la fórmula
> cerrada del sistema francés con aritmética decimal de 50 dígitos. Un fixture sacado
> de una implementación es una FOTO — detecta que se separan, no que las dos están mal
> a la vez.

## El contrato HTTP publicado — `openapi/` (ADR 0140)

El documento OpenAPI 3.1 de cada pieza del árbol de servicios que publica contrato. **Generado**
del código —los metadatos de los endpoints del host real, con `Microsoft.AspNetCore.OpenApi`
SÓLO en `Synergos.Servicios.Tests`—: no se edita a mano. El nombre del fichero es el del
ensamblado, igual que su `info.title`.

| Fichero | Pieza | Quién lo ejecuta |
|---|---|---|
| [`openapi/Synergos.Bff.Eventos.json`](openapi/Synergos.Bff.Eventos.json) | El orquestador de Eventos: el contrato del flujo de compra. El único que el UI convierte en tipos (el front conoce el contrato del flujo, no el de las capacidades). | CMS: los cuatro gates de abajo · UI: el generador de tipos de los `Synergos.Bff.*` (repo hermano) |
| [`openapi/Synergos.Api.Pricing.json`](openapi/Synergos.Api.Pricing.json) | La capacidad de precios y cotizaciones. | CMS: los cuatro gates de abajo + `ContratoConsumidorEventosTests` |
| [`openapi/Synergos.Api.Inventory.json`](openapi/Synergos.Api.Inventory.json) | La capacidad de existencias y apartados. | CMS: los cuatro gates de abajo + `ContratoConsumidorEventosTests` |
| [`openapi/Synergos.Api.Payments.json`](openapi/Synergos.Api.Payments.json) | La capacidad de cobros. | CMS: los cuatro gates de abajo + `ContratoConsumidorEventosTests` |

Los cuatro los vigilan, en `Synergos.Servicios.Tests`:

- **`ContratoOpenApiTests`** — la deriva: regenera desde el host real y compara byte a byte (y en
  `openapi/` hay un documento por pieza, ninguno más).
- **`SueloDelContratoTests`** — lo que regenerar NO arregla: `operationId` único, un 2xx JSON con
  esquema por operación, ningún número que también sea cadena, ningún anulable requerido en una
  petición, `Rechazo` y el 401 presentes.
- **`RespuestaPorElTipoDeRetornoTests`** — en el host real, la respuesta de cada operación la
  declara SÓLO su tipo de retorno: ni `IResult`/`object`, ni `[ProducesResponseType]`/`[Produces]`,
  ni un `.Produces` escondido en un ayudante. Lo escrito a mano deja el documento mintiendo con la
  deriva en verde, porque regenerarlo copia la mentira.
- **`SondasDelContratoTests`** — lo que el documento declara a mano, contra el host real:
  `Idempotency-Key` declarada ⇔ exigida —la opcional con el cuerpo que la activa, que el documento
  no sabe decir y lleva una tabla de la sonda—, y su `maxLength` es el largo que el endpoint acepta
  (una llave de ese largo pasa, una más larga sale con `400 *.idempotency_key_required`); todo
  rechazo real cumple `Rechazo`, `X-Synergos-Identity` declarada ⇔ leída (un token que no lo es
  contesta `identity.*`), y sin `X-Synergos-Key` toda operación contesta 401 sin cuerpo. En
  `BuyTickets` el largo es menor que los 128 de una capacidad porque la llave abre la saga y de
  ella cuelgan las de cada paso (`LlaveDeSaga`, en `Bff.Core`, hace la cuenta).

Y los tres de capacidad, además, **`ContratoConsumidorEventosTests`**: lo que `Bff.Eventos` manda
y lee de cada una cabe en su documento (ruta, query, llave, cuerpo y respuesta, con nombres
exactos, y el `format` además del `type`: una fecha que pasa a texto libre sólo pierde su
`date-time`). Así un renombre en cualquiera de los dos lados deja de ser un default silencioso.

Para regenerarlos, después de cambiar un record de `Contracts/` o un endpoint:

```bash
SYNERGOS_ACTUALIZAR_CONTRATOS=1 dotnet test backend/Synergos.Servicios.Tests --filter ContratoOpenApi
```

Son deterministas a propósito —sin `servers`, sin `tags`, sin descripciones sacadas de
comentarios, en orden ordinal, LF y sin BOM— para que el gate pueda comparar byte a byte en
Windows y en el CI. Lo único que puede moverlos sin tocar código es un parche del runtime .NET 10:
el rojo del gate imprime la versión.

### Lo que el documento NO dice

Se escribe porque un contrato que calla algo se lee como si lo dijera:

- **Los códigos de rechazo.** Toda operación que puede rechazar publica los seis estados
  (400/403/404/409/410/503, de `RejectionResults.StatusCodeFor`) con el esquema `Rechazo`
  —ProblemDetails con `code: string` y `transient: boolean`—, pero no QUÉ códigos
  (`pricing.bad_subject`…): no se derivan del tipo, y un orquestador reenvía los de sus
  capacidades y sintetiza `{capacidad}.unreachable|empty_response|unknown|unparseable`. Un GET
  documenta un 409 que no puede dar: sobra, no falta.
- **Lo que el negocio exige.** `required` describe la forma del cable: en una petición, un
  anulable nunca es requerido aunque el dominio lo necesite (`eventId`, `lines`…). Lo que exige el
  negocio lo dice el 400 con su código; y lo prueban los tests contra host real, no el esquema.
- **Los 400 y 415 del framework.** Un JSON malformado contesta 400 `text/plain` (vacío en
  Production) y otro `Content-Type` contesta 415, ninguno con `Rechazo`. Un consumidor tiene que
  tolerar un 4xx sin `code`, como ya hace `CapabilityHttp`.
- **Lo que no es del contrato de la pieza**: `/health` (es del molde) y el webhook de la pasarela
  en Payments (lo llama un tercero que firma, no un consumidor con la llave).
- **Todavía no es el contrato del navegador.** `Synergos.Bff.Eventos.json` publica también lo que
  pone la puerta (`buyerKind`, `buyerId`, `serviceFeePercent`) y operaciones de operación
  (`RetryTicketPurchase`, `ListCompensations`). Separarlos es de la F3 (ADR 0140, la puerta): el DOM
  no es frontera de confianza.

## Naming conventions canónicas

| Asset | Convention | Example |
|---|---|---|
| Custom element tag | `synergos-{kebab-case}` | `<synergos-accordion>` |
| Schema alias (CMS) | `elementSyn{PascalCase}` | `elementSynAccordion` |
| Bundle path on CDN | `/elements/{alias}/{version}/main.js` | `/elements/elementSynAccordion/1.4.2/main.js` |
| CSS token | `--syn-{category}-{descriptor}` | `--syn-color-brand-500` |
| Dictionary key (i18n) | `{Section}.{SubSection}.{Key}` PascalCase | `Admin.Action.Approve` |
| CustomEvent name | `syn:{component}:{event}` | `syn:accordion:opened` |
| Window namespace | `window.synergos.*` | `window.synergos.i18n.t(...)` |

## Reglas de no-acoplamiento

❌ **El UI NO importa código del CMS** (sin shared TS package, sin
gRPC stubs, etc.).
❌ **El CMS NO importa código del UI** (sin npm install del UI).
❌ **Cero shared NuGet/npm package compartido** — solo contratos en
markdown + JSON Schema cuando aplique.
✅ **Single source of truth** del schema vive en el CMS uSync XMLs.
El UI espeja via `element-registry.json` exportado (process futuro:
generation script que lee uSync y emite el JSON).
✅ **Cambios de contracts** se proponen en este folder + ADR antes
de implementar. Compatibilidad backward-first.

## Versioning de los contratos

Cada doc lleva un `Contract version: vN` en su header. Bumps:

- **Patch** (typo, clarification): no bump.
- **Minor** (additive, e.g. nuevo CustomEvent): bump.
- **Major** (breaking): nuevo doc + ADR superseding.

CMS y UI commit de adoption del contract version en sus respectivos
CHANGELOGs.

## Bootstrapping

Para nuevos developers:

1. Lee este README.
2. Lee `host-bridge.md` para entender el flow runtime end-to-end.
3. Para tu cambio específico, lee el contract doc relevante.
4. Si tu cambio rompe un contract version: nuevo ADR.

## References

- ADR 0012 — CDN contract is consumed, not owned.
- ADR 0015 — SynHost framework-agnostic integration.
- ADR 0083 — CMS↔UI alignment via contracts (este cap).
- `feedback_synhost_naming_convention` (memory).
- `feedback_framework_agnostic_integration` (memory).
