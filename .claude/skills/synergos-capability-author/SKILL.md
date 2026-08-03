---
name: synergos-capability-author
description: Crea o modifica una capacidad agnóstica del árbol de servicios (Synergos.Api.*) siguiendo EL MOLDE — las cuatro carpetas, las cinco formas de endpoint, las siete reglas de construcción y los dos gates que lo verifican. Conoce el filtro de atomicidad (¿puede decir NO sola? ¿es dueña de su almacén?), la regla del Ref opaco, el orden idempotencia-antes-que-estado, y la disciplina de mutar cada gate. Invocar ANTES de escribir una Synergos.Api.* nueva, al agregarle endpoints a una existente, o cuando alguien propone una capacidad y hay que decidir si de verdad lo es.
---

# SYNERGOS Capability Author — escribir una `Synergos.Api.*`

Las veinte capacidades existentes son **idénticas en forma y distintas solo en
lógica de negocio**. Eso no es estética: es lo que permite que un agente que
nunca vio `Api.Signing` sepa dónde está todo en treinta segundos. El molde lo
verifica `ApiMoldTests`, y la agnosticidad `BackendSegregationTests`.

Fuentes de verdad, en este orden:

| Pregunta | Dónde |
|---|---|
| ¿Esto es una capacidad? | `docs/product/07-diseno-atomico-capacidades.md` |
| ¿Cuál es el molde exacto? | `docs/product/08-despiece-apis.md` §4 |
| ¿Qué APIs necesita cada dominio? | `docs/product/08` §1–2 (la matriz 20×9) |
| ¿Por qué el backend está partido así? | `docs/product/06-arquitectura-backend.md` |

---

## 0. ANTES de escribir nada: el filtro de atomicidad

**La mayoría de las capacidades propuestas no son capacidades.** Dos preguntas,
y hay que pasar las dos:

1. **¿Puede decir NO sola?** Si todas sus reglas dependen de preguntarle a otro
   servicio, no tiene negocio propio: es un cliente HTTP con ínfulas.
2. **¿Es dueña de su almacén?** Si no guarda nada, **es un tipo, no un
   servicio**. `Money`, `TimeWindow` y `Ref` viven en `Synergos.Core` por esto.

Un tercer filtro, práctico: **¿cuántos de los nueve dominios la usan?** El
criterio que produjo las veinte fue el conteo de la matriz. Una capacidad con un
solo consumidor casi siempre es una feature de su BFF.

> Si la respuesta a alguna es «no», **decilo y parate ahí**. Proponer capacidades
> de más es el error caro de esta arquitectura: cada una suma un proceso, un
> almacén, un despliegue y una superficie que mantener para siempre.

---

## 1. El molde — las cuatro carpetas, sin excepciones

```
Synergos.Api.X/
├── Contracts/XContracts.cs     lo que cruza el cable (requests + responses)
├── Domain/X.cs                 los records del dominio + los enums
├── Domain/XRules.cs            LO QUE RECHAZA SOLA — el único sitio
├── Domain/XService.cs          compone reglas + almacén + reloj
├── Storage/XStore.cs           IXStore + FileSystemXStore
├── Endpoints/XEndpoints.cs     el ruteo, y NADA más
└── Program.cs                  el arranque
```

**Por qué `Contracts/` está separado de `Domain/`**: es lo que permite cambiar el
modelo interno sin romper a los clientes. Fusionarlos es cómodo el primer mes y
carísimo el segundo, porque cada renombre interno pasa a ser cambio de contrato.

`Synergos.Api.X.csproj` referencia **exactamente dos** proyectos:

```xml
<ProjectReference Include="..\Synergos.Core\Synergos.Core.csproj" />
<ProjectReference Include="..\Synergos.Shared\Synergos.Shared.csproj" />
```

Cualquier otra referencia rompe `BackendSegregationTests`.

---

## 2. Las siete reglas de construcción

1. **Todo bajo `/v1/`.** Sin excepciones, incluido lo nuevo.
2. **Solo `MapPost` y `MapGet`.** Nada de `MapPut`/`MapPatch`: un cambio de
   estado es un verbo del dominio (`/confirm`, `/release`, `/capture`), no una
   escritura de campos. El gate lo verifica.
3. **Ruteo solo en `Endpoints/`.** Un `MapPost` en otro fichero rompe el gate.
4. **`UseSharedKeyAuth` + `/health`** en `Program.cs`, siempre.
5. **Nada de `DateTimeOffset.UtcNow` fuera de `Program.cs`.** El reloj se inyecta
   (`TimeProvider`) o los tests no pueden mover el tiempo. Gate.
6. **Todo lo que llega es nullable.** Si un campo obligatorio fuera no-nullable,
   el binder de ASP.NET devolvería un 400 genérico **antes** de que la API pueda
   explicar qué falta, y el llamador se queda sin saber cuál de siete campos era.
7. **Un rechazo = un código estable.** `{prefijo}.{motivo_en_snake}`. El código
   lo compara una máquina; el `detail` lo lee una persona, en español.

### Las cinco formas de endpoint

| Forma | Ejemplo | Notas |
|---|---|---|
| crear | `POST /v1/cosas` | **exige `Idempotency-Key`** |
| leer una | `GET /v1/cosas/{id}` | |
| listar | `GET /v1/cosas?filtro=…` | **exige filtro**: sin él es un volcado por HTTP |
| verbo del dominio | `POST /v1/cosas/{id}/confirmar` | el cambio de estado |
| consulta derivada | `GET /v1/cosas/{id}/disponibilidad` | |

Y la sexta, obligatoria y aparte: `GET /health`.

---

## 3. Las cuatro reglas que se rompen sin darse cuenta

### 3.1 El `Ref` es opaco — se guarda y se devuelve, NUNCA se ramifica

```csharp
// PROHIBIDO, y hay gate:
if (subject.Kind == "salud.profesional") { … }
```

El día que una capacidad ramifica sobre `Ref.Kind`, deja de servirle al siguiente
dominio y se copia — que es de donde veníamos. El gate busca comparaciones contra
**cadena literal** (porque `Ref.Kind` es `string`); un `switch` sobre un enum de
dominio propio es legítimo y no se marca.

### 3.2 La idempotencia se resuelve ANTES que cualquier regla de estado

```csharp
// BIEN
if (_idempotency.Find("cosa", idem) is { } yaEra) return Devolver(yaEra);
var motivo = Rules.CheckCapacity(…);          // recién ahora

// MAL — defecto real, lo encontró un proceso vivo:
var motivo = Rules.CheckCapacity(…);          // el reintento choca con SU PROPIO hold
if (_idempotency.Find(…) is { } yaEra) …
```

Por eso `IIdempotencyLedger` está partido en `Find` y `Remember`, y no es un
`GetOrAdd`. Un test de esto con capacidad 5 **pasa por accidente**: usá 1.

### 3.3 Un listado sin filtro es un volcado

`GET /v1/cosas` sin criterio es la forma más cómoda de exfiltrar el rastro entero
de una operación. Rechazá con `{prefijo}.filter_required`.

### 3.4 Sin sustantivos de negocio

`Reservation` y `Order` son vocabulario legítimo de una capacidad. `Patient`,
`Tramite`, `Flight` y `Course` **no lo son nunca** — eso vive en su
`Synergos.Bff.*`. El gate `Ninguna_capacidad_nombra_un_negocio_concreto` mira
declaraciones de tipo, no comentarios: documentar por qué algo NO está no se
castiga.

---

## 4. El almacén

`JsonCollectionStore<T>` de `Synergos.Shared`, siempre, salvo motivo escrito.
Escritura atómica (temporal + move), caché en memoria, `lock` de proceso.

> **La limitación, que hay que decir de frente y no descubrir en producción:** el
> `lock` es de proceso. Dos instancias sobre el mismo directorio se pisan. Es
> aceptable con un despliegue único —el caso hoy— y es la primera razón para
> cambiar de almacén, no un detalle.

Y lo que **no** se negocia: **cada almacén es de UNA capacidad y nadie más lo
lee.** Ni un `JOIN`, ni un fichero compartido.

---

## 5. Los tests que la capacidad ship con ella

En `Synergos.CMS.Tests/Api/XRulesTests.cs` y `XServiceTests.cs`. Los cuatro casos
canónicos de ADR 0075 (empty / happy / filter / idempotent) **más** lo propio de
la capacidad. Y dos cosas que en este árbol no son opcionales:

1. **Mutá cada regla que escribiste.** Reintroducí el defecto, confirmá que el
   test se pone rojo, restaurá. Un test que nunca se vio fallar no prueba nada.
2. **Si el cambio cruza servicios, verificá con procesos reales.** Los dos
   defectos más caros de este repo los encontró un proceso vivo, no un test —
   porque los tests codificaban la misma suposición equivocada que el código.

Agregá el `ProjectReference` a `Synergos.CMS.Tests.csproj` (es el único proyecto
exento del gate CMS ⊥ API: probar la separación exige ver los dos lados).

---

## 6. Antes de dar por cerrado

```bash
dotnet build Synergos.CMS.sln -v quiet                       # 0 errores CS
dotnet test  Synergos.CMS.Tests/Synergos.CMS.Tests.csproj \
  --filter "FullyQualifiedName~Architecture"                 # molde + segregación
dotnet test  Synergos.CMS.sln -v quiet                       # la suite
```

Checklist:

- [ ] Pasó el filtro de atomicidad, y está escrito por qué
- [ ] Cuatro carpetas, dos referencias de proyecto
- [ ] Todo bajo `/v1/`, sin `MapPut`/`MapPatch`, ruteo solo en `Endpoints/`
- [ ] `UseSharedKeyAuth` + `/health`
- [ ] Reloj inyectado, cero `UtcNow` fuera de `Program.cs`
- [ ] Idempotencia **antes** de las reglas de estado
- [ ] Listados con filtro obligatorio
- [ ] Cero ramificación sobre `Ref.Kind`, cero sustantivos de negocio
- [ ] Tests + **mutación de cada uno**
- [ ] `Synergos.CMS.sln` actualizado y `CLAUDE.md` §2 y §11 al día
