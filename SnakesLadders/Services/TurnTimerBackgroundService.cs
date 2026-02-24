using Microsoft.AspNetCore.SignalR;
using SnakesLadders.Hubs;

namespace SnakesLadders.Services;

public sealed class TurnTimerBackgroundService(
    IGameRoomService roomService,
    IHubContext<GameHub> hubContext,
    ILogger<TurnTimerBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatches = roomService.ProcessExpiredTurns();
                foreach (var dispatch in dispatches)
                {
                    await hubContext.Clients.Group(dispatch.RoomCode)
                        .SendAsync("DiceRolled", dispatch.Payload, cancellationToken: stoppingToken);

                    await hubContext.Clients.Group(dispatch.RoomCode)
                        .SendAsync("RoomUpdated", dispatch.Payload.Room, cancellationToken: stoppingToken);

                    if (dispatch.Payload.Turn.IsGameFinished)
                    {
                        await hubContext.Clients.Group(dispatch.RoomCode)
                            .SendAsync("GameFinished", dispatch.Payload, cancellationToken: stoppingToken);

                        var reset = roomService.ResetFinishedGame(dispatch.RoomCode);
                        if (reset.Success && reset.Value is not null)
                        {
                            await hubContext.Clients.Group(dispatch.RoomCode)
                                .SendAsync("RoomUpdated", reset.Value, cancellationToken: stoppingToken);
                        }
                    }
                    else
                    {
                        await hubContext.Clients.Group(dispatch.RoomCode)
                            .SendAsync("TurnChanged", dispatch.Payload.Room.CurrentTurnPlayerId, cancellationToken: stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process expired turns.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
