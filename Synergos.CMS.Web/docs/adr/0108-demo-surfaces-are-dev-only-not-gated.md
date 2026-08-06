# ADR 0108 — Las superficies de DEMO no se gatean: no se despliegan (`[DevSeedOnly]` en `/api/ehr`; por qué el demo NO adopta `IPhiAccessGuard`)

- **Status:** Accepted
- **Date:** 2026-07-16
- **Deciders:** Arquitecto + agente. Originado por un reporte de seguridad ("fuga de PHI: anónimo obtiene el censo de pacientes en `/api/ehr`") cuya **premisa resultó falsa al verificarla en vivo**. El arquitecto devolvió la pregunta de negocio ("¿cuál es la mejor forma?") tras ver que el modelo de acceso ya estaba decidido en ADR 0098; el agente recomendó y ejecutó. Verificado en vivo contra el CMS corriendo, encendiendo y apagando el flag.
- **Relacionados:** ADR 0013 (cero seeders; tooling dev-only tras `Synergos:DevSeed:Enabled` — este ADR **extiende su alcance** de "tooling que muta datos" a "superficies que SIRVEN datos sembrados"), ADR 0098 (`IPhiAccessGuard` — el gate de PHI de producción, que este ADR deliberadamente NO reusa en el demo), ADR 0107 (el principio que decide este caso: un campo/promesa que nadie cumple se BORRA, porque el siguiente confía y el fallo es silencioso), ADR 0002 (Application sin Umbraco/AspNetCore), ADR 0075 (tests por seam).

---

## Context

Un reporte señaló que `EhrController` (`/api/ehr`, 18 endpoints) no tiene **ninguna**
guarda: ni `[Authorize]`, ni gate de member, ni ownership. Eso es literalmente cierto:

```
GET /api/ehr/patients   → 200 {"patients":[{"name":"Andrés Pardo","document":"CC 1.094.220.515",
                                            "bloodType":"AB-",...}]}
```

El reporte concluía "anónimo obtiene el censo completo de pacientes con PHI". La
conclusión HTTP es correcta; **la premisa sobre la data no**. Tres cosas que el
encuadre daba por ciertas y no lo eran:

1. **No es PHI.** El padrón sale de `EhrDemoSeed` en memoria: 5 pacientes fabricados,
   emails `@example.co`. No hay DB, no hay persona real.
2. **El PHI real ya está protegido.** Vive en `/api/healthcare`
   (`HealthcareApiController`), que invoca `IPhiAccessGuard` **primero** en cada
   endpoint, fail-closed. Verificado en vivo: `GET /api/healthcare/patients` → **401**
   anónimo.
3. **El "rol clínico" no era una decisión de negocio pendiente.** Ya está tomada en
   ADR 0098: `DefaultPhiAccessGuard` reconoce `doctor` / `nurse` / `reception`, más
   pertenencia y consentimiento.

El reporte también pedía verificar que anónimo recibiera **403**. El patrón vigente
(`ShopCatalogController`, `HealthcareApiController`) da **401** a anónimo y reserva el
403 para *member autenticado ajeno*. Verificar 403 en anónimo habría sido verificar lo
incorrecto.

Calibrada la data, el riesgo real no es de hoy — es **latente y diferido**, y estaba
escrito en el propio código. El xmldoc de `StubPatientRegistry` prometía:

> "el adapter real (HIS/DB) reemplaza el seam sin tocar el controller"

Y `SeamComposer.cs:663` registra el stub **incondicionalmente**, sin flag. O sea: el
día que alguien enchufe un padrón real detrás de ese seam —haciendo exactamente lo que
el xmldoc invita a hacer— publica el censo entero a cualquier anónimo, **con build
verde y sin que el diff toque una sola línea del controller**. Esa frase es el defecto.

## Decision

### Una superficie de demo no se gatea: se hace imposible de desplegar

`EhrController` lleva `[DevSeedOnly]`: existe solo con `Synergos:DevSeed:Enabled=true`
y responde 404 con el flag off (que es el default de `appsettings.json`; el `true` vive
solo en `appsettings.Development.json`).

La razón por la que esto —y no un gate— es la respuesta correcta: `/api/ehr` **ya tiene
un gemelo de producción**. El xmldoc del propio controller lo dice ("Es la capa de DEMO
… DISTINTA del núcleo PHI de producción"). Cuando existen dos APIs clínicas y una es la
real, la otra no debe servir data real *nunca*; entonces gatearla es resolver el
problema equivocado. La decisión no es "quién puede leer el demo" sino "el demo no
llega a producción".

Esta elección es **dominante bajo las dos ramas del futuro**: si `/api/ehr` sigue siendo
demo, es la respuesta final; si algún día se vuelve la app clínica real, sigue siendo
correcta hoy (corta el riesgo a costo cero) y entonces se hace la partición de
superficies **deliberadamente**, no atornillada.

### El demo NO adopta `IPhiAccessGuard`, y la razón es concreta

`AccessCheckRequest.TargetPatientKey` es `Guid?`. Los ids del EHR son strings
(`pat-andres-pardo`). Adoptar el guard obliga a una de dos, y las dos son malas:

- **Mutar el contrato de ADR 0098** → tocar código PHI de producción para acomodar un
  demo. Dirección equivocada: el demo no puede imponerle forma al núcleo real.
- **Pasar `null`** → te quedas con rol + auditoría pero **pierdes pertenencia y
  consentimiento en silencio**. Un gate que *parece* cumplir ADR 0098 y no cumple es
  peor que ninguno: el siguiente lector confía en él.

Lo segundo es exactamente el antipatrón que ADR 0107 prohíbe. Se rechaza.

### La promesa falsa se BORRA (ADR 0107 aplicado)

El xmldoc de `StubPatientRegistry` ahora dice la verdad: este seam **no** admite un
adapter HIS/DB real "sin tocar el controller", porque su consumidor sirve todo
anónimamente y eso solo es seguro mientras la data sea fabricada. El padrón de
producción es `IPatientRepository` detrás de `/api/healthcare`.

Esto es lo que de verdad cierra el riesgo. El flag impide el accidente; borrar la
promesa impide la *decisión equivocada informada por un comentario mentiroso*.

### El gate va a nivel de CLASE, no por endpoint

`DevController` (el precedente de ADR 0013) repite `if (!_settings.Enabled) return
NotFound();` en cada acción. Con 18 endpoints eso son 18 oportunidades de olvidarlo, y
el endpoint #19 nacería abierto. `DevSeedOnlyAttribute` es un `TypeFilterAttribute`
declarado **una vez** sobre el controller: un endpoint nuevo nace gateado por omisión.
Es el mismo principio que ADR 0107 aplicó al colapsar las 5 copias del matching.

### `DevSeedSettings` amplía su alcance, explícitamente

ADR 0013 lo definió para "tooling **de datos** de desarrollo" — cosas que *mutan*
(`/dev/seed-test-site`). Una API read-only que *sirve* un seed no encajaba en esa
frase, pero sí en el principio: nada que dependa de data sembrada existe sin flag. El
xmldoc de `DevSeedSettings` se actualizó para decirlo en vez de dejar que el lector
adivine.

## Consequences

**Positivas:**

- El riesgo diferido de `/api/ehr` queda cerrado **categóricamente**, no
  probabilísticamente: en un deploy real el controller no existe.
- La demo local no cambia en absoluto (corre en Development, flag `true`). Verificado.
- Un solo modelo de PHI en el producto: `/api/healthcare` + `IPhiAccessGuard`. No se
  crea un segundo dialecto de autorización clínica que después haya que reconciliar.
- `[DevSeedOnly]` es reusable: cualquier superficie futura respaldada por un seed se
  marca con un atributo y hereda el comportamiento.

**Negativas o trade-offs:**

- **`Synergos:DevSeed:Enabled` gana poder de blast radius.** Era "el tooling de seeding
  está activo"; ahora también es "las APIs de demo existen". Un `true` accidental en un
  ambiente real ya no solo expone `/dev/*`: expone `/api/ehr`. Sigue sirviendo data
  fabricada, así que el daño está acotado — pero el flag ahora merece más cuidado.
- **Si `/api/ehr` fuera algún día la app clínica real, esto es un desvío**, no la
  solución: habría que revertir el atributo y hacer la partición de superficies.
- La demo clínica queda inaccesible en cualquier ambiente desplegado. Si alguna vez
  hace falta una demo hosteada, este ADR es lo que hay que reabrir (ver criterio abajo).

**Notas de implementación:**

- **El alcance del atributo, medido — no asumido.** Con el flag off: los 13 GET → 404;
  los POST con body **válido** → 404 sin ejecutar la acción ni escribir nada. Pero un
  POST con body **inválido** → **400, no 404**: la validación de modelo de
  `[ApiController]` corre *antes* que los action filters. O sea: el atributo garantiza
  que no se sirva ni se escriba nada, **NO** que la existencia del endpoint quede
  oculta. Si algún día hace falta lo segundo, requiere middleware por ruta (antes del
  model binding), no un action filter. Queda escrito en el xmldoc del atributo para que
  nadie le atribuya una propiedad que no tiene.
- **Trampa que costó un arranque:** el perfil de launch se llama `SynergosLocal`, no
  `"Development"`. Un `--launch-profile` inexistente **no falla ahí**: `dotnet run`
  sigue, no setea `ASPNETCORE_ENVIRONMENT`, arranca como **Production** y revienta con
  `InvalidOperationException: The factory has not been configured with a proper
  connection string` — que parece un problema de DB y es del perfil. Corregido en la
  skill `synergos-run-dev` (decía el nombre malo).
- **Criterio de reapertura:** si `/api/ehr` debe servir pacientes reales, o si hace
  falta una demo clínica en un ambiente desplegado. En el primer caso, **no basta con
  pedir login**: `patientId`/`patient`/`user`/`provider` los pone el caller, así que
  cualquier member autenticado leería historias ajenas — el mismo IDOR que T2 cerró en
  Tienda. Exige partir las dos superficies que hoy conviven en el controller: la
  clínica (`patients`, `patient/{id}`, `doctors`, `schedule`, `inbasket`, `encounter`,
  `prescription`, `order` → rol) y el portal del paciente (`portal/home`, `results`,
  `medications`, `refill`, `messages`, `billing`, `health` → pertenencia, derivando el
  paciente del caller e **ignorando** el query param).

**Hallazgo que esta revisión destapó y este ADR NO resuelve:**

El mismo patrón —anónimo + identidad por query param = leer lo de cualquiera— está
vivo y confirmado con **200** en vivo en otros tres verticales:

```
GET /api/gov/applications?citizen=   → 200
GET /api/realty/saved?user=          → 200
GET /api/blogs/messages?user=        → 200
GET /api/blogs/notifications?user=   → 200
```

**No se arreglan con `[DevSeedOnly]`**: a diferencia del EHR, esos controllers **son**
la app y no tienen gemelo de producción — dev-flagearlos borraría el producto. Piden el
tratamiento de T2 (derivar la identidad del caller, ignorar el query param) y una
decisión de modelo de acceso por vertical. Queda como trabajo abierto.

## Alternatives considered

- **Adoptar `IPhiAccessGuard` en `/api/ehr`** (la recomendación inicial del agente, y la
  que el reporte sugería). Rechazada por el desajuste `Guid?` vs. string id: obliga a
  mutar el contrato de producción o a un cumplimiento aparente. Ver arriba.
- **Solo exigir member logueado** (el mínimo del reporte). Rechazada: **no cierra el
  hueco**. Los ids de paciente los pone el caller, así que cualquier member autenticado
  seguiría leyendo las 5 historias completas. Habría producido la *sensación* de un
  arreglo con el IDOR intacto — el peor resultado posible.
- **Partir el controller en dos superficies ahora** (clínica + portal). Es lo correcto
  **si** el demo se vuelve app real, pero hoy sería trabajo grande sobre data
  fabricada, y decidir la forma del portal sin producto detrás es diseñar a ciegas.
  Diferida con criterio de reapertura escrito.
- **Rechazar query params desconocidos con 400** (el bug menor reportado: `?query=`
  devuelve el censo entero en vez de error). Rechazada por el arquitecto. Ignorar
  params no reconocidos es lo convencional en REST y el cliente Angular manda `q`;
  además se verificó que el filtro real **no miente**: `?q=xxnoexiste` → 0 contra 5 sin
  filtro. Rechazar params desconocidos arriesga romper clientes (cache-busting, UTM)
  sin cerrar nada — la protección real es el gate.

## References

- Controller y filtro: `Synergos.CMS.Web/Controllers/EhrController.cs` ·
  `Synergos.CMS.Web/Filters/DevSeedOnlyAttribute.cs`.
- La promesa falsa que originó todo: `Synergos.CMS.Application/Services/Impl/StubPatientRegistry.cs`
  (xmldoc corregido) · registro sin flag: `Synergos.CMS.Web/Composers/SeamComposer.cs:663`.
- El gemelo de producción: `Synergos.CMS.Web/Controllers/HealthcareApiController.cs` ·
  `Synergos.CMS.Interfaces/IPhiAccessGuard.cs` · `Synergos.CMS.Web/Services/DefaultPhiAccessGuard.cs:18-19`
  (roles `doctor` / `doctor,nurse,reception`).
- Molde del patrón vigente de guardas: `Synergos.CMS.Web/Controllers/ShopCatalogController.cs:86-93`
  (`RequireMember` → 401) y `:103-116` (`DenyIfForeignMember` → 403 directo, no `Forbid()`).
- Precedente de ADR 0013: `Synergos.CMS.Web/Controllers/DevController.cs:42`.
- Verificación en vivo (2026-07-16, CMS en `localhost:5000`): `/dev/ping` →
  `devSeedEnabled` como control del flag; `/api/healthcare/patients` → 401;
  `/api/ehr/patients` → 200 con flag on, 404 con flag off.
