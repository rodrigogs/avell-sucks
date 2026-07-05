using GamingCenter.Core;
using GamingCenter.Core.Events;
using GamingCenter.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace GamingCenter.Api.Controllers;

/// <summary>
/// Live telemetry/events endpoints.
/// </summary>
[ApiController]
public sealed class EventsController : ControllerBase
{
    private readonly IEventPublisher _publisher;

    public EventsController(IEventPublisher publisher)
    {
        _publisher = publisher;
    }

    /// <summary>
    /// Server-Sent Events stream for live telemetry.
    /// </summary>
    /// <remarks>
    /// The client should send <c>Accept: text/event-stream</c> and disconnect when finished.
    /// </remarks>
    [HttpGet("/events")]
    [Produces("text/event-stream")]
    public async Task StreamAsync(HttpResponse response, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        response.ContentType = "text/event-stream";
        response.Headers.ContentType = "text/event-stream";

        var id = 0;
        while (!ct.IsCancellationRequested)
        {
            var data = await _publisher.NextAsync(ct).ConfigureAwait(false);
            var envelope = new EventEnvelope<SystemInfo>(Guid.NewGuid().ToString(), "system.snapshot", DateTimeOffset.UtcNow, data);

            var payload = System.Text.Json.JsonSerializer.Serialize(envelope);

            await response.WriteAsync($"id: {id++}\n", ct).ConfigureAwait(false);
            await response.WriteAsync($"event: system.snapshot\n", ct).ConfigureAwait(false);
            await response.WriteAsync($"data: {payload}\n\n", ct).ConfigureAwait(false);
            await response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }
}
