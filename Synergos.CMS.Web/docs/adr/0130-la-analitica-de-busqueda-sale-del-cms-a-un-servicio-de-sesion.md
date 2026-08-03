# ADR 0130 — La analítica de búsqueda sale del CMS a un servicio de sesión

- **Estado:** Aceptado
- **Fecha:** 2026-08-03
- **Cumple:** la seam `ISearchAnalyticsStore` (ADR 0045), que ya declaraba este destino
- **Sigue el molde de:** ADR 0012 (el contrato del CDN se CONSUME, no se posee)

## Contexto

El arquitecto lo planteó así:

> *"esto lo podemos solucionar creando una API de sesión, la cual le respalde la información de
> búsqueda o este tipo de cosas que pueden ser predictivas para entender el usuario… sería un
> proyecto aparte del CMS, porque no quiero saturar. En algún momento quiero que separemos muy
> bien todas las responsabilidades y generemos cada cosa en su propio circuito de aplicativo."*

No hacía falta inventar la costura: `ISearchAnalyticsStore` ya lo decía por escrito.

> *"Esta costura existe para ser reemplazada. El destino previsto es un servicio de sesión
> propio —fuera del CMS— que respalde búsqueda y demás señales de comportamiento; ese día se
> registra otro adapter y ni `SearchController` ni `AdminController` se enteran."*

## Decisión

**`Synergos.Sessions`, un servicio aparte, dueño de las señales de sesión.** v1 cubre búsqueda.

| | |
|---|---|
| Ubicación | proyecto propio en esta solución, **sin referencia a `Synergos.CMS.*`** |
| Acople | el contrato **HTTP**, no un ensamblado compartido |
| Almacén | JSONL append-only, un fichero por día, retención propia |
| Selección | `Synergos:SearchAnalytics:Mode` = `FileSystem` (default) \| `Sessions` |

El default sigue siendo `FileSystem` a propósito: un clon recién bajado arranca sin depender de
otro proceso. Encender `Sessions` sin el servicio arriba **degrada** —el dashboard sale vacío—
pero no tumba el CMS.

Que no haya referencia de ensamblado es la decisión que hace real la separación: el día que este
proyecto se mude a su propio repositorio, no hay nada que desenredar. Es el mismo razonamiento
del ADR 0012 con el CDN.

## Lo que obligó a cambiar la interfaz

Las lecturas de `ISearchAnalyticsStore` pasan a **async**. No es simetría estética: bloquear un
hilo del pool esperando a otro proceso es lo que agota el pool bajo carga, y
`/api/search/analytics` es `[AllowAnonymous]` con el gate por rol saliendo de configuración
—hoy `"admin,editor"`, pero vaciarlo es una línea—, así que el peor caso es una ruta pública con
una llamada de red bloqueante por request. Habría sido introducir a sabiendas el mismo
`sync-over-async` que se acababa de quitar de `BlogsController`.

Cascada: 5 sitios de producción + `IDashboardReadModel.GetSearchInsightsAsync`. La implementación
de fichero devuelve tareas ya completadas y no paga nada.

`Record` **no** cambia: sigue siendo síncrono y fire-and-forget, que es lo que el contrato ya
prometía.

## Cómo se comporta cuando el otro lado no está

Es la parte que importa: un servicio auxiliar caído no puede convertirse en un CMS caído.

- **La escritura no viaja en el request del usuario.** `Record` encola y vuelve; un lazo de fondo
  hace el POST. En línea, cada búsqueda de un visitante esperaría un salto de red para guardar
  una métrica — cambiar acoplamiento por latencia, y encima en el camino del usuario.
- **La cola es acotada (2 000) y descarta lo más viejo.** Sin tope, el servicio caído se come la
  memoria del CMS: la analítica habría tumbado justo lo que venía a descargar. Se descarta lo
  viejo porque en una serie temporal el dato reciente vale más.
- **No se reintenta.** Reintentar analítica con el servicio caído convierte una degradación en
  una tormenta de peticiones.
- **Las lecturas fallidas devuelven vacío.** Un dashboard sin datos es un inconveniente; uno que
  revienta es un fallo de operación.

## Verificación

**Los dos procesos, hablando de verdad.** Servicio en `:5201` con llave, CMS en `:5202` con
`Mode=Sessions`, ambos con almacenes desechables:

1. Cuatro búsquedas contra `/api/search` del CMS → el servicio reporta `written: 4` y aparece su
   fichero del día.
2. `/api/search/analytics` del CMS, autenticado → devuelve los agregados **que vinieron del
   servicio por HTTP**, con el query repetido contando 2.
3. Sin llave, la ingesta responde **401**.

**Un defecto que encontró un test**, y vale contarlo: el contador de descartes estaba clavado en
cero. Con `BoundedChannelFullMode.DropOldest`, `TryWrite` **siempre** devuelve `true` —descarta
por dentro y no avisa—, así que contar por su retorno no medía nada. Se usa la sobrecarga con
callback `itemDropped`. Sin el test, `/health` habría reportado "0 descartados" para siempre.

## Consecuencias

**A favor.** El CMS deja de acumular el rastro de comportamiento. Y el circuito queda probado de
punta a punta con superficie mínima, que era el objetivo de empezar por búsqueda.

**En contra, y hay que decirlo.**

- **Un proceso más que operar**, con su despliegue y su vigilancia. Es el precio de separar.
- **Los eventos se pueden perder**: cola acotada, sin reintentos, sin persistencia local previa
  al envío. Para analítica es aceptable —no es contabilidad— pero no hay que fingir lo contrario.
- **La llave compartida es simple y suficiente hoy**, y no es autenticación fuerte. Sin llave el
  servicio arranca igual pero **avisa a gritos** de que la ingesta está abierta.
- **Sigue sin haber concepto de sesión.** Se registra *qué* se buscó, no *quién*. Lo predictivo
  de verdad necesita hilar las búsquedas de una misma persona, y eso trae de golpe las preguntas
  de privacidad y consentimiento. Es la decisión que sigue, y es de producto.

## Lo que NO entró, a propósito

- **`IAnalyticsTracker`** (15 consumidores en el CMS). Cabe detrás del mismo contrato, pero se
  decidió empezar por una sola señal para probar el circuito antes de mover quince sitios.
- **Identidad de visitante.** Ver arriba.
- **Repositorio propio.** El proyecto ya está listo para mudarse —no referencia el CMS—; se
  quedó aquí para no pagar CI y release nuevos antes de que el circuito estuviera probado.
