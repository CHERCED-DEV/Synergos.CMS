Cierra #

## Qué cambia y por qué

<!-- El PORQUÉ. El QUÉ ya se lee en el diff. -->

## Definición de hecho

<!-- Marcá solo lo que de verdad corriste. Un check falso es peor que uno vacío. -->

- [ ] `dotnet build` sin errores CS
- [ ] Gates de arquitectura en verde — `dotnet test --filter "FullyQualifiedName~Architecture"`
- [ ] Tests nuevos **y muté cada uno**: reintroduje el defecto, confirmé el rojo, restauré
- [ ] Si toca schema uSync: `node tools/usync-audit.mjs` en verde
- [ ] Si cruza servicios: **verificado con procesos reales** — levanté la pila y maté una capacidad a mitad de flujo
- [ ] Si aprendí una regla nueva: está escrita en `CLAUDE.md` §5 **en este mismo commit**
- [ ] Si esto vuelve obsoleto algo de `CLAUDE.md`: corregido acá, no después

## Qué mutación pone esto en rojo

<!-- El cambio de una línea que reintroduce el defecto o rompe la feature.
     Si no se puede escribir, el cambio no está entendido. -->

## Lo que encontré y NO arreglé acá

<!-- Enlaces a los tickets de tipo Hallazgo que abrí. Un hallazgo no puede comerse la tarea:
     se abre ticket y se sigue. Si no encontraste nada, borrá esta sección. -->
