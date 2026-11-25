using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace CaramelChat.Services;

/// <summary>Petit hub WebSocket : pousse les nouveaux messages à l'interface ouverte.</summary>
public sealed class RealtimeHub
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ILogger<RealtimeHub> _logger;

    public RealtimeHub(ILogger<RealtimeHub> logger) => _logger = logger;

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        _sockets[id] = socket;
        _logger.LogDebug("Client temps réel connecté ({Count} au total)", _sockets.Count);

        var buffer = new byte[2048];
        try
        {
            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException)
        {
            // fermeture du navigateur
        }
        catch (WebSocketException)
        {
            // connexion interrompue
        }
        finally
        {
            _sockets.TryRemove(id, out _);
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "au revoir", CancellationToken.None);
                }
                catch
                {
                    // ignoré
                }
            }
        }
    }

    public async Task BroadcastAsync(object payload)
    {
        if (_sockets.IsEmpty) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);

        await _sendLock.WaitAsync();
        try
        {
            foreach (var pair in _sockets)
            {
                var socket = pair.Value;
                if (socket.State != WebSocketState.Open)
                {
                    _sockets.TryRemove(pair.Key, out _);
                    continue;
                }

                try
                {
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Envoi temps réel impossible, client retiré");
                    _sockets.TryRemove(pair.Key, out _);
                }
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
