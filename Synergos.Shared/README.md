# Synergos.Shared

Fontanería que **todo host de API repite**. Nada más.

## Regla de admisión

> Admite un tipo si, al borrarlo, **un host deja de arrancar igual**, y el tipo **no menciona
> ningún sustantivo del negocio**.

Y la frontera contra `Synergos.Core`, que es lo que impide que esto se convierta en un `Utils`:

> `Core` no sabe qué es un host. `Shared` no sabe qué es un pedido.

Un tipo que parece pertenecer a los dos no existe: está mal cortado y hay que partirlo.

`Shared` **sí** puede referenciar `Core` — una flecha, nunca al revés. La justifica
`RejectionResults`: el mapeo `Rejection → HTTP` lo necesitan las dieciséis capacidades.

## Por qué existe, si CLAUDE.md §6 prohíbe los `Shared/`

Porque lo que §6 prohíbe es el proyecto cuyo criterio de admisión es *"lo que no cupo en otro
lado"* — ese siempre crece y nunca encoge. Este tiene criterio positivo, y lo verifica
`BackendSegregationTests`, no el juicio del que abre el PR.

Y no se creó especulando: **cada tipo de aquí salió de `Synergos.Sessions`**, donde ya estaba
escrito y funcionando. No es código que *podría* compartirse; es código que ya se iba a copiar
en la segunda API.

Ver [`docs/product/06-arquitectura-backend.md`](../Synergos.CMS.Web/docs/product/06-arquitectura-backend.md).
