# Umbraco — Composers

An Umbraco `IComposer` is the entry point for DI wiring, notification
registration, and runtime configuration. Synergos.CMS keeps composers in
one folder by design — see [ADR 0005](../adr/0005-composers-centralized.md).

## Where they live

`Synergos.CMS.Web/Composers/` — and only there. The `Application` and
`Interfaces` projects never contain an `IComposer`, because they don't
reference Umbraco.

## Naming

- One composer per concern.
- Name: `{Concern}Composer.cs`, `sealed`, in namespace
  `Synergos.CMS.Web.Composers`.
- Examples (when they exist):
  - `DependencyInjectionComposer.cs` — plain DI registrations.
  - `NotificationsComposer.cs` — registers Umbraco notification handlers.
  - `RuntimeConfigComposer.cs` — runtime config (global settings, content
    settings).

## What a composer *does*

- Registers services into `IUmbracoBuilder.Services`.
- Adds notification handlers via `AddNotificationHandler` /
  `AddNotificationAsyncHandler`.
- Wires runtime configuration through `Configure<TOptions>`.

## What a composer *does not* do

- Implement business logic. Handlers, services, and strategies live in
  their own files.
- Read files, call APIs, or initialize state. If startup needs that,
  it goes in an `INotificationHandler<UmbracoApplicationStartingNotification>`.
- Cross between concerns. Don't create a `BigComposer` that does DI
  and notifications and uSync configuration — split it.

## Startup ordering

If one composer must run after another:

```csharp
[ComposeAfter(typeof(NotificationsComposer))]
public sealed class RuntimeConfigComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) { /* … */ }
}
```

Avoid ordering when you can. If you need it, it's worth a brief
comment explaining why — future refactors break silently on implicit
ordering assumptions.

## Registering a notification handler

The handler itself lives in `Synergos.CMS.Web/Notifications/`. The composer
only wires it:

```csharp
// Synergos.CMS.Web/Composers/NotificationsComposer.cs
public sealed class NotificationsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationHandler<
            ContentSavedNotification,
            Notifications.ContentSavedLoggingHandler>();
    }
}
```

## Common mistakes

| Mistake | Fix |
|---------|-----|
| Composer in `Application/` | Move to `Synergos.CMS.Web/Composers/`. Umbraco won't even see it otherwise. |
| DI wiring scattered across many `UseX` extension methods | Consolidate into one `Composer` unless they belong to different concerns. |
| Composer that implements logic inline | Extract logic to a service; composer only registers. |
| Two composers registering the same service | Delete one. Prefer a `TryAdd*` registration to make conflicts visible. |
