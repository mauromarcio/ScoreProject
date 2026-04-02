// File: VolleyScore/Hubs/ScoreHub.cs
// Purpose: SignalR hub that broadcasts real-time score updates to all connected display clients.
//          The operator court view calls server-side score methods; all clients subscribed to a
//          match group receive immediate updates without polling.

using Microsoft.AspNetCore.SignalR;

namespace VolleyScore.Hubs;

public class ScoreHub : Hub
{
    /// <summary>
    /// A display client calls this on connect to subscribe to updates for a specific match.
    /// Group name format: "match-{matchId}"
    /// </summary>
    public async Task JoinMatchGroup(int matchId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"match-{matchId}");
    }

    /// <summary>Leave a match group (called on tab close or explicit disconnect)</summary>
    public async Task LeaveMatchGroup(int matchId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"match-{matchId}");
    }

    /// <summary>
    /// Broadcast updated score data to all clients in the match group.
    /// Called by ScoreController after persisting the score change.
    /// </summary>
    public async Task BroadcastScoreUpdate(int matchId, object scoreData)
    {
        await Clients.Group($"match-{matchId}").SendAsync("ScoreUpdated", scoreData);
    }
}
