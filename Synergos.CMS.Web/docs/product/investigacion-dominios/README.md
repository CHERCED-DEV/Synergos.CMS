# Investigación comercial por dominio — nueve verticales contra lo construido

> **Cómo se produjo.** Nueve agentes en paralelo, uno por dominio, cada uno aterrizándose
> primero en el repo (la matriz del doc 08, el catálogo del doc 07, los `*Rules.cs` de las
> capacidades de su dominio) y después investigando el mercado con búsqueda web. Un décimo
> agente sintetizó los nueve. **10 de 10 completaron, cero fallidos.**
>
> Cada informe marca sus afirmaciones **VERIFICADO** (con URL consultada) o **INFERIDO**.

## Los ficheros

| | |
|---|---|
| `00`–`08` | Un informe por dominio: comprador, competencia, ley, ajuste contra las 20 capacidades, backlog priorizado, ángulo CMS, demo, riesgo que mata |
| `09-SINTESIS.md` | Lo transversal: necesidades comunes, orden de construcción, con qué salir primero, la crítica al diferencial, capacidades nuevas aprobadas, y la calidad de la evidencia |

## Verificación por muestreo hecha sobre el resultado

No se aceptó el resultado tal como llegó. Se comprobaron afirmaciones de alto impacto:

| Afirmación | Veredicto |
|---|---|
| Res. 1888 de 2025 crea el RDA, exige HL7 FHIR, transición 15-oct-2025 → **obligatorio 15-abr-2026** | ✅ confirmado (el PDF de MinSalud existe pero es escaneado; contrastado con fuente secundaria) |
| Precios publicados de Saludtools (COP 89.000 / 147.000 / 168.000 por profesional/mes) | ✅ confirmado al peso |
| Res. 2890/2017 exige **CIIU 7990** al operador de boletería | ✅ confirmado, Art. 2 |
| «…y estar **autorizado por MinCultura**» | ❌ **FALSO.** No existe régimen de autorización de operadores; el registro PULEP es del **productor**. La barrera con la que la síntesis descarta Eventos es un trámite de RUT, no una habilitación |
| Ticketmaster entró a Colombia en marzo de 2025 | ⚠️ **no verificable** — la fuente (Forbes) devuelve 403 |

**La corrección de MinCultura importa**: debilita el motivo principal por el que §3 descarta Eventos.
Léase esa sección sabiendo eso.

## Los límites que el propio ejercicio declara

La §7 de la síntesis es autocrítica y hay que leerla antes que las recomendaciones. Lo esencial:

1. **La evidencia de código es la más sólida** — todas las afirmaciones sobre el repo que se
   volvieron a comprobar resultaron correctas.
2. **La evidencia de precio de la competencia es débil y sesgada a la baja**: casi todo precio
   sale de la página del propio vendedor, y los competidores que de verdad importan no publican.
   **No sabemos el ticket real contra el que competiríamos en ningún dominio.**
3. **Cero entrevistas.** Los nueve «quién firma / cuánto gasta / qué le duele» son inferencia
   desde documentos públicos. Es la debilidad más grande del conjunto.
4. **Ningún demo de los nueve es ejecutable hoy de punta a punta**, porque el borde no cobra
   (`LoggingPaymentProvider`) y no notifica (`LoggingNotificationSender`).
