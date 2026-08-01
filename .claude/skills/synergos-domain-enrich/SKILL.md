---
name: synergos-domain-enrich
description: Enriquece un dominio/vertical de Synergos a nivel best-in-class de forma COMPOSABLE y build-safe, con la receta de 3 capas derivada del enriquecimiento de Eventos (artist + highlights + sessions). Actívala cuando haya que darle más carne a la ficha o pantalla de un dominio (Eventos, Tienda, Healthcare, Propiedades, etc.): la UI Angular ya lee campos ricos pero el backend cae a fallbacks pobres, o quieres agregar contenido real es-CO a un stub. Cubre el orden obligatorio: (1) leer el model.ts + template Angular para saber QUÉ lee la UI y con qué guards, (2) Interfaces: records de dominio nuevos + campos OPCIONALES con default=null al record existente (aditivo → cero call-sites rotos, la clave del build-safe), (3) Application: poblar el stub con contenido real es-CO revisado por un crítico, (4) Web: DTOs + reshape del response + helpers null-safe emitiendo las claves EXACTAS del contrato (ADR 0083), (5) build cross-project + verificar API sin leaks + navegador. Respeta ADR 0002 (Application sin Umbraco) y complementa synergos-contract-drift.
model: claude-opus-4-8
---

# SYNERGOS Domain Enrich — subir un dominio a best-in-class, composable y build-safe

Receta para **enriquecer un vertical** (agregar campos de contenido que la ficha/pantalla
Angular ya sabe mostrar pero que el backend no emite) sin romper build ni contratos.
Destilada del enriquecimiento de **Eventos** (commit `feat(eventos): enriquecer ficha
con artista + highlights + agenda` — hallable con `git log --grep "enriquecer ficha"`):
3 capas tocadas de forma **aditiva, cero call-sites rotos**.

El síntoma que dispara esta skill: la UI tiene un normalizador defensivo que **cae a
fallbacks pobres** (p.ej. `artist.name = título del evento`, secciones ocultas porque
`highlights.length === 0`). El objetivo: que el **dominio** provea el contenido real y la
pantalla quede llena — sin tocar el módulo Angular ni inventar un adapter nuevo.

> Regla de oro: **el enriquecimiento es ADITIVO y viaja de la UI hacia atrás.** Primero
> confirmas qué lee la UI; luego bajas por Interfaces → Application → Web. Nunca al revés.

---

## 0. Prerequisitos

- Stack corriendo para verificar en vivo (usa **synergos-run-dev**): CMS en
  `http://synergos.local:5000` + el dev server / bundle del módulo del dominio.
- Saber a qué **seam de catálogo/dominio** pertenece la pantalla. Para Eventos es
  `IEventCatalogProvider` (Interfaces) con default `StubEventCatalogProvider`
  (Application). Cada vertical tiene su análogo (`IRoomAvailabilityProvider`,
  `IProductCatalogProvider`, `IFlightAvailabilityProvider`…). El controller Web
  (`{Dominio}Controller`) es el que mapea a DTOs JSON.
- Este flujo respeta el grafo `Interfaces ← Application ← Web` (ADR 0002): la lógica y
  el contenido viven en Interfaces/Application, **jamás** en el controller. Application
  no referencia `Umbraco.Cms.*` ni `Microsoft.AspNetCore.*`.
- Complementa **synergos-contract-drift**: el normalizador cliente ya defiende contra
  claves faltantes, pero eso NO te exime de emitir las claves reales (§4, ADR 0083).

**Rutas base (ejemplo Eventos):**

```powershell
$repoRoot   = "C:\Users\HITMA\Desktop\synergos"
$iface      = "$repoRoot\Synergos.CMS\Synergos.CMS.Interfaces\IEventCatalogProvider.cs"
$stub       = "$repoRoot\Synergos.CMS\Synergos.CMS.Application\Services\Impl\StubEventCatalogProvider.cs"
$controller = "$repoRoot\Synergos.CMS\Synergos.CMS.Web\Controllers\EventosController.cs"
$uiModel    = "$repoRoot\Synergos.UI\platforms\angular\apps\elements\modules\eventos\src\eventos\eventos.model.ts"
$uiClient   = "$repoRoot\Synergos.UI\platforms\angular\apps\elements\modules\eventos\src\eventos\eventos-api.client.ts"
```

---

## 1. Capa UI (leer, NO tocar) — el contrato manda

La UI es la **fuente de verdad de las claves** (memoria
`feedback_parallel_agents_contract_and_input_race`). Antes de escribir una línea de C#,
abre el `*.model.ts` y el `*-api.client.ts` del módulo y responde tres preguntas:

1. **¿Qué campos lee la ficha?** — busca las interfaces del detalle. En Eventos
   (`eventos.model.ts`): `EventDetail` declara `readonly highlights: readonly string[]`,
   `readonly sessions: readonly EventSession[]`, `readonly artist: EventArtist`. Y los
   records anidados: `EventArtist { name; headline; followers: number }`,
   `EventSession { id; time; title; speaker }`.
2. **¿Con qué GUARD se muestran?** — el template renderiza la sección solo si hay datos
   (`@if (highlights.length > 0)`, `@if (sessions.length > 0)`). Vacío = sección oculta.
   Esto es lo que hace el enriquecimiento **seguro de omitir**: si un evento no trae
   highlights, la sección simplemente no aparece.
3. **¿A qué fallback cae el normalizador?** — en `eventos-api.client.ts`
   (`normalizeDetail`): `artist.name = readString(artist['name']).trim() || event.title`
   y `highlights = readStringArray(value['highlights'])` (vacío si falta). Ese fallback
   es exactamente el "contenido pobre" que vamos a reemplazar.

> Anota las **claves JSON exactas** (camelCase de la UI): `highlights`, `sessions`,
> `artist.name`, `artist.headline`, `artist.followers`. Son el destino del reshape del §4.

**No edites el módulo Angular.** Si el enriquecimiento requiere una clave que la UI aún
no lee, eso es otra tarea (cambio de contrato UI-first, no un enrich backend).

---

## 2. Capa Interfaces — records nuevos + campos OPCIONALES (aditivo)

Aquí está la clave del **build-safe**. En `Synergos.CMS.Interfaces/I{Dominio}...Provider.cs`:

### 2.1 Records de dominio nuevos

Declara un record por concepto, con XML-doc editor-facing. Del commit real:

```csharp
/// <summary>
/// El acto/artista protagonista de la ficha (perfil "Artista"). Adaptado al tipo
/// de evento: headliner (música), keynote (conferencia), compañía (teatro), etc.
/// </summary>
public sealed record EventArtist(string Name, string Headline, int Followers);

/// <summary>
/// Una entrada de la agenda del evento (sección "Agenda"): hora + qué pasa + quién.
/// </summary>
public sealed record EventSession(string Id, string Time, string Title, string Speaker);
```

### 2.2 Campos opcionales al record existente — el patrón crítico

Al record de detalle YA existente (`EventDetail`) le agregas los campos nuevos **como
parámetros opcionales con `= null`, al final de la lista posicional**:

```csharp
public sealed record EventDetail(
    EventSummary Summary,
    string Description,
    string Organizer,
    IReadOnlyList<EventTier> Tiers,
    EventSeatMap? SeatMap,
    EventArtist? Artist = null,                 // ← nuevo, opcional
    IReadOnlyList<string>? Highlights = null,   // ← nuevo, opcional
    IReadOnlyList<EventSession>? Sessions = null); // ← nuevo, opcional
```

**Por qué esto es build-safe (y por qué importa):** `EventDetail` tiene múltiples
call-sites — el stub lo construye 4 veces, `PublishEventAsync` lo recibe, los tests lo
arman. Un parámetro **posicional opcional con default** NO rompe ninguna de esas
llamadas existentes: siguen compilando idénticas y reciben `null`. Solo el call-site que
QUIERES enriquecer (el stub) pasa los valores nuevos. Cero cambios forzados en cascada.

Documenta la opcionalidad en el XML-doc del record (calca el original):

```csharp
/// <see cref="Artist"/>, <see cref="Highlights"/> y <see cref="Sessions"/> alimentan
/// los bloques artista / "por qué asistir" / agenda (opcionales — null/vacío los oculta).
```

---

## 3. Capa Application — poblar el stub con contenido real es-CO

En `Synergos.CMS.Application/Services/Impl/Stub{Dominio}...Provider.cs` llenas los campos
nuevos en cada entrada sembrada. Este es el **único** call-site que pasa los valores.

```csharp
Artist: new EventArtist("Cordillera Eléctrica",
    "Headliner · fusión andina y electrónica en vivo", 328_000),
Highlights: new[]
{
    "Dos escenarios en paralelo, sin cruces de set",
    "Más de 20 artistas nacionales e internacionales",
    "Cierre estelar con Cordillera Eléctrica",
    "E-ticket QR y check-in ágil en el ingreso",
},
Sessions: new[]
{
    new EventSession("fest-s1", "14:00", "Apertura de puertas y zona de food trucks", ""),
    new EventSession("fest-s2", "16:00", "Bandas emergentes en el Escenario Norte", "La Sonora Bogotá"),
    new EventSession("fest-s5", "22:00", "Cierre estelar en el Escenario Sur", "Cordillera Eléctrica"),
}
```

### 3.1 Calidad del contenido — el paso del "crítico"

El contenido NO es relleno; es lo que hace la demo best-in-class. El enriquecimiento de
Eventos pasó por revisión de un crítico editorial. Checklist antes de dar por bueno el
contenido:

| Criterio | Qué verificar |
|---|---|
| **Coherencia interna** | Los `Sessions` cuadran con el evento (escenarios, horas ascendentes, `speaker` concreto). El `Artist.headline` calza con la `Category`. |
| **es-CO real** | Ciudades/venues reales (Parque Simón Bolívar, Teatro Metropolitano), tono local. |
| **Sin anglicismos** | "plan Pro" en vez de "Pro plan"; "talleres" no "workshops". |
| **Sin relleno genérico** | Nada de "Una experiencia inolvidable". Cada highlight aporta un dato distinto. |
| **Sin redundancia léxica** | No repetir la misma palabra clave en 3 highlights. |
| **Marcar que es DEMO** | El XML-doc del stub debe decir que es contenido sembrado (ver §5, no inventar data "real" sin marcarla). |

Los campos que no tienen fuente en el dominio se dejan vacíos (`speaker: ""`), no se
inventan. `Followers` es un aproximado plausible, no un dato "verificado".

---

## 4. Capa Web — DTOs + reshape + helpers null-safe

En `{Dominio}Controller.cs`: (a) DTOs nuevos, (b) el response emite los campos con las
**claves exactas** del §1, (c) helpers null-safe traducen dominio → DTO.

### 4.1 DTOs (calcan las claves de la UI)

```csharp
// Contrato UI (EventArtist): la ficha lee artist.{name,headline,followers}.
public sealed record EventArtistDto(string Name, string Headline, int Followers);
// Contrato UI (EventSession): la agenda lee session.{id,time,title,speaker}.
public sealed record EventSessionDto(string Id, string Time, string Title, string Speaker);
```

Y el response gana los campos (comenta el guard de la UI para el próximo que lea):

```csharp
public sealed record EventDetailResponse(
    EventSummaryDto Event,
    string Description,
    // Contrato UI: la ficha lee `highlights` (string[]) y `sessions` (agenda).
    // Vacíos ocultan la sección (el template hace .length>0).
    IReadOnlyList<string> Highlights,
    IReadOnlyList<EventSessionDto> Sessions,
    EventArtistDto Artist,
    /* …resto igual… */);
```

### 4.2 Reshape del response — emite SIEMPRE las claves reales

En el action, mapea el dominio al DTO, colapsando `null` → vacío (nunca dejes que un
`null` del dominio llegue como `null`/ausente al JSON):

```csharp
return Ok(new EventDetailResponse(
    Event: ToSummaryDto(detail.Summary),
    Description: detail.Description,
    Highlights: detail.Highlights ?? Array.Empty<string>(),
    Sessions: (detail.Sessions ?? Array.Empty<EventSession>()).Select(ToSessionDto).ToList(),
    Artist: ToArtistDto(detail.Artist),
    /* … */));
```

### 4.3 Helpers null-safe

```csharp
// Artista: null-safe. Si el evento no lo provee, cadena vacía → el normalizador
// cliente cae a event.title (contrato UI: artist.name || event.title).
private static EventArtistDto ToArtistDto(EventArtist? a) =>
    a is null
        ? new EventArtistDto(string.Empty, string.Empty, 0)
        : new EventArtistDto(a.Name, a.Headline, a.Followers);

private static EventSessionDto ToSessionDto(EventSession s) =>
    new(s.Id, s.Time, s.Title, s.Speaker);
```

> **Por qué emitir la clave aunque la UI defienda (ADR 0083):** el normalizador tiene
> `artist.name || event.title` y `readStringArray(highlights)`, así que técnicamente un
> `null` no rompe. Pero el **contrato JSON estable** exige la clave presente: hace el
> response auto-descriptivo, evita que el próximo consumer (otro módulo, un test de
> contrato) tenga que adivinar, y desacopla el backend de los fallbacks internos de UN
> cliente. La defensa del normalizador es red de seguridad, no permiso para omitir.

---

## 5. Verificar — build cross-project + API sin leaks + navegador

```powershell
# 5.1 Application compila clean (0 warnings CS):
dotnet build "$repoRoot\Synergos.CMS\Synergos.CMS.Application\Synergos.CMS.Application.csproj" -v quiet

# 5.2 Web compila cross-project (si el Web NO corre, sin --no-dependencies para validar
#      que Interfaces+Application enlazan). Con Web corriendo, MSB3021 file-lock es esperado:
dotnet build "$repoRoot\Synergos.CMS\Synergos.CMS.Web\Synergos.CMS.Web.csproj" -v quiet
```

```powershell
# 5.3 API sin leaks: la clave debe existir y venir poblada (no null / no NaN / no undefined):
$r = Invoke-RestMethod "http://synergos.local:5000/api/eventos/event/evt-festival-estereo"
$r.artist; $r.highlights; $r.sessions
# Sanity: artist.name != título del evento (si son iguales, el fallback ganó → algo no se emitió)
```

- **Navegador:** abre la ficha del dominio y confirma que las secciones nuevas
  renderizan (perfil artista + seguidores, lista "por qué asistir", Agenda). Verifica un
  caso con datos (festival) y uno sin (para ver la sección ocultarse limpia). Cero
  `undefined` / `NaN` / secciones vacías con borde.
- **Regresión de temas:** si el enrich toca estilos, verifica en los 7 temas por-siteRoot
  (memoria `feedback_verify_all_siteroot_themes`). Para enrich de solo-contenido no aplica.
- **Cierre:** commit atómico `feat({dominio}): enriquecer …` (nunca mezclar con refactor)
  y, si abriste Ola, ciérrala con **synergos-ola-close**.

---

## 6. Qué NO hacer

| ❌ Anti-patrón | ✅ En su lugar |
|---|---|
| Agregar un parámetro **NO opcional** (sin `= null`) a un record de dominio con múltiples call-sites | Siempre opcional con default → aditivo, cero call-sites rotos (§2.2). Es LA regla build-safe. |
| Poner el parámetro nuevo **en medio** de la lista posicional del record | Al **final**, después de los existentes → no desplaza argumentos de las llamadas actuales. |
| Hardcodear el contenido (highlights, nombres, agenda) en el **DTO o el controller** | El contenido vive en el **stub / dominio** (Application). El controller SOLO reshape (ADR 0002). |
| Emitir `null` / omitir la clave "porque el normalizador ya defiende" | Emite la clave real, colapsando null→vacío (`?? Array.Empty<>()`, helper null-safe). ADR 0083. |
| Inventar datos y presentarlos como reales | Es contenido **DEMO sembrado**; márcalo en el XML-doc del stub. `speaker` sin fuente = `""`, no inventado. |
| Editar el `*.model.ts` / template Angular para "que calce con el backend" | La UI es la fuente de verdad de claves. El backend se adapta a ella, no al revés. |
| Empezar por el controller | Empieza por leer la UI (§1) → Interfaces → Application → Web. El flujo va UI-hacia-atrás. |
| Contenido genérico / con anglicismos / redundante | Pásalo por el checklist del crítico (§3.1) antes de commitear. |
| Meter lógica de negocio en Application con `using Umbraco.Cms.*` | Application es lógica pura (ADR 0002). Contenido y records, nada de Umbraco/AspNetCore. |

---

## 7. Checklist de una tanda de enrich

1. [ ] Leí `*.model.ts` + `*-api.client.ts`: tengo las claves exactas y los guards (`.length`).
2. [ ] Interfaces: records nuevos + campos **opcionales `= null` al final** del record de detalle.
3. [ ] Application: stub poblado con contenido es-CO real, pasado por el checklist del crítico (§3.1).
4. [ ] Web: DTOs con claves-contrato + reshape con `?? Array.Empty<>()` + helpers null-safe.
5. [ ] `dotnet build` Application + Web → 0 CS errors.
6. [ ] API: la clave existe y viene poblada; `artist.name` != título (no ganó el fallback).
7. [ ] Navegador: secciones renderizan con datos y se ocultan limpio sin datos; cero leaks.
8. [ ] Commit atómico `feat({dominio}): …`; si es Ola, **synergos-ola-close**.

**Skills relacionadas:** synergos-contract-drift (la defensa del normalizador que
complementa este flujo) · synergos-run-dev (levantar stack) · synergos-element-inventory
(ver estado de un dominio antes de enriquecer) · synergos-test-author (tests del seam si
el enrich agrega comportamiento, ADR 0075) · synergos-smoke-test (verificación post-deploy).