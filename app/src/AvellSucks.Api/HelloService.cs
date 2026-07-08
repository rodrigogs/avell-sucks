using AvellSucks.Api.Models;

namespace AvellSucks.Api.Services;

/// <summary>
/// Trivial use case for hello-world automation.
/// </summary>
public interface IHelloService
{
    HelloResponse Respond(string? name);
}

/// <summary>
/// Default implementation, used by <see cref="HelloController"/>.
/// </summary>
public sealed class DefaultHelloService : IHelloService
{
    public HelloResponse Respond(string? name) => new(
        Message: string.IsNullOrWhiteSpace(name) ? "Hello, world!" : $"Hello, {name}!",
        RequestedAt: DateTimeOffset.UtcNow);
}
