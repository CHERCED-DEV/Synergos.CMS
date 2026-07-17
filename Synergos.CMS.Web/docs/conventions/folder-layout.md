# Folder Layout Conventions

The full lookup table of "where does this go?" lives in
[`../architecture/folder-layout.md`](../architecture/folder-layout.md).
This file is the **rationale** for the conventions that table encodes.

## Guiding principles

### 1. Organize by *technical kind* first, by *feature* second

Inside `Synergos.CMS.Web`, the top-level folders are technical kinds:
`Controllers/`, `Composers/`, `ValueConverters/`, etc.

Feature folders appear **inside** a technical kind, not at the root:
- `Controllers/Booking/BookingController.cs` ✅
- `Booking/Controllers/BookingController.cs` ❌

The prior fail (`epicfail2`) inverted this inside `Application/` and
ended up with feature folders that each replicated Services, Mappers,
Models, and Proxies — 234 classes of that.

### 2. Folders earn their existence

A folder is created when there are **three or more peer files** that
share a concern. Not before. A single service doesn't need its own folder.

Exception: the top-level scaffold (`Composers/`, `Controllers/`, etc.)
is committed empty (with `.gitkeep`) because the structure is itself
the governance statement.

### 3. No `Shared/`, `Common/`, `Utils/`, `Helpers/`, `Misc/`

These folders collect anything that doesn't fit elsewhere, and because
everything fits there eventually, they grow unbounded. If you find
yourself reaching for one, the right answer is usually:

- An extension method living near the type it extends.
- A small class with a specific name describing its role.
- An inline private method, if used once.

### 4. Depth cap: three levels

No folder may be deeper than three from a project root. If you're
about to create a fourth level, the model is probably wrong.

- `Controllers/Booking/BookingController.cs` — level 2, fine.
- `Controllers/Booking/Forms/BookingFormController.cs` — level 3, borderline.
- `Controllers/Booking/Forms/Validation/V2/...` — refactor.

### 5. Tests mirror their target

`Synergos.CMS.Tests/` folder tree mirrors `Synergos.CMS/` (and, where
relevant, `Application/`). When a file moves in the target project,
move its test in the same PR.

### 6. No code lives in the root of a project

Exceptions: `Program.cs` (Web), the single composition anchor
(`ISynergosServiceBuilder.cs` in Interfaces). Anything else belongs in
a folder with an explicit purpose.

## Anti-patterns to reject on sight

| Anti-pattern | Why it's rejected |
|--------------|-------------------|
| `Services/ServiceBase.cs` with `abstract` | Premature abstraction. Share via composition or extension. |
| `Mappers/` folder | Mapping is a method on a DTO or a small extension — not a layer. |
| `Abstractions/` folder | If it's a contract, it's in `Services/` (for Application) or `Interfaces/` (for composition). |
| `Infrastructure/` folder | The whole `Application` project is *not* infrastructure. Umbraco and external APIs each have a specific home. |
| Duplicated folder across projects (both Web and App have `Models/`) | Acceptable only when one holds ViewModels (Web) and the other holds DTOs (App). They are distinct concerns. |
