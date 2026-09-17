using System.Diagnostics.CodeAnalysis;

// xUnit test method naming uses underscores by convention
// (<c>Method_Behavior_When…</c>); CA1707 is silenced at assembly level for
// the test project so reviewers see only signal in the build output.
[assembly: SuppressMessage(
    "Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test method naming convention.")]

// Inline array literals (`new[] { "a", "b" }`) are idiomatic in xUnit
// arrange-act-assert blocks; the micro-allocation cost the rule warns
// about is irrelevant for tests.
[assembly: SuppressMessage(
    "Performance", "CA1861:Avoid constant arrays as arguments",
    Justification = "Inline array literals are idiomatic in xUnit tests.")]

// A test builds its own JsonSerializerOptions inline so the reader sees, right there, the
// shape being asserted — hoisting it to a field moves the fixture away from the assertion.
// The allocation the rule warns about happens once per test method. In production code the
// rule stands and is honoured (see HostBridgeFallback).
[assembly: SuppressMessage(
    "Performance", "CA1869:Cache and reuse 'JsonSerializerOptions' instances",
    Justification = "Inline options keep the fixture next to the assertion; one allocation per test.")]

// The namespace mirrors the project it covers (Synergos.Shared), which is what makes the test
// tree navigable. The rule protects consumers in other languages; a test assembly has none.
[assembly: SuppressMessage(
    "Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Test namespaces mirror the project under test; nothing consumes this assembly.",
    Scope = "namespace", Target = "~N:Synergos.CMS.Tests.Shared")]
