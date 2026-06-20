using Microsoft.AspNetCore.Components.Server.Circuits;

namespace GrowIT.Client.Infrastructure;

public class CircuitExceptionHandlingHandler : CircuitHandler
{
    private readonly ILogger<CircuitExceptionHandlingHandler> _logger;

    public CircuitExceptionHandlingHandler(ILogger<CircuitExceptionHandlingHandler> logger)
    {
        _logger = logger;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Blazor circuit {CircuitId} closed.", circuit.Id);
        return Task.CompletedTask;
    }
}
