# Recipe — Add a new typed settings section

Add a strongly-typed settings class bound to a new `Synergos:*` section in
`appsettings.json`.

Example: `Synergos:Notifications` → `NotificationsSettings` with per-channel
endpoints.

## Steps

### 1. Design the shape

Decide what lives in config vs. what lives in content / code:

- ✅ **Infra values:** URLs, timeouts, credentials, feature flags, TTLs
- ❌ **Brand values:** colors, fonts, page names → those go in `SeedConfig`
- ❌ **Content values:** titles, body copy → those go in the Umbraco content tree
- ❌ **Runtime state:** anything that changes at runtime without a restart

### 2. Create the settings class

`Synergos.CMS/Configuration/NotificationsSettings.cs`:

```csharp
namespace Synergos.CMS.Configuration;

/// <summary>
/// Multi-channel notification endpoints. Each channel has a URL; empty strings
/// disable the channel. Bound from appsettings.json → Synergos:Notifications.
/// </summary>
public sealed class NotificationsSettings
{
    public const string SectionPath = "Synergos:Notifications";

    /// <summary>Slack incoming webhook URL. Empty = disabled.</summary>
    public string SlackWebhookUrl { get; init; } = string.Empty;

    /// <summary>Teams incoming webhook URL. Empty = disabled.</summary>
    public string TeamsWebhookUrl { get; init; } = string.Empty;

    /// <summary>Request timeout for notification POSTs in ms.</summary>
    public int TimeoutMs { get; init; } = 5000;

    /// <summary>Retry attempts on transient failures. 0 = no retry.</summary>
    public int MaxRetries { get; init; } = 2;
}
```

Rules:
- `public sealed class` with `{ get; init; }` properties (immutable after binding).
- Every property has a **default value** so a fresh install has sensible behavior.
- XML doc comments on every field — they double as config reference docs.
- `public const string SectionPath = "Synergos:<Name>";` by convention.

### 3. Register in `Program.cs`

```csharp
// Alongside other Configure<T> calls near the top of Program.cs
builder.Services.Configure<NotificationsSettings>(
    builder.Configuration.GetSection(NotificationsSettings.SectionPath));
```

### 4. Add default values to `appsettings.json`

```json
"Synergos": {
  // … existing sections
  "Notifications": {
    "SlackWebhookUrl": "",
    "TeamsWebhookUrl": "",
    "TimeoutMs":       5000,
    "MaxRetries":      2
  }
}
```

Why add to JSON if the defaults are in C#?
- Makes the section **discoverable** (developers see it in appsettings.json).
- Gives per-env overrides a clear base (just change the value).
- Clarifies intent — "we explicitly want these defaults".

### 5. Consume via `IOptions<T>`

```csharp
using Microsoft.Extensions.Options;
using Synergos.CMS.Configuration;

namespace Synergos.CMS.Application.Notifications;

public sealed class SlackNotifier
{
    private readonly NotificationsSettings _settings;
    private readonly IHttpClientFactory    _httpFactory;

    public SlackNotifier(IOptions<NotificationsSettings> options, IHttpClientFactory http)
    {
        _settings    = options.Value;
        _httpFactory = http;
    }

    public async Task NotifyAsync(string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SlackWebhookUrl))
        {
            // Channel disabled — silent no-op is fine.
            return;
        }

        using var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromMilliseconds(_settings.TimeoutMs);

        await client.PostAsJsonAsync(_settings.SlackWebhookUrl,
            new { text = message }, cancellationToken: ct);
    }
}
```

Use `IOptionsSnapshot<T>` if the values can change at runtime (e.g. reload on
appsettings.json edit). Most of the time `IOptions<T>` is fine — values are
captured at DI registration.

### 6. Pick the right layer

**Where does `SlackNotifier` live?**

- If it's a use case with business logic (e.g. "notify on form submission"), it
  could live in Application — but since it does HTTP, it's actually a bit
  mixed.
- Cleanest: define `INotifier` in Domain, implement `SlackNotifier` in
  `Infrastructure/Http/` or `Infrastructure/Notifications/`.

See [`../architecture/clean-architecture.md`](../architecture/clean-architecture.md)
for the dependency rule.

### 7. Per-environment overrides

Production `appsettings.Production.json`:

```json
"Synergos": {
  "Notifications": {
    "SlackWebhookUrl": "https://hooks.slack.com/services/…",
    "TeamsWebhookUrl": "https://outlook.office.com/webhook/…",
    "TimeoutMs":       10000
  }
}
```

Secrets should come from environment variables or user-secrets:

```bash
# Environment variable (works everywhere)
Synergos__Notifications__SlackWebhookUrl=https://hooks.slack.com/…

# dotnet user-secrets (dev only, stored outside the repo)
cd Synergos.CMS
dotnet user-secrets set "Synergos:Notifications:SlackWebhookUrl" "https://hooks.slack.com/…"
```

Never commit secrets to appsettings.json or any other tracked file.

### 8. Document it

Update [`../configuration/reference.md`](../configuration/reference.md) to
include the new section.

### 9. Skip SchemaVersion

Typed settings are not schema — no bump needed. Schema pipeline doesn't run.

## When does a setting deserve its own class?

| Scenario | Add a class | Reuse existing |
|---|---|---|
| New feature with 3+ config values | ✅ | |
| One-off value used in 1 place | Inline `Configuration["Synergos:X"]` may be ok | |
| Values are related to existing feature | | Add to existing class |
| Values are CDN-related | | Add to `StaticAssetsSettings` / `ElementServersSettings` |
| Values are brand/content | | Add to `SeedConfig` |

## Anti-patterns

### "Read it directly from `IConfiguration`"

```csharp
// ❌ BAD
var timeout = int.Parse(configuration["Synergos:Notifications:TimeoutMs"] ?? "5000");
```

- No type safety.
- No default value unless you add it inline.
- No IntelliSense or refactoring support.
- Hard to mock in tests.

Use `IOptions<T>` via a typed class every time.

### "Stuff it in `GlobalSettings`"

GlobalSettings is the Umbraco content-type for editorial fallbacks (SEO,
tracking scripts). It's not a config store.

- Editors shouldn't edit infrastructure values (CDN URLs, timeouts, webhook URLs).
- GlobalSettings values are fetched via the content cache — too slow for hot paths.
- Unfit for per-environment overrides (one DB for all).

Use `appsettings.json` + a typed class.

### "Stuff it in `SeedConfig`"

`SeedConfig` is for first-boot content seeding. After the seed runs, editors
own the content. Putting runtime infrastructure there is wrong.

- SeedConfig is consumed only by `ContentSeeder`, not by runtime services.
- Editor values can diverge from SeedConfig defaults, so code can't trust them.

Use a dedicated `*Settings` class.

## See also

- [`../configuration/reference.md`](../configuration/reference.md) — every existing section.
- [`../configuration/seed.md`](../configuration/seed.md) — when to use SeedConfig vs typed settings.
- [`add-document-type.md`](add-document-type.md), [`add-element-type.md`](add-element-type.md), [`add-cdn-macro.md`](add-cdn-macro.md) — other recipes.
