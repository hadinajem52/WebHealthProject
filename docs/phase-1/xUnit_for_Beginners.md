# xUnit for Beginners

This document explains what xUnit is and how tests use it in WebHealth.

## What is xUnit?

xUnit is a .NET testing framework. It lets us write small programs called tests
that check whether our application behaves as expected.

Instead of manually opening the website and checking every result, a test can do
this automatically:

1. Arrange the required input or test environment.
2. Act by calling code or making an HTTP request.
3. Assert that the result is correct.

Example:

```csharp
[Fact]
public void Adds_two_numbers()
{
    // Arrange
    var first = 2;
    var second = 3;

    // Act
    var result = first + second;

    // Assert
    Assert.Equal(5, result);
}
```

The test is successful if `result` is `5`. If the code produces another value,
xUnit reports a failure.

## xUnit is not the application

There are several pieces involved:

- **xUnit** provides attributes such as `[Fact]` and assertion methods such as
  `Assert.Equal`.
- **Microsoft.NET.Test.Sdk** provides the general .NET test execution support.
- **xunit.runner.visualstudio** lets Visual Studio, `dotnet test`, and compatible
  tools discover and run xUnit tests.
- **FluentAssertions** is an optional library used in this project for more
  readable assertions.

The test project references these packages in its `.csproj` file. Package
versions are managed centrally in `Directory.Packages.props`.

## How xUnit finds a test

xUnit discovers public methods marked with test attributes.

### `[Fact]`: one fixed test case

Use `[Fact]` when the test has one specific scenario:

```csharp
[Fact]
public async Task Liveness_endpoint_returns_ok()
{
    using var client = factory.CreateAnonymousHttpsClient();

    var response = await client.GetAsync("/health/live");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

This test always asks the same question: does `/health/live` return HTTP 200?

### `[Theory]`: the same test with different data

Use `[Theory]` when the rule is the same but the input changes:

```csharp
[Theory]
[InlineData("hello_world", "hello_world")]
[InlineData("DisplayName", "display_name")]
public void Converts_names_to_snake_case(string input, string expected)
{
    var result = DatabaseConventions.ToSnakeCase(input);

    Assert.Equal(expected, result);
}
```

xUnit runs this as two test cases. A failure tells us which input was involved.

## Assertions

An assertion is a statement about what must be true.

Common assertions in this project include:

```csharp
Assert.Equal(expected, actual);
Assert.True(condition);
Assert.False(condition);
Assert.Contains(expectedText, actualText);
Assert.DoesNotContain(unwantedText, actualText);
Assert.Single(collection);
Assert.NotNull(value);
```

The order for `Assert.Equal` is important:

```csharp
Assert.Equal(expected, actual);
```

Read it as: “the actual value should equal the expected value.”

This project also references FluentAssertions, which can express the same idea
like this:

```csharp
result.Should().Be(5);
```

Both styles check behavior; the important thing is that the test clearly states
the expected result.

## The two test projects in WebHealth

### Unit tests

Located in:

```text
tests/WebHealth.UnitTests
```

Unit tests usually test one class or one rule without starting the web server or
connecting to PostgreSQL. They are fast and useful for domain and application
logic.

### Integration tests

Located in:

```text
tests/WebHealth.IntegrationTests
```

Integration tests check that several parts work together. In this project they
can start the ASP.NET Core application in a test host and make HTTP requests to
it.

For example, `AuthenticationShellTests` verifies that:

```text
anonymous GET /       → redirect to /Account/Login
anonymous GET /health/live → 200 OK
anonymous GET /health/ready → redirect to login
authenticated GET /   → 200 OK and the shell contains the user name
```

These tests are not testing only a controller method. They exercise routing,
authorization, middleware, Razor rendering, and the response together.

## How the WebHealth test host works

`WebHealthWebApplicationFactory` inherits from
`WebApplicationFactory<Program>`.

That factory creates a test version of the WebHealth ASP.NET Core application.
It lets tests send requests without starting a separate browser or manually
launching the site.

Example:

```csharp
using var client = factory.CreateAnonymousHttpsClient(
    allowAutoRedirect: false);

var response = await client.GetAsync("/");
```

The test receives an ordinary `HttpResponseMessage`, just as a real HTTP client
would.

`allowAutoRedirect: false` is important when checking redirects. If redirects
were enabled, the client would automatically follow `/Account/Login`, and the
test would no longer see the original redirect response.

## The test authentication handler

The integration test host replaces normal authentication with
`TestAuthenticationHandler`.

If a request contains this header:

```text
X-WebHealth-Test-User: Test User
```

the handler creates a test identity. This lets a test simulate an authenticated
request without creating a real database user or typing a password.

The helper methods make the intent clear:

```csharp
factory.CreateAnonymousHttpsClient();
factory.CreateHttpsClient();
```

This is test-only behavior. The real application still uses ASP.NET Core
Identity and its authentication cookie.

## Shared fixtures

Some tests use:

```csharp
IClassFixture<WebHealthWebApplicationFactory>
```

This tells xUnit to create one shared factory for the test class and provide it
to the constructor. It avoids repeating the expensive test-host setup for every
test method in that class.

The fixture is shared for that class; tests should not depend on another test
having run first.

## Test lifecycle

For each `[Fact]` or each data row of a `[Theory]`, xUnit normally creates a new
instance of the test class and runs the test method.

A test should therefore:

- create its own input;
- avoid relying on test order;
- clean up resources it owns;
- use `using` or `await using` for clients, containers, and connections where
  appropriate.

Async tests are ordinary methods returning `Task`:

```csharp
[Fact]
public async Task Example()
{
    var response = await client.GetAsync("/");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

xUnit waits for the returned `Task`. Do not use `.Result` or `.Wait()` in an
async test because that can hide failures or cause blocking problems.

## Running the tests

From the repository root:

```powershell
dotnet test
```

Run only the unit tests:

```powershell
dotnet test tests/WebHealth.UnitTests/WebHealth.UnitTests.csproj
```

Run only the integration tests:

```powershell
dotnet test tests/WebHealth.IntegrationTests/WebHealth.IntegrationTests.csproj
```

Useful options:

```powershell
dotnet test --filter FullyQualifiedName~AuthenticationShellTests
dotnet test --list-tests
dotnet test --verbosity normal
```

`--filter` selects a smaller group of tests. This is useful while working on one
feature.

## Skipped tests and PostgreSQL

Some integration tests use Testcontainers to run PostgreSQL. If Docker is not
available, those tests may be skipped or may fail depending on the test setup.
That is different from a failed assertion:

- **Passed**: the test ran and the behavior matched the expectation.
- **Failed**: the test ran and found unexpected behavior, or the test itself
  could not complete.
- **Skipped**: the test was deliberately not run, often because a dependency is
  unavailable.

## How to read a WebHealth test

When reading a test, ask three questions:

1. **What is being arranged?** Look for a factory, client, input, or fixture.
2. **What is the action?** Usually a method call or HTTP request.
3. **What is being asserted?** Look at the `Assert` or `Should()` statements.

For example:

```csharp
[Fact]
public async Task DetailedReadiness_IsProtectedButLivenessRemainsPublic()
{
    using var client = factory.CreateAnonymousHttpsClient(
        allowAutoRedirect: false);

    var readiness = await client.GetAsync("/health/ready");
    var liveness = await client.GetAsync("/health/live");

    Assert.Equal(HttpStatusCode.Redirect, readiness.StatusCode);
    Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
}
```

The test's meaning is:

- Arrange: create an anonymous client that does not follow redirects.
- Act: request both health endpoints.
- Assert: readiness requires authentication, while liveness is public.

The method name is intentionally descriptive. A good test name tells us the
behavior that must remain true.

## What xUnit does not prove automatically

A passing test only proves the scenario that the test actually executed. It does
not prove every possible input or every production environment.

For example, the current authentication shell tests use the test authentication
handler. They prove protected-shell behavior, but they do not by themselves
prove that a real Identity password is accepted, that a disabled database user
is rejected, or that an API returns `401` instead of a cookie redirect.

Those behaviors need focused tests with the appropriate setup.

## A practical rule for adding a test

When adding a non-trivial rule:

1. Give the test a behavior-focused name.
2. Arrange the smallest useful scenario.
3. Perform one important action.
4. Assert the user-visible or correctness-critical result.
5. Add a variation when the rule has an important boundary.

The purpose of a test is not to repeat the implementation. It is to protect a
behavior that the application must continue to provide.
