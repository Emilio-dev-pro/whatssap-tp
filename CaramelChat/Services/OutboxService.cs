namespace CaramelChat.Services;

/// <summary>
/// File d'attente d'envoi : toutes les 5 secondes, les messages « queued » sont renvoyés
/// dès que le destinataire redevient joignable (il vient de démarrer, par exemple).
/// </summary>
public sealed class OutboxService : BackgroundService
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly ChatService _chat;
    private readonly ILogger<OutboxService> _logger;

    public OutboxService(ChatService chat, ILogger<OutboxService> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _chat.RetryPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Reprise de la file d'attente impossible");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
