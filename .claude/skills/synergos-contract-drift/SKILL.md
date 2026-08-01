---
name: synergos-contract-drift
description: Diagnostica y arregla DRIFT de contrato entre el backend CMS (controllers ASP.NET + DTOs record en Synergos.CMS.Web/Controllers/) y las apps Angular custom-element (Synergos.UI/platforms/angular/apps/elements/modules/<app>/). Actívala cuando una ficha/tarjeta de una app (eventos, realty, storefront, academy, ehr, gov, booking, blogs) muestra datos vacíos, precio en 0 o Gratis erróneo, subtítulos con separador colgante, mapa sin pines, o campos por defecto — y sospechas que el JSON del endpoint no trae las claves que la UI lee. Cubre el procedimiento completo, (1) hallar las claves que la UI lee en <app>.model.ts + normalizeX() del <app>-api.client.ts; (2) compararlas con lo que emite el DTO record del controller; (3) evaluar la severidad REAL contra el normalizador defensivo del cliente (crash vs fallback pobre vs cosmético por data faltante — casi nada crashea porque cada cliente hace value[uiKey] ?? value[legacyKey]); (4) reshape backend build-safe (agregar campos, [property JsonPropertyName] para la clave exacta, conservar campos legacy, params opcionales con default en records de dominio); (5) verificar vía curl al endpoint + scan de leaks y en navegador. La UI es la fuente de verdad (ADR 0083); el backend DEBE emitir la clave que la UI lee — NO confiar en el fallback del cliente como si fuera el arreglo.
model: claude-opus-4-8
---

# synergos-contract-drift

Diagnóstico y corrección de **drift de contrato** CMS(Razor/C#) ↔ UI(Angular).

**Regla rectora (ADR 0083):** la UI es la **fuente de verdad**. El backend
mapea a **DTOs JSON estables** con las claves que la UI ya lee. No hay shared
code package entre repos; la única superficie de acople son los contratos y el
shape del JSON runtime. Un drift = el DTO backend emite una clave distinta (o no
la emite) de la que la UI consume.

**Antiregla capital:** cada app Angular tiene un `normalizeX()` **defensivo** que
hace `value['uiKey'] ?? value['legacyKey']` y rellena defaults (`''`, `0`, `[]`).
Eso significa que **la mayoría de los drifts NO crashean la vista** — el
normalizador los tapa. Pero tapado ≠ arreglado: la dirección canónica sigue
siendo emitir la clave correcta desde el backend. No trates el `?? fallback`
como el fix.

## 0. Prerrequisitos

- Repo backend: `C:\Users\HITMA\Desktop\synergos\Synergos.CMS\Synergos.CMS.Web\`.
- Repo UI: `C:\Users\HITMA\Desktop\synergos\Synergos.UI\platforms\angular\apps\elements\modules\<app>\`.
- Para verificar vía HTTP el CMS debe estar corriendo en
  `http://synergos.local:5000` (usa **synergos-run-dev** si no lo está).
- Contratos canónicos: `Synergos.CMS.Web/docs/contracts/` (README + dom-events +
  css-tokens + i18n-bridge + host-bridge). ADR: `docs/adr/0083-cms-ui-alignment-via-contracts.md`.
- Memoria relacionada: `feedback_cms_ui_contracts_alignment` (naming canónico +
  realidad del normalizador), `feedback_parallel_agents_contract_and_input_race`
  (UI = fuente de verdad; backend reshape).

Convención de nombres de archivo por app (verificado en `eventos` y `realty`):

| Pieza | Ruta |
|---|---|
| Controller + DTOs | `Synergos.CMS.Web/Controllers/<App>Controller.cs` |
| Interfaces UI (claves que lee) | `Synergos.UI/.../modules/<app>/src/<app>/<app>.model.ts` |
| Cliente HTTP + `normalizeX()` | `Synergos.UI/.../modules/<app>/src/<app>/<app>-api.client.ts` |

Rutas de API confirmadas: `[Route("api/eventos")]`, `[Route("api/realty")]`. El
patrón `api/<app>` se repite en los controllers verificados; aun así, **confirma
el `[Route(...)]` del controller específico** antes de asumirlo (algunas apps
—Shop, Travel— exponen sub-rutas como `api/shop/search`, `api/travel/search/hotel`).

## 1. Hallar las claves que la UI lee (dos fuentes, no una)

La UI declara su contrato en **dos** lugares y hay que leer **ambos**:

1. **`<app>.model.ts`** — las `interface` TS = las claves esperadas y sus tipos.
   Ej. `EventSummary` lee `fromAmount:number`, `startsAt:string`, `venueName`,
   `cover`, `badges:string[]`, `subtitle`, `status`. `TicketTier` lee `id`,
   `amount`, `remaining`, `maxPerOrder`, `perks:string[]`, `zoneId?`.

2. **`normalizeX()` en `<app>-api.client.ts`** — la lectura REAL, con los
   fallbacks. Aquí ves qué clave prefiere y a cuál cae:

```ts
// eventos-api.client.ts — normalizeEvent()
fromAmount: readNumber(value['fromAmount'] ?? value['price'] ?? value['amount']),
venueName:  readString(value['venueName'] ?? value['venue']).trim(),
startsAt:   readString(value['startsAt'] ?? value['startDate'] ?? value['date']).trim(),
cover:      readString(value['cover']).trim() || readString(value['image']).trim(),
// normalizeTier()
amount:     readNumber(value['amount'] ?? value['price']),
// normalizeDetail()
artist.name: readString(artist['name']).trim() || event.title,   // fallback POBRE
```

**Lectura clave:** la primera clave del `??` es la **canónica** (la que el
backend debe emitir); las siguientes son legacy/compat. Si el backend solo emite
la legacy, funciona pero está driftado. Si no emite ninguna, cae al default
(`0`, `''`, `[]`) o a un fallback pobre (p.ej. `artist.name → event.title`).

> Grep útil: `grep -nE "value\['|\?\?" <app>-api.client.ts` lista todas las
> claves leídas y sus fallbacks de un vistazo.

## 2. Comparar contra lo que emite el DTO backend

Abre `<App>Controller.cs` y localiza:
- El **mapper** `To<X>Dto(...)` — qué campos rellena y con qué fuente de dominio.
- El **record DTO** `public sealed record <X>Dto(...)` — las claves emitidas
  (recuerda: System.Text.Json serializa camelCase por defecto salvo
  `[property: JsonPropertyName(...)]`).

Cruza cada clave de la lista del paso 1 contra el DTO. Marca cada una como:

| Estado | Significado |
|---|---|
| **MATCH** | el DTO emite la clave canónica que la UI lee. Nada que hacer. |
| **LEGACY-ONLY** | el DTO emite solo la clave legacy del `??`. Funciona por fallback; agregar la canónica. |
| **MISSING** | el DTO no emite ninguna variante. La UI cae a default/fallback pobre. |
| **CASE/NAME** | el DTO emite la clave pero con casing distinto al contrato (p.ej. `seatMap` vs `seatmap`). Necesita `JsonPropertyName`. |
| **VOCAB** | el valor difiere del vocabulario UI (p.ej. `"venta"` vs `"sale"`). Reshape en el mapper, no en el DTO. |

Ejemplos reales ya resueltos en el repo (úsalos de patrón):
- **eventos** `EventSummaryDto`: emite `startUtc` **y** `startsAt`, `imageUrl` **y**
  `cover`, `priceFrom` **y** `fromAmount` — el segundo de cada par es la clave que
  la UI lee; el primero se conserva para consumers previos.
- **eventos** venue anidado: `[property: JsonPropertyName("seatmap")]` (minúscula)
  y `[property: JsonPropertyName("rowNumber")]` calcan el contrato exacto que
  lee `<synergos-seat-map>`.
- **realty** `ListingDto`: emite `geo` (LocationDto) y `specs` (SpecsDto)
  anidados + `subtitle`/`badges` derivados; `MapOperation` traduce
  `venta→sale`, `arriendo→rent` (VOCAB).

## 3. Evaluar la severidad REAL (contra el normalizador)

**No asumas crash.** Antes de clasificar, lee el `normalizeX()` y decide:

| Severidad | Síntoma | Causa | Acción |
|---|---|---|---|
| **CRASH** | la ficha no renderiza / excepción JS | la UI lee una clave **sin** guard ni `??` (raro — casi todo está guardado) | fix backend URGENTE |
| **FALLBACK-POBRE** | dato equivocado pero visible (p.ej. "Artista: <título del evento>", precio "Gratis" por `0`) | backend MISSING → normalizador cae a otra clave/default | fix backend (emitir la clave) + si el dato no existe en dominio, enriquecer dominio/stub |
| **COSMÉTICO / DATA** | subtítulo " · ", mapa sin pines, chip ausente | el campo **existe** en el contrato pero el **dominio** no tiene la fuente → default legítimo | NO es drift de contrato; se arregla enriqueciendo dominio+stub, no el DTO |
| **LEGACY-OK** | todo se ve bien | backend emite legacy, normalizador la toma vía `??` | additivo: emitir también la canónica; baja prioridad |

Regla de oro de la memoria `feedback_cms_ui_contracts_alignment`: *me equivoqué
asumiendo crash en eventos — el cliente lo cubría*. **Verifica el `normalizeX`
ANTES de declarar severidad.** Los nits que quedan tras el normalizador suelen
ser de DATA (dominio/stub sin el campo), no de contrato.

## 4. Reshape backend build-safe

Principios (todos verificables en `EventosController.cs` / `RealtyController.cs`):

1. **Additivo, nunca rompedor.** Agrega la clave canónica; **conserva** la
   legacy en el mismo DTO. Ambas portan el mismo valor. Así no rompes consumers
   previos (ADR 0083 §Backward compatibility mandatoria).

   ```csharp
   private EventTierDto ToTierDto(EventTier t) => new(
       Id: t.Code,        // la UI lee tier.id (checkout); mismo valor que code
       Code: t.Code,      // legacy conservado
       Name: t.Name,
       Amount: t.Price,   // la UI lee tier.amount (major units)
       Price: t.Price,    // legacy conservado
       ...);
   ```

2. **`[property: JsonPropertyName("claveExacta")]`** cuando el casing camelCase
   por defecto no coincide con el contrato. El default daría `seatMap`; el
   contrato es `seatmap` → fíjalo explícito:

   ```csharp
   [property: JsonPropertyName("seatmap")] EventSeatMapDto? SeatMap,
   [property: JsonPropertyName("venue")]   EventVenueDto?   Venue);
   ```

3. **Null-safe en los mappers.** Si el dominio no provee el objeto, emite un DTO
   con campos vacíos en vez de `null` cuando la UI espera un objeto:

   ```csharp
   private static EventArtistDto ToArtistDto(EventArtist? a) =>
       a is null ? new EventArtistDto(string.Empty, string.Empty, 0)
                 : new EventArtistDto(a.Name, a.Headline, a.Followers);
   ```

4. **Derivaciones en helpers**, no en el DTO. Subtítulos/badges/status/vocabulario
   se calculan en métodos `private static` (`BuildSubtitle`, `BuildBadges`,
   `MapOperation`, `DeriveEventStatus`) y se pasan al DTO ya listos.

5. **Params opcionales con default en records de dominio.** Si el reshape exige
   un campo nuevo en un record de `Application`/dominio y no quieres tocar todos
   los call-sites, agrégalo como parámetro **opcional con default**
   (`string? Agency = null, double? Rating = null` — patrón real en `AgentDto`).
   Así el build queda verde sin editar cada constructor existente.

6. **Grafo de dependencias.** El reshape vive en la capa **Web** (controller). No
   metas `Umbraco.Cms.*` ni lógica de presentación en `Application` (ADR 0002).

Build de verificación (desde `C:\Users\HITMA\Desktop\synergos`):

```powershell
dotnet build Synergos.CMS\Synergos.CMS.Web\Synergos.CMS.Web.csproj -v quiet --no-dependencies
# Esperar 0 errores CS. Los MSB3021 (file-lock) son esperados si el Web corre.
```

## 5. Verificar (API + navegador)

### 5.1 Vía HTTP — la forma REAL del JSON

```powershell
# La forma cruda del endpoint (confirma que la clave canónica sale):
curl.exe -s "http://synergos.local:5000/api/eventos/event/EVT-1" | ConvertFrom-Json | ConvertTo-Json -Depth 6

# Confirmar una clave puntual (ej. que 'seatmap' minúscula existe y 'fromAmount' viene):
curl.exe -s "http://synergos.local:5000/api/eventos/events?q=" |
  Select-String -Pattern '"fromAmount"','"startsAt"','"cover"' -AllMatches
```

Bash equivalente (agente):

```bash
curl -s "http://synergos.local:5000/api/eventos/event/EVT-1" | python -m json.tool | head -60
```

**Scan de leaks** — que el DTO no filtre campos internos/dominio no contratados
(precios en minor units crudos, flags internas, PII). Revisa que las claves
emitidas ⊆ contrato de la UI + legacy documentados. Si aparece una clave que ni
la UI ni el contrato conocen, quítala del DTO.

### 5.2 En navegador — que hidrata y muestra el dato

Usa **synergos-app-verify** (o synergos-smoke-test) para abrir la app en vivo,
confirmar que el custom element `<synergos-<app>>` hidrata (no queda como
comentario HTML) y que el campo antes vacío ahora muestra el dato real. Chequea
la consola: el cliente loguea `Eventos API "<endpoint>" unavailable — using mock
data.` cuando cae al fallback; si ves ese warn, el endpoint no respondió y estás
viendo mock, no tu fix.

> Recuerda verificar en los 7 temas por-siteRoot si el drift afecta algo visual
> (ver `feedback_verify_all_siteroot_themes`).

## 6. Qué NO hacer

| ❌ No hagas | ✅ En su lugar |
|---|---|
| Tratar el `?? fallback` del cliente como el arreglo | Emitir la clave canónica desde el DTO backend (ADR 0083); el fallback es red de seguridad, no el fix |
| Renombrar el campo del DTO al nombre UI **sin** `[property: JsonPropertyName]` | Cambiar el nombre C# es libre, pero fija la clave JSON con `JsonPropertyName` a la que lee la UI |
| Quitar el campo legacy al agregar la clave nueva | Conservar ambos en el mismo DTO (additivo, backward-compat); portan el mismo valor |
| Editar `normalizeX()` para que lea la clave que el backend ya emite | La UI es la fuente de verdad; se mueve el backend, no el contrato de la UI |
| Asumir que un drift crashea la ficha | Leer `normalizeX()` primero: casi todo está guardado con `??`/defaults |
| Cambiar la firma de un record de dominio rompiendo call-sites | Parámetro opcional con default (`= null`/`= 0`) para build verde |
| "Arreglar" un campo COSMÉTICO/DATA tocando el DTO | Si el dominio no tiene la fuente, enriquecer dominio+stub; el contrato ya está bien |
| Meter lógica de reshape/presentación en `Application` | Vive en el controller (capa Web); `Application` no referencia Umbraco/ASP.NET (ADR 0002) |
| Serializar precios en minor units crudos o filtrar campos internos | Emitir major units + `PriceFormatted` (IPriceFormatter es-CO) y solo las claves contratadas |
| Verificar solo con `dotnet build` verde | Build verde no atrapa drift de claves JSON — verificar `curl` + navegador |

## 7. Checklist de cierre

1. `<app>.model.ts` + `normalizeX()` leídos → lista de claves canónicas.
2. DTO backend cruzado → cada clave MATCH / LEGACY-ONLY / MISSING / CASE / VOCAB.
3. Severidad decidida contra el normalizador (no asumida).
4. Reshape additivo aplicado (canónica + legacy, `JsonPropertyName` donde toque).
5. `dotnet build` Web → 0 CS.
6. `curl` al endpoint → clave canónica presente, sin leaks.
7. Navegador (synergos-app-verify) → hidrata + dato real, sin warn de mock.
8. Si tocaste algo estructural, actualiza §11.x en
   `refactor-docs/architecture/00-current-state-synergos-cms.md`.