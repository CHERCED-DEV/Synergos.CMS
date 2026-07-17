# ADR 0059 — Post-135 runtime hotfixes (Error.cshtml namespace + Umbraco localization race)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, hotfix batch tras `uSync Import full all`.
- **Consolida:** 2 fix commits post-cap.

## Context

Tras cerrar el scope acotado a Ola 135 (snapshot consolidado en
`refactor-docs/architecture/00-current-state-synergos-cms.md` §11.12),
el arquitecto corrió `uSync Import full all` y arrancó el sitio. 2
problemas runtime se manifestaron que no aparecían en el build:

1. **`Error.cshtml` CS0234 runtime** — la directive
   `@inject Umbraco.Cms.Core.Web.IUmbracoHelper Umbraco` que se había
   agregado en Ola 98 para hacer compilar `@Umbraco.GetDictionaryValue`
   referenciaba un tipo que **no existe** en esa namespace. La fix
   original había sido apresurada y nadie volvió a renderizar la vista
   tras los olas siguientes.
2. **`InvalidOperationException` en `DynamicRequestCultureProviderBase.TryAddLocked`** —
   bug upstream de Umbraco bajo tráfico concurrente en el primer
   request post-boot. Se manifestó porque el import disparó admin
   background work (`admin finished process (743 changes)`) en
   paralelo con la navegación del arquitecto.

Adicionalmente, el build compilaba con 3 warnings menores
(CS1573 / CA1068 / CA1870) que el arquitecto pidió cerrar.

## Decision

### Hotfix #1 — Error.cshtml + 3 build warnings

**Cambio en `Synergos.CMS.Web/Views/Error.cshtml`:**

- **Antes** (broken):
  ```razor
  @model Synergos.CMS.Web.Controllers.ErrorPageViewModel
  @inject Umbraco.Cms.Core.Web.IUmbracoHelper Umbraco
  ```
- **Después** (correcto):
  ```razor
  @inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage<Synergos.CMS.Web.Controllers.ErrorPageViewModel>
  ```

`UmbracoViewPage<T>` ya expone `Umbraco` como propiedad (helper) +
`Model` typed. Reemplazar `@model + @inject` por `@inherits` da ambos
sin tocar el shape del helper.

**Otros 3 warnings cerrados en mismo commit:**

- `IFormSubmissionReader.FormSubmissionListItem` — agregados los 4
  `<param>` tags faltantes (FormKey, ReceivedAtUtc, ClientIp,
  FieldCount). Cierra **CS1573**.
- `SlackWebhookSender.SendAsync` — `CancellationToken` movido al
  final del signature. 3 call sites actualizados (Comment / Form /
  Cart Slack notifiers). Cierra **CA1068**.
- `AdminController.EscapeCsvField` — `SearchValues<char>` cacheado
  estáticamente como `private static readonly` + uso de
  `value.AsSpan().IndexOfAny(...)`. Cierra **CA1870**.

Commit: `3938afe`.

### Hotfix #2 — Umbraco localization race

**`LocalizationComposer`** nuevo en
`Synergos.CMS.Web/Composers/LocalizationComposer.cs`:

```csharp
public sealed class LocalizationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<RequestLocalizationOptions>()
            .Configure<ILocalizationService>((opts, localization) =>
            {
                foreach (var lang in localization.GetAllLanguages())
                {
                    CultureInfo ci;
                    try { ci = CultureInfo.GetCultureInfo(lang.IsoCode); }
                    catch (CultureNotFoundException) { continue; }

                    var supported = opts.SupportedCultures;
                    if (supported is not null &&
                        !supported.Any(c => c.Name.Equals(ci.Name, StringComparison.OrdinalIgnoreCase)))
                        supported.Add(ci);

                    var supportedUi = opts.SupportedUICultures;
                    if (supportedUi is not null &&
                        !supportedUi.Any(c => c.Name.Equals(ci.Name, StringComparison.OrdinalIgnoreCase)))
                        supportedUi.Add(ci);
                }
            });
    }
}
```

**Cómo neutraliza el race**:

El bug upstream está en `DynamicRequestCultureProviderBase.TryAddLocked`
(introducido v11.4, presente en todas las versiones 12/13/14/15+, sin
fix shipped en 13.x). El método nativo enumera
`supportedCultures.Any(predicate)` afuera del lock mientras otro thread
está adentro mutando la misma lista — race documentado en review del
[PR #14064](https://github.com/umbraco/Umbraco-CMS/pull/14064)
(comment de `Nuklon` 2023-09-29). Bajo tráfico concurrente en
first-request-after-boot explota con `InvalidOperationException`
"Collection was modified".

Si pre-poblamos `SupportedCultures` + `SupportedUICultures` con todas
las Languages al boot, `TryAddLocked` nunca tiene que mutar y el race
no tiene oportunidad de manifestarse.

Commit: `8140e2b`.

## Consequences

**Positivas:**

- **Error.cshtml renderiza**: el flow de error pages (404 / 500)
  vuelve a funcionar end-to-end. Validable navegando a una URL
  inexistente.
- **Build sin warnings nuevos**: 0 CS, 0 CA en el Web project.
- **Localization estabilizada**: el race no se reproduce más bajo el
  tráfico que lo disparaba (admin background + navegación). Cambios
  en Languages requieren restart para entrar al pool — aceptable, no
  es escenario hot-reload.

**Negativas:**

- **Restart-on-language-change**: si el editor agrega un Language en
  backoffice, no participa en `RequestLocalizationOptions` hasta el
  próximo boot. Mitigación futura: hook a un `ILanguageNotificationHandler`
  que dispare un re-bind del `IOptions<RequestLocalizationOptions>`.
  Diferido — con 2 idiomas (es-CO + en-US) y estables el requisito no
  muerde.
- **Workaround opaco**: el bug está en Umbraco mismo; nuestro composer
  lo enmascara pero no lo resuelve upstream. Si Umbraco fixea el race
  en algún v13.x.y futuro, el composer queda como overhead innocuo
  (no rompe nada, solo pre-popula lo que el dynamic provider haría
  lazy).

**Neutras:**

- 2 fix commits + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.
- 0 schema rompedor.

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| post-135.1 | Error.cshtml `@inherits UmbracoViewPage<T>` + CS1573/CA1068/CA1870 cerrados | `3938afe` |
| post-135.2 | LocalizationComposer pre-popula RequestLocalizationOptions | `8140e2b` |
| 0059 | (este) ADR consolidado | — |

## Próximas direcciones

- **Re-bind on language change** via `ILanguageNotificationHandler`
  (opcional, low priority).
- **Upstream PR a Umbraco** corrigiendo `TryAddLocked` para que
  enumere bajo lock (idealmente reescrito como `lock (locker) { ... }`
  envolviendo TODO el predicate + Add). Fuera del scope CMS.

## References

- [Umbraco PR #14064 — Add DynamicRequestCultureProviderBase and improve locking](https://github.com/umbraco/Umbraco-CMS/pull/14064)
  (introducción del bug, review comment de `Nuklon` reportándolo).
- [Umbraco PR #18015 — Use the new more efficient .NET 9 Lock type](https://github.com/umbraco/Umbraco-CMS/pull/18015)
  (cambio Jan 2025; **no** corrige el race, solo swap del lock type).
- ADR 0058 — Health + Receiver SDK + Webhook test harness (último
  scope ola batch antes del cap 135).
- `refactor-docs/architecture/00-current-state-synergos-cms.md` §11.12
  (snapshot del cap).
