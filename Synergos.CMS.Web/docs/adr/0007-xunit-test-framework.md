# ADR 0007 — xUnit as the test framework

- **Status:** Accepted
- **Date:** 2026-04-17
- **Deciders:** Project owner

## Context

.NET has three mainstream test frameworks: xUnit, NUnit, and MSTest.

- The reference project `NS.Booking.CMS` uses **NUnit + Moq**. That
  choice made sense in 2019 when NUnit had better tooling parity.
- **xUnit** is the default for `dotnet new xunit`, is the framework
  used by the .NET runtime team itself, and has the cleanest
  isolation semantics (one test class instance per test method —
  no shared mutable state by default).
- Synergos.CMS has no tests yet. The cost of picking is zero.

## Decision

Use **xUnit** (`xunit` + `xunit.runner.visualstudio`) for all test
projects. Version is managed centrally in `Directory.Packages.props`
(see ADR 0004).

No preference is imposed on mocking libraries yet — one will be chosen
when the first test that actually needs a mock is written, and a
successor note will be added to this ADR at that point.

## Consequences

**Positive**
- `dotnet test` works out of the box with the template scaffolded by
  `dotnet new xunit`.
- Test classes are instantiated per-test, so state leakage between
  tests is structurally prevented.
- Matches the ecosystem default; future contributors familiar with
  modern .NET test patterns don't need to re-learn NUnit's `[SetUp]`
  / `[TearDown]` lifecycle.

**Negative**
- Moving tests from `NS.Booking.CMS` (if we ever borrow coverage
  patterns) requires small syntactic translation: `[Test]` → `[Fact]`,
  `Assert.AreEqual` → `Assert.Equal`, `[SetUp]` → constructor,
  `[TearDown]` → `IDisposable.Dispose`.

## Alternatives considered

- **NUnit**, to match `NS.Booking.CMS` — rejected, no carryover tests
  exist, so compatibility isn't a real constraint.
- **MSTest**, to match Visual Studio defaults — rejected, least
  popular of the three in modern .NET codebases.
