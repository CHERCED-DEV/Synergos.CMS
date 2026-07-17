# ADR 0107 — Motor de catálogo transversal en memoria (`ICatalogIndex<T>` + descriptores declarativos); Examine diferido con umbral de reapertura (T5 Ola 0, doc 25)

- **Status:** Accepted
- **Date:** 2026-07-16
- **Deciders:** Arquitecto + agente, fase de lógica de negocio (doc rector `25`, transversal T5). El arquitecto **firmó explícitamente la desviación del doc rector** (el rótulo de T5 dice "Examine") y acotó el alcance a la Ola 0. Diseño producido por un panel multi-agente; endurecido por **tres revisiones adversariales** que encontraron bugs reales en el trabajo del propio agente.
- **Relacionados:** ADR 0105 (`IJsonEntityStore` — el precedente exacto: colapsar N copias de una capacidad transversal en UNA seam), ADR 0083 (contratos CMS↔UI — la UI es la fuente de verdad), ADR 0031 (Examine para el search de CONTENIDO, que sigue vigente y no se toca), ADR 0002 (Application sin Umbraco/AspNetCore), ADR 0075 (tests por seam), ADR 0001 (Umbraco 13 pinned — la razón por la que Examine 4.x no está disponible). Regla de oro doc 25: ninguna capacidad transversal se implementa dos veces.

---

## Context

El doc 25 rotula T5 como **"Búsqueda/índice real (Examine)"**. Al leer el código, ese
rótulo resultó ser un **nombre equivocado**, y conviene decirlo sin adornos:

- Para el **contenido del sitio**, Examine ya está vivo y hecho desde la Ola 86
  (`ExamineSearchProvider` / `ISearchQuery`, ADR 0031). No hay hueco.
- Para los **catálogos de dominio** (Tienda, Eventos, Propiedades, Trámites, Educación),
  el hueco NO es de motor: es de **duplicación** y de **origen de datos**. Los 5
  implementaban cada uno su propio matching, su propio facetado y su propio orden — 5
  copias de la misma capacidad, con 3 firmas distintas de `SearchAsync` y 2 records de
  faceta gemelos. Es el mismo cuadro que ADR 0105 encontró en storage.

**Y había un bug funcional VIVO que ninguna discusión arquitectónica debía tapar.** Los 5
catálogos filtraban con `.Contains(text, OrdinalIgnoreCase)`, que pliega mayúsculas pero
**no tildes**, sobre un seed es-CO cargado de ellas. Verificado en vivo antes de escribir
una línea:

```
Tienda    "Tecnología"    → 2 resultados  |  "tecnologia"    → 0
Eventos   "Bogotá"        → 2             |  "bogota"        → 0
Props     "Medellín"      → 2             |  "medellin"      → 0
Gobierno  "Registraduría" → 1             |  "registraduria" → 0
```

Un usuario colombiano escribiendo desde el móvil —donde la tilde exige long-press y casi
nadie la pone— **no encontraba nada**. En 5 verticales. La demo estaba rota y nadie lo
sabía.

Había además dos bugs de multi-valor, también en silencio: `?brand=Aurora,Barista` → **0**
teniendo 1 y 1 (filtrar por dos marcas devolvía menos que por una), y `?minRating=4.5,4.0`
hacía `double.TryParse("4.5,4.0")`, que falla, y el filtro **moría sin aplicarse** con los
dos chips encendidos.

## Decision

### El motor es en memoria, y Examine queda DIFERIDO con umbral escrito

`ICatalogIndex<T>` (Interfaces) + `InMemoryCatalogIndex<T>` (Application, C# puro): barrido
lineal, ranking ponderado y facetas drill-down. UNA impl para los 5 verticales.

**Esto no es sobre-ingeniería inversa; el caso está construido con evidencia:**

- **A este volumen, Lucene es el desvío.** ~24 ítems por vertical. El propio
  `IShopQuery.cs:14` pone el umbral de un índice en "10k+". El barrido lineal a 24 ítems
  son microsegundos.
- **Examine 3.7.1 NO tiene facetado first-class.** `WithFacets`/`IFacetResult`/`FacetValue`
  no existen en los binarios; llegó en 4.x = Umbraco 14+ = fuera por ADR 0001. El conteo
  habría que escribirlo en memoria **igual**.
- **`productPriceBase` es `Umbraco.TextBox`** → un `price-asc` en Lucene ordenaría
  lexicográficamente (`'89000' < '9000'`), **en silencio**.
- **Tres de los cinco verticales tienen `Publish*`** y Examine indexa asíncrono: el
  organizador publicaría un evento y su propia búsqueda no lo devolvería. Regresión
  visible en demo que ningún build verde atrapa.
- **El ExternalIndex indexa TODOS los `productPage`**, y esos los comparten Tienda, Booking
  y Propiedades → `__NodeTypeAlias:productPage` serviría apartamentos en `/api/shop/search`.
- Lo único que Lucene compraba gratis (plegado de diacríticos) son ~40 líneas.

**A este volumen el motor en memoria es el subconjunto estricto del trabajo que Examine
exigiría de todos modos.** El swap queda abierto: cambia la impl de `ICatalogIndex<T>` y
nada más.

**CRITERIO DE REAPERTURA, escrito para que la decisión se revise por dato y no por opinión:
>5.000 ítems por vertical, o p95 de `/api/shop/search` >50ms.** Si se reabre, confirmar
ANTES con un spike COMPILABLE la firma exacta de `AddExamineLuceneIndex<TIndex,TDirectoryFactory>`
— hoy solo está verificada la EXISTENCIA de los símbolos, no la firma.

### El comportamiento se DECLARA en un descriptor; el motor no se toca

`CatalogDescriptor<T>` declara por vertical: campos buscables con peso, facetas, sorts y
orden por defecto. ~20 líneas de declaración, cero lógica.

**La línea es dura: si hay que tocar el motor para acomodar un vertical, el descriptor está
mal modelado.** Se cumplió: los 5 verticales entraron sin una sola modificación al motor.

Los filtros son **polimórficos** (`CatalogTermFilter` / `CatalogThresholdFilter` /
`CatalogRangeFilter`), no un `switch (Kind)` dentro del motor: las tres semánticas difieren
tanto en el match como en cómo derivan sus chips (un término los descubre del dato, un
umbral los tiene declarados). Añadir un Kind es una subclase.

### La forma del contrato NO se inventó: es la de la UI

`CatalogQuery(Text, Filters, Sort, Skip, Take)` es calco de `DiscoveryCriteria`
(`discovery-shell.ts:54-58`), ya probado en 6 verticales. **Y ahí está el hallazgo que
ordena todo:** la UI SIEMPRE habló multi-valor (`facets: Record<string, readonly string[]>`,
con `page`) mientras el backend la estrechaba a `IReadOnlyDictionary<string,string>` — un
valor por clave. **La pérdida del multi-select era estructural y ocurría en la frontera**:
el tipo no podía expresar `brand IN (a,b)`. Esto hace que el backend hable el idioma de la
UI (ADR 0083). **CSV es la convención del wire** porque la UI ya serializa
`values.join(',')`.

### La ñ se PRESERVA; las tildes se pliegan

`CatalogText.Fold` recorta, quita diacríticos y baja a minúsculas — pero **protege la ñ con
un centinela PUA (U+E000) y compone a FormC antes de descomponer a FormD**.

Es una decisión de idioma, no un detalle: **la ñ es una letra propia del español, no un
acento**. Se pliega lo que el usuario NO escribe (la tilde exige long-press en móvil) y se
respeta lo que SÍ escribe (la ñ tiene tecla propia). Plegarla colisionaría `"año"` con
`"ano"`, inaceptable en es-CO.

Se pliega a mano con `FormD` y no con `CompareOptions.IgnoreNonSpace` porque esa exige ICU
y este entorno ya demostró globalización frágil (los 9 tests rojos pre-existentes de formato
es-CO). `FormD` es determinista.

### Facetas: drill-down y conteo sobre el universo, no sobre la página

- **Drill-down:** cada faceta se cuenta excluyéndose a sí misma. Sin esto, encender
  "Aurora" colapsa la columna Marca a un solo chip y el multi-select que la UI promete es
  inalcanzable.
- **Sobre el universo filtrado, NUNCA sobre la página:** con 30 ítems y `take=5` el chip
  dice 30. Es exactamente lo que Lucene no da sin materializar todo.
- **El texto SÍ acota el universo de las facetas:** buscar "laptop" y ver "Barista (3)"
  sería mentira.
- **Un umbral con varios valores gana el MENOS restrictivo** ("4+" y "3+" = "3+"), no se
  descarta la faceta.

### Degradar antes que romper

Filtro desconocido, sort desconocido, valor no parseable, `NaN`/`Infinity`, UTF-16
malformado y tope de config absurdo: **se ignoran o se sanean, nunca vacían el listado ni
dan 400**. Un `?color=rojo` pegado a una URL vieja no debe romper la página. `Take` se capa
en vez de rechazarse: `?take=1000000` es un cliente torpe, no un ataque.

**Excepción declarada:** las 4 seams que prometen TODAS las coincidencias (Trámites,
Propiedades, Eventos, Educación) usan `CatalogSettings.Unpaged`. El tope protege el WIRE, y
esas seams no están expuestas a un `?take=`: para ellas materializar todo ES el contrato.
Sin esto, un catálogo de más de 96 ítems perdía el resto **en silencio** — y tres de esos
verticales crecen en runtime con `Publish*`.

### El orden es TOTAL

Todo orden cierra por `Id`. Sin desempate total, dos ítems empatados salen en orden distinto
entre requests idénticos y **la paginación baila**: un ítem aparece en la página 1 y en la
2, y otro en ninguna.

Sin texto manda el **orden histórico de cada vertical**, declarado en su descriptor, para que
la demo no cambie de aspecto salvo que se escriba texto.

### Acotar por siteRoot es de la FUENTE, no del motor

`CatalogQuery` **no tiene `Scope`**, y es deliberado. Acotar es decidir QUÉ ÍTEMS existen, y
eso es de `ICatalogSource<T>.GetAllAsync(scope)`. Un `Scope` en la query daría dos sitios
donde acotar y uno que no acota: el motor no tiene de dónde leer el siteRoot de un `T`
cualquiera, así que lo ignoraría y `/api/shop/search` seguiría sirviendo apartamentos EN
SILENCIO. **Un campo que promete aislamiento y no lo da es peor que no tenerlo.**

Por la misma razón `ICatalogSource<T>` **no tiene `Version`**: documentaba una caché que no
existe. Sin caché, el read-your-writes sale gratis — cada búsqueda repide los ítems, y el
`Publish*` se ve en la misma pantalla.

## Consequences

**Positivas:**

- **El bug de tildes está muerto en los 7 buscadores** (los 5 catálogos + pacientes de
  Healthcare + cuerpo de posts de Blogs, que el barrido destapó). Verificado en vivo con
  query de control.
- **Las 5 copias del matching están muertas.** Grep de `MatchesText`/`BuildFacet` en los
  catálogos → cero.
- **Multi-select vivo** en marca Y categoría. Verificado clicando en el navegador: 6 →
  Aurora 1 → +Barista 2; 6 → Hogar 2 → +Deportes 4.
- **Paginación real** (la UI ya mandaba `?page=` y el backend lo ignoraba) y **chips
  legibles** ("4 estrellas o más" en vez de "4.0" — la UI ya leía `label` y caía al valor
  crudo).
- **Prefijo**: "zapa" encuentra "zapatos". Ni `.Contains` ni el `GroupedOr` de Examine lo dan.
- **Mueren 3 estáticos mutables** de proceso al pasar el matching al motor puro.
- El motor es **Singleton-safe por construcción**, no por disciplina: es una función pura de
  (ítems, query).

**Negativas o trade-offs:**

- **No hay índice invertido:** a >5.000 ítems por vertical esto se degrada. Es el umbral de
  reapertura escrito arriba, y es una apuesta consciente contra un futuro que hoy no existe.
- **El matching cambia de semántica:** AND entre tokens (antes OR plano) y prefijo. Con
  texto manda la relevancia, no el orden histórico. Cae dentro de la licencia acordada ("la
  demo no cambia de aspecto salvo que se escriba texto") pero es un cambio real.
- **`CatalogFacetKind` viaja al wire y nadie lo lee:** el `discovery-shell` pinta toda faceta
  como checkbox. Por eso `category` se declaró `MultiSelect` (la verdad de lo que ocurre) en
  vez de `SingleSelect` (que sería decorativo). Deuda anotada.
- **Propiedades: los chips de `beds` cambian** de conteos exactos a umbrales ("3+
  habitaciones"). Es lo que el filtro (`>=`) siempre hizo — el chip mentía — pero es un
  cambio visible. Se cae el chip "0", que como umbral no significaba nada.
- **La faceta `city` de Propiedades no hace drill-down** porque `Location` se pre-filtra
  antes del motor (busca por substring sobre ciudad O barrio, que una faceta de igualdad
  exacta no puede expresar). No es regresión: el código viejo colapsaba igual.

**Notas de implementación:**

- **Tres revisiones adversariales encontraron bugs REALES del agente**, varios confirmados
  ejecutando el código. Vale la pena registrarlos porque son el tipo de defecto que un build
  verde jamás atrapa: (1) `CatalogQuery.Scope` estaba **muerto** —el xmldoc prometía cerrar
  la mezcla cross-vertical y el motor nunca lo leía—; (2) la **asimetría del Trim** —el
  motor recortaba el valor seleccionado pero no el del dato, así que un `brand = "Aurora "`
  pintaba su chip "Aurora (1)" y al pulsarlo devolvía CERO—; (3) el **CSV de `category`** —
  el único filtro fuera del splitter, con el mismo bug que se acababa de "cerrar" para
  `brand`—; (4) el **label del chip de habitaciones** — se cambió una mentira de conteo por
  una de etiqueta.
- **La lección estructural:** los 17 tests del provider de Tienda estaban verdes porque le
  entregaban la lista YA PARTIDA — probaban la seam, no la **frontera**. Los bugs vivían
  entre el query string y la seam, donde no había ni un test. De ahí nace
  `ShopCatalogSearchTests`.
- **Verificación:** un `[FromQuery]` mal nombrado **no falla, devuelve TODO**. Toda cifra
  en vivo exige una query de CONTROL (`?q=xxnoexiste` → 0) antes de creerse nada. Esto
  produjo dos verificaciones falsas durante la ola.
- **Educación es el vertical delicado:** su composer de dos pasos (registro del tipo
  concreto + forward + property injection de `EnrollmentMetrics` en la MISMA instancia) NO
  se tocó. "Limpiarlo" al estilo de Tienda haría que el DI eligiera otro ctor y la inyección
  aterrizara en otra instancia → panel del instructor con 0 alumnos y $0, en silencio y **con
  los tests en verde** (los tests cablean la inyección ellos mismos).
- **Drift documental corregido de paso:** ADR 0031:28 y `SeamComposer.cs:464` dicen "Examine
  3.1.0"; el build resuelve **3.7.1** (transitivo por `CentralPackageTransitivePinningEnabled`).

## Alternatives considered

- **Índice Examine/Lucene custom alimentado por `IIndexPopulator`** (la lectura literal del
  doc 25). Rechazado: ~2.000 líneas de infraestructura para indexar 29 ítems hardcodeados,
  de las que ~70% se tiran el día del content-first. Arrastra 4 modos de fallo (índice
  ausente = PLP VACÍA en demo, peor que un 500; sort lexicográfico; read-your-writes roto;
  mezcla cross-vertical) y **no resuelve el facetado**, que en 3.7.1 hay que escribir en
  memoria igual. Diferido con umbral, no descartado.
- **Motor en memoria pero sobre el hardcode, sin `ICatalogSource`.** Rechazado a medias: el
  motor es idéntico, pero sin la seam de origen la mudanza a contenido (Ola A) sería otro
  refactor en vez de un swap de una línea en el composer.
- **`CompareOptions.IgnoreNonSpace` para el plegado.** Rechazado: exige ICU y este entorno
  ya demostró globalización frágil.
- **Plegar la ñ junto con las tildes** (lo que `FormD` hace por defecto). Rechazado:
  colisiona `"año"` con `"ano"`.
- **Añadir un Kind "Ceiling" al motor** para el `maxPrice` de Propiedades. Rechazado: habría
  sido tocar el motor para acomodar un vertical. Se pre-filtra en la fachada, que es lo que
  el contrato manda.

## References

- Doc rector: `refactor-docs/architecture/25-punto-estabilizacion.md` (§ transversal T5).
- Diseño: `scratchpad/t5-design.md` (panel multi-agente, plan de 15 pasos).
- Contrato de la UI: `Synergos.UI/platforms/angular/libs/shells/src/discovery/discovery-shell.ts:54-58`.
- Umbral de Lucene declarado por el propio proyecto: `Synergos.CMS.Interfaces/IShopQuery.cs:14`.
- Commits: `c065eda` (tildes) · `a666fc3` (desacople del id) · `8605628` (contrato) ·
  `8cf80d9` (motor) · `a180e5a` (endurecido) · `4e06dcf` (Tienda piloto) · `852291b` (CSV de
  category) · `d6c3214` (los 4 restantes) · `b7a662b` (label del chip).
