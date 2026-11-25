using System.Collections.Concurrent;
using CaramelChat.Models;

namespace CaramelChat.Services;

/// <summary>Liste des postes vus récemment sur le réseau local (mémoire seulement).</summary>
public sealed class PeerRegistry
{
    private readonly ConcurrentDictionary<string, PeerInfo> _peers = new(StringComparer.Ordinal);

    /// <summary>Déclenché quand un pair apparaît, change de nom/adresse ou redevient en ligne.</summary>
    public event Action<PeerInfo>? PeerChanged;

    public IReadOnlyList<PeerInfo> All =>
        _peers.Values.OrderByDescending(p => p.LastSeen).ToList();

    public PeerInfo? Get(string peerId) =>
        _peers.TryGetValue(peerId, out var peer) ? peer : null;

    public void Update(PeerInfo peer)
    {
        var changed = false;
        _peers.AddOrUpdate(
            peer.PeerId,
            _ =>
            {
                changed = true;
                return peer;
            },
            (_, existing) =>
            {
                changed = existing.Name != peer.Name
                          || existing.MachineName != peer.MachineName
                          || existing.Ip != peer.Ip
                          || existing.Port != peer.Port
                          || existing.AvatarVersion != peer.AvatarVersion
                          || !existing.IsOnline;
                return peer;
            });

        if (changed) PeerChanged?.Invoke(peer);
    }

    public void Prune(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (var pair in _peers)
        {
            if (pair.Value.LastSeen < cutoff) _peers.TryRemove(pair.Key, out _);
        }
    }
}
