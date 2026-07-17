# Folder Layout — "Where does this go?"

A lookup table for every kind of file you might be tempted to add.

## Synergos.CMS.Web (Umbraco host)

| If you are adding… | It goes in… |
|--------------------|-------------|
| A new `IComposer` that wires DI | `Composers/` |
| An MVC controller rendering a page | `Controllers/{Feature}/` |
| A Web-API controller | `Controllers/Api/{Feature}/` |
| A ViewModel for a controller | `Models/{Feature}/` |
| A Razor view for a document type | `Views/{DocTypeName}.cshtml` |
| A partial view reused across templates | `Views/Partials/` |
| A macro handler | `Views/MacroPartials/` |
| A property value converter | `ValueConverters/` |
| An Umbraco notification handler (`ContentSavedNotification`, etc.) | `Notifications/` |
| A parameter resolver for a macro | `Resolvers/` |
| A helper service used only by controllers/views | `Services/` |
| A custom property editor (JS/HTML/manifest) | `App_Plugins/{EditorName}/` |
| A runtime JSON config file | `Config/{Name}.json` |

## Synergos.CMS.Application

| If you are adding… | It goes in… |
|--------------------|-------------|
| A request DTO (inbound from Web) | `Dto/Requests/` |
| A response DTO (outbound to Web) | `Dto/Responses/` |
| A constants class (route names, keys) | `Dto/Constants/` |
| An enum | `Dto/Enums/` |
| A new business service interface | `Services/I{Name}Service.cs` |
| The implementation of that service | `Services/Impl/{Name}Service.cs` |
| A proxy to an external HTTP API | `Proxies/Impl/{Name}Proxy.cs` + contract in `Proxies/I{Name}Proxy.cs` |
| A configuration POCO (binds from appsettings) | `Configuration/{Name}Options.cs` |
| A configuration base/abstraction | `Configuration/Base/` |
| An extension method | `Extensions/` |

## Synergos.CMS.Interfaces

Add only composition contracts here — and only when they genuinely need
to be referenced by two projects that can't reference each other.

99% of the time, the answer is: **this belongs in `Application/Services/`,
not here.**

## Synergos.CMS.Tests

Mirror of `Synergos.CMS.Web`. If you test a controller at
`Synergos.CMS.Web/Controllers/Foo/BarController.cs`, the test goes at
`Synergos.CMS.Tests/Controllers/Foo/BarControllerTests.cs`.

`FakeConfigFiles/` is for JSON fixtures referenced by config-driven tests.

## Rules when creating a new feature

A "feature" is a user-visible capability. When the first line of code for
a feature is written:

1. **Do not** create a `{Feature}/` folder at the project root.
2. **Do** pick the right existing folder by the *kind* of file:
   - Controller? → `Controllers/{Feature}/`
   - Service? → `Application/Services/{Feature}Service`
   - ViewModel? → `Models/{Feature}/`
3. Group sub-features inside the same folder only when there are 3+
   files in a single feature bucket. Premature grouping is how
   `epicfail2` ended up with 234 classes under `Application/`.

## Rules when removing a feature

- Delete empty folders. Don't leave `.gitkeep` breadcrumbs if a folder is
  meant to be permanent (e.g. `Composers/`) — those must have real files
  or we're lying about their purpose.
