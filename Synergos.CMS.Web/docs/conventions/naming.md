# Naming Conventions

Names are public API inside the repo. They outlive the code they describe.
When in doubt, prefer the name a six-months-from-now reader would expect.

## C# identifiers

| Kind | Convention | Example |
|------|------------|---------|
| Namespace | `Synergos.CMS.{Project}.{Folder}` | `Synergos.CMS.Application.Services` |
| Class | `PascalCase` | `BookingProxy` |
| Interface | `I` + `PascalCase` | `IBookingProxy` |
| Method | `PascalCase`, verb-first | `GetByKey`, `Publish` |
| Parameter / local | `camelCase` | `pageKey` |
| Private field | `_camelCase` | `_cache` |
| Constant | `PascalCase` | `MaxRetries` |
| Enum member | `PascalCase` | `OrderStatus.Completed` |
| Async method | Suffix `Async` only when an overload without it exists | `GetByKeyAsync` |
| Generic type parameter | `T`, or `TFoo` if meaningful | `IReader<TEntity>` |

## File names

- One public type per file. File name = type name.
- `{TypeName}.cs` for classes/interfaces/records.
- Partial classes use `{TypeName}.{Aspect}.cs` (e.g. `FooService.Validation.cs`).

## Folder names

- `PascalCase`, singular unless the folder holds a list of peers
  (`Controllers/`, `Proxies/`, `Notifications/` — plural; `Configuration/` — singular).
- No abbreviations, no transliterations. `ViewModels/` not `VMs/`.
  `Notifications/` not `Notifs/`.
- Feature sub-folders use the feature's user-facing name:
  `Controllers/Booking/`, not `Controllers/Bkng/`.

## Umbraco-specific

- **Document Types**: `PascalCase`, no suffix. `ArticlePage`, `BlogHome`.
- **Element Types**: `PascalCase`, suffix `Element` only if disambiguation
  is needed. `HeroBanner`, `FaqElement` (when `Faq` alone collides).
- **Compositions**: `PascalCase`, suffix `Composition` to avoid collision
  with document types. `SeoComposition`, `VisibilityComposition`.
- **Data Types**: `PascalCase`, describe intent not widget. `BlogCategory`,
  not `DropdownList2`.
- **Aliases** (the Umbraco alias string): `camelCase`, matching Umbraco's
  own convention. `articlePage`, `heroBanner`.

## What to avoid

- Hungarian notation (`strName`, `iCount`).
- Random abbreviations (`mgr`, `ctrl`, `svc`, `bo`). Prefer the full
  word — modern IDEs autocomplete.
- Marketing-style names (`SuperCoolContentProvider`). Name by role.
- `Helper`, `Util`, `Common`, `Manager` — these are smell names that
  mean "I didn't find the right abstraction". If you truly need one,
  write the more specific name you'd use if `Helper` were forbidden.
- Trailing numbers (`Service2`, `ControllerV2`). If it's a new version,
  either rename the old or use a different concept entirely.
