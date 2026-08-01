---
name: synergos-adr-author
description: Escribe un ADR (Architecture Decision Record) para Synergos siguiendo el formato exacto del proyecto. Asigna el número correcto (siguiente al más alto existente), genera el archivo en Synergos.CMS/Synergos.CMS.Web/docs/adr/, actualiza el índice §11.2 de refactor-docs/architecture/00-current-state-synergos-cms.md, y produce los DOS commits (viven en repos distintos). Invocar cuando se toma una decisión arquitectónica que debe quedar registrada.
model: claude-opus-4-8
---

# SYNERGOS ADR Author — registrar decisiones arquitectónicas

Los ADRs son la memoria del proyecto. Con 107 escritos, el formato y la numeración deben ser consistentes: un ADR que no encaja obliga al siguiente lector a preguntarse si el proyecto cambió de convención o si alguien se equivocó.

---

## 0. Antes de nada: ¿merece un ADR?

1. **¿Ya está decidido en otro ADR?**
   ```bash
   grep -rl "{palabra clave}" Synergos.CMS/Synergos.CMS.Web/docs/adr/
   ```
2. **¿Es una decisión o una preferencia?** Los ADRs registran decisiones con consecuencias duraderas, no gustos de estilo.
3. **¿Supera a un ADR anterior?** Si sí, marcar el viejo `Superseded by ADR NNNN` **en su propio archivo**, no solo en el nuevo.
4. **¿Tiene consecuencias fuera de su módulo?** Si no afecta a nadie más, probablemente no es un ADR.

---

## 1. El número siguiente

Los ADRs viven en `Synergos.CMS/Synergos.CMS.Web/docs/adr/` con nombre `NNNN-slug-en-ingles.md` (4 dígitos, **sin** prefijo `ADR-`), más un `README.md`.

```bash
ls Synergos.CMS/Synergos.CMS.Web/docs/adr/ | grep -E '^[0-9]{4}-' | sort | tail -1
```

El siguiente es ese +1, con 4 dígitos. El **slug del archivo va en inglés** (`0107-in-memory-catalog-engine-and-deferred-examine.md`); **el contenido va en español**.

---

## 2. El formato REAL — verificado contra los 107

> ⚠️ **Verificar siempre contra el ADR más reciente antes de escribir**, no contra este template: la convención puede haber evolucionado y este documento puede estar viejo.
> ```bash
> head -12 $(ls Synergos.CMS/Synergos.CMS.Web/docs/adr/[0-9]*.md | sort | tail -1)
> ```

```markdown
# ADR NNNN — {Título en español, con el vertical/transversal entre paréntesis si aplica}

- **Status:** Accepted
- **Date:** YYYY-MM-DD
- **Deciders:** {Quién y en qué contexto. Los ADRs recientes registran aquí cómo se produjo
  la decisión — panel multi-agente, revisión adversarial, firma explícita del arquitecto —
  porque eso es lo que le dice al lector futuro cuánto peso tiene.}
- **Relacionados:** {ADR NNNN (por qué se relaciona), ADR NNNN (por qué), … + reglas del
  proyecto que aplican, p.ej. "Regla de oro doc 25: ninguna capacidad transversal se
  implementa dos veces".}

---

## Context

## Decision

### {Sub-encabezados por decisión — los ADRs buenos tienen 3-6}

## Consequences

**Positivas:**
- …

**Negativas o trade-offs:**
- …

**Notas de implementación:**
- …

## Alternatives considered

## References
```

**Marcadores exactos** (107 ADRs los usan; **cero** usan `**Estado:**` / `## Contexto`):
`- **Status:**` · `- **Date:**` · `- **Deciders:**` · `- **Relacionados:**` · `## Context` ·
`## Decision` · `## Consequences` · `## Alternatives considered` · `## References`

**No hay campo `Ola:`.** El contexto de la ola va dentro de `Deciders:` o de `Context`.

---

## 3. Qué hace bueno a un ADR de este proyecto

Los ADRs recientes (0103-0107) no son formularios rellenados: son **argumentos**. Lo que los distingue:

- **Registran las premisas FALSAS que se corrigieron.** ADR 0106 abre listando tres cosas que el encuadre inicial daba por ciertas y no lo eran. Eso es lo que impide que el próximo las vuelva a asumir.
- **Justifican con evidencia medida, no con opinión.** "Examine 3.7.1 no tiene facetado — verificado en los binarios" pesa; "Lucene es overkill" no.
- **Escriben el criterio de reapertura.** Si la decisión depende del volumen o la latencia, decir el número (">5.000 ítems/vertical o p95 >50ms") para que se revise por dato.
- **Nombran lo que se rechazó y POR QUÉ**, en `Alternatives considered`. Un rechazo sin razón invita a rehacerlo.
- **Registran los bugs que la propia ola destapó**, incluidos los del agente. Son el tipo de defecto que un build verde no atrapa, y el ADR es donde sobreviven.
- **Son honestos con las consecuencias negativas.** Un ADR sin trade-offs es publicidad.

Si el ADR no dice nada que un `git log` no diga ya, no hacía falta.

---

## 4. Escribir el archivo

Usar la herramienta Write directamente. **No** generar el contenido con here-strings de PowerShell: el español lleva tildes y ñ, y el encoding se corrompe (ver `feedback_powershell_utf8_bulk_edits`). Si hiciera falta por otra razón, `[System.IO.File]::WriteAllText(..., [System.Text.Encoding]::UTF8)`.

---

## 5. El índice §11.2 — DOS sitios que actualizar

El índice **no** vive junto a los ADRs: vive en `refactor-docs/architecture/00-current-state-synergos-cms.md`, sección §11.2. Y tiene **dos partes**, las dos hay que tocarlas:

1. **El encabezado en prosa** (una sola línea larguísima):
   `## 11.2 ADRs ratificados — ahora hasta ADR NNNN (fase de lógica de negocio: ADR NNNN {resumen} — ADR NNNN-1 {resumen} — …)`
   → cambiar `hasta ADR NNNN` y **anteponer** el resumen del nuevo al principio del paréntesis.

2. **La fila de la tabla**, al final de la lista: `| NNNN | Título | {descripción larga} |`
   → insertarla **después** de la fila del ADR anterior.

La descripción de la tabla es **densa a propósito**: es el único sitio donde alguien que no abre el ADR ve la sustancia. Los recientes cierran con: commits · estado de la suite · ADRs relacionados · `0 GUIDs, 0 NuGet, 0 npm` (y `0 schema, 0 Import` si aplica).

> **No automatizar esto con regex.** El encabezado es prosa libre y la tabla tiene descripciones de miles de caracteres con pipes y comillas dentro. Editar con Read + Edit, o con un script Node que busque la línea por prefijo (`startsWith('| 0106 |')`) e inserte. Un `-replace` a ciegas corrompe el documento.

---

## 6. Los commits — SON DOS, en REPOS DISTINTOS

⚠️ **La trampa que hace fallar el paso 5:** `Synergos.CMS/` es un repo git **propio**, y el repo raíz `synergos/` **lo ignora**. El ADR y el índice **no pueden ir en el mismo commit**.

```bash
# 1) El ADR — repo Synergos.CMS
cd Synergos.CMS
git add Synergos.CMS.Web/docs/adr/NNNN-*.md
git commit -m "docs(adr): NNNN — {título corto}"

# 2) El índice — repo raíz
cd ..
git add refactor-docs/architecture/00-current-state-synergos-cms.md
git commit -m "docs(arch): indice §11.2 — ADR NNNN ({tema})"
```

Formato observado en el repo: `docs(adr): NNNN — …` y `docs(arch): indice §11.2 — ADR NNNN (…)`.

---

## 7. Rangos temáticos (referencia)

| Rango | Categoría |
|-------|-----------|
| 0001–0009 | Plataforma base (Umbraco, uSync, Models) |
| 0010–0019 | Branding, multi-site, identidad |
| 0020–0029 | Page composition, layouts, render pipeline |
| 0030–0039 | Audit, analytics, observabilidad |
| 0040–0049 | Auth, members, seguridad |
| 0050–0059 | Razor, templates, localización |
| 0060–0069 | HTTP resilience, CDN, bundle registry |
| 0070–0079 | Testing, seams, contratos |
| 0080–0089 | CMS↔UI contracts, custom elements |
| 0090–0099 | Automatización, dev experience |
| 0100+ | Verticales + fase de lógica de negocio (doc 25: T0…T9) |

Los rangos son orientativos y ya se agotaron: **no renumerar ni forzar** un ADR a un rango. El número siguiente es el siguiente.
