using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameRoomService
{
    private ServiceResult<RoomSnapshot> RemovePlayerUnsafe(
        string connectionId,
        string roomCode,
        string playerId,
        bool keepAsDisconnected)
    {
        _connections.Remove(connectionId);

        if (!_rooms.TryGetValue(roomCode, out var room))
        {
            return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
        }

        var player = room.FindPlayer(playerId);
        if (player is null)
        {
            return ServiceResult<RoomSnapshot>.Fail("ไม่พบผู้เล่น");
        }

        var leavingTurnIndex = room.Players.IndexOf(player);

        if (keepAsDisconnected)
        {
            player.Connected = false;
            player.ConnectionId = string.Empty;
        }
        else
        {
            room.Players.Remove(player);
        }

        if (room.Players.Count == 0)
        {
            _rooms.Remove(room.RoomCode);
            _chatByRoom.Remove(room.RoomCode);
            return ServiceResult<RoomSnapshot>.Ok(new RoomSnapshot
            {
                RoomCode = room.RoomCode,
                GameKey = room.GameKey,
                HostPlayerId = room.HostPlayerId,
                Status = GameStatus.Finished,
                BoardOptions = room.BoardOptions,
                Players = Array.Empty<PlayerSnapshot>(),
                CurrentTurnPlayerId = null,
                TurnCounter = room.TurnCounter,
                CompletedRounds = room.CompletedRounds,
                TurnDeadlineUtc = null,
                WinnerPlayerId = room.WinnerPlayerId,
                FinishReason = room.FinishReason,
                Board = null
            });
        }

        if (!room.Players.Any(x => x.PlayerId == room.HostPlayerId))
        {
            room.HostPlayerId = room.Players[0].PlayerId;
            room.Players[0].IsReady = true;
        }

        if (room.Status == GameStatus.Started)
        {
            if (!keepAsDisconnected)
            {
                if (leavingTurnIndex < room.CurrentTurnIndex)
                {
                    room.CurrentTurnIndex--;
                }

                if (room.CurrentTurnIndex >= room.Players.Count)
                {
                    room.CurrentTurnIndex = 0;
                }
            }

            if (room.Players.Count == 1)
            {
                room.Status = GameStatus.Finished;
                room.WinnerPlayerId = room.Players[0].PlayerId;
                room.FinishReason = "LastPlayerStanding";
                room.TurnDeadlineUtc = null;
            }
            else if (keepAsDisconnected &&
                     ReferenceEquals(room.CurrentTurnPlayer, player))
            {
                room.TurnDeadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(OfflineAutoRollDelayMs);
            }
            else
            {
                room.TurnDeadlineUtc = ResolveNextActionDeadlineUnsafe(room);
            }
        }

        return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
    }

    private void RemoveExistingConnectionUnsafe(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var existing))
        {
            RemovePlayerUnsafe(connectionId, existing.RoomCode, existing.PlayerId, keepAsDisconnected: false);
        }
    }

    private void BindConnectionToPlayerUnsafe(
        string connectionId,
        GameRoom room,
        PlayerState player,
        string displayName)
    {
        if (!string.IsNullOrWhiteSpace(player.ConnectionId))
        {
            _connections.Remove(player.ConnectionId);
        }

        player.ConnectionId = connectionId;
        player.Connected = true;

        var safeName = SanitizePlayerName(displayName);
        if (!string.IsNullOrWhiteSpace(safeName))
        {
            player.DisplayName = safeName;
        }

        if (player.PlayerId == room.HostPlayerId)
        {
            player.IsReady = true;
        }

        if (room.Status == GameStatus.Started &&
            ReferenceEquals(room.CurrentTurnPlayer, player))
        {
            room.TurnDeadlineUtc = ResolveNextActionDeadlineUnsafe(room);
        }

        _connections[connectionId] = new ConnectionBinding(room.RoomCode, player.PlayerId);
    }

    private void UpsertLobbyPresenceUnsafe(string connectionId, string displayName)
    {
        _lobbyPresence[connectionId] = new LobbyPresenceEntry
        {
            DisplayName = SanitizePlayerName(displayName),
            LastSeenUtc = DateTimeOffset.UtcNow
        };
    }

    private static string NormalizeRoomCode(string roomCode) =>
        roomCode.Trim().ToUpperInvariant();

    private bool TryGetGameModule(string gameKey, out IGameRoomModule gameModule)
    {
        return _gameModules.TryGetValue(GameCatalog.Normalize(gameKey), out gameModule!);
    }

    private static string NewPlayerId() => Guid.NewGuid().ToString("N")[..8];
    private static string NewSessionId() => Guid.NewGuid().ToString("N");

    private static PlayerState NewPlayerState(
        string playerId,
        string sessionId,
        string connectionId,
        string playerName,
        int avatarId,
        BoardOptions boardOptions)
    {
        var safeName = SanitizePlayerName(playerName);
        return new PlayerState
        {
            PlayerId = playerId,
            SessionId = sessionId,
            ConnectionId = connectionId,
            DisplayName = safeName,
            AvatarId = NormalizeAvatarId(avatarId),
            LuckyRerollsLeft = boardOptions.RuleOptions.LuckyRerollEnabled
                ? boardOptions.RuleOptions.LuckyRerollPerPlayer
                : 0,
            NextCheckpoint = Math.Max(1, boardOptions.RuleOptions.CheckpointInterval)
        };
    }

    private string GenerateRoomCodeUnsafe()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> code = stackalloc char[6];

        do
        {
            for (var i = 0; i < code.Length; i++)
            {
                code[i] = alphabet[Random.Shared.Next(0, alphabet.Length)];
            }
        } while (_rooms.ContainsKey(code.ToString()));

        return code.ToString();
    }

    private static string SanitizePlayerName(string input)
    {
        var name = (input ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return "ผู้เล่น";
        }

        return name.Length <= 24 ? name : name[..24];
    }

    private static int NormalizeAvatarId(int avatarId)
    {
        if (avatarId < MinAvatarId || avatarId > MaxAvatarId)
        {
            return MinAvatarId;
        }

        return avatarId;
    }

    private static RoomSnapshot ToSnapshot(GameRoom room)
    {
        var board = room.Board;
        return new RoomSnapshot
        {
            RoomCode = room.RoomCode,
            GameKey = room.GameKey,
            HostPlayerId = room.HostPlayerId,
            Status = room.Status,
            BoardOptions = room.BoardOptions,
            Players = room.Players.Select(x => new PlayerSnapshot
            {
                PlayerId = x.PlayerId,
                DisplayName = x.DisplayName,
                AvatarId = NormalizeAvatarId(x.AvatarId),
                Position = x.Position,
                Connected = x.Connected,
                IsReady = x.IsReady,
                Shields = x.Shields,
                LuckyRerollsLeft = x.LuckyRerollsLeft,
                SnakeRepellentCharges = x.SnakeRepellentCharges,
                LadderHackPending = x.LadderHackPending,
                AnchorActive = x.AnchorTurnsRemaining > 0,
                AnchorTurnsLeft = Math.Max(0, x.AnchorTurnsRemaining),
                Cash = x.Cash,
                IsBankrupt = x.IsBankrupt,
                JailTurnsRemaining = Math.Max(0, x.JailTurnsRemaining)
            }).ToArray(),
            CurrentTurnPlayerId = room.CurrentTurnPlayer?.PlayerId,
            TurnCounter = room.TurnCounter,
            CompletedRounds = room.CompletedRounds,
            TurnDeadlineUtc = room.TurnDeadlineUtc,
            WinnerPlayerId = room.WinnerPlayerId,
            FinishReason = room.FinishReason,
            Board = board is null
                ? null
                : new BoardSnapshot
                {
                    Size = board.Size,
                    Jumps = board.Jumps,
                    ForkCells = board.ForkCells,
                    ActiveFrenzySnake = room.ActiveFrenzySnake,
                    TemporaryJumps = room.TemporaryJumps.Select(x => x.Jump).ToArray(),
                    Items = room.ActiveItems.ToArray(),
                    BananaTrapCells = room.BananaTraps.Select(x => x.Cell).Distinct().OrderBy(x => x).ToArray(),
                    MonopolyCells = room.Monopoly?.Cells
                        .OrderBy(x => x.Cell)
                        .Select(x => new MonopolyCellSnapshot
                        {
                            Cell = x.Cell,
                            Name = x.Name,
                            Type = x.Type,
                            ColorGroup = x.ColorGroup,
                            Price = x.Price,
                            Rent = x.Rent,
                            Fee = x.Fee,
                            OwnerPlayerId = x.OwnerPlayerId,
                            IsMortgaged = x.IsMortgaged,
                            HouseCount = x.HouseCount,
                            HasHotel = x.HasHotel,
                            HasLandmark = x.HasLandmark,
                            HouseCost = x.HouseCost
                        })
                        .ToArray(),
                    MonopolyFreeParkingPot = room.Monopoly?.FreeParkingPot ?? 0
                },
            MonopolyState = BuildMonopolySnapshot(room)
        };
    }

    private sealed record ConnectionBinding(string RoomCode, string PlayerId);

    private sealed class LobbyPresenceEntry
    {
        public required string DisplayName { get; init; }
        public required DateTimeOffset LastSeenUtc { get; init; }
    }

    private static MonopolyStateSnapshot? BuildMonopolySnapshot(GameRoom room)
    {
        var monopoly = room.Monopoly;
        if (monopoly is null)
        {
            return null;
        }

        var playerEconomy = room.Players
            .Select(player => BuildMonopolyPlayerEconomySnapshot(monopoly, player))
            .ToArray();

        MonopolyAuctionSnapshot? auctionSnapshot = null;
        if (monopoly.ActiveAuction is not null)
        {
            var auctionCell = monopoly.FindCell(monopoly.ActiveAuction.CellId);
            auctionSnapshot = new MonopolyAuctionSnapshot
            {
                CellId = monopoly.ActiveAuction.CellId,
                CellName = auctionCell?.Name ?? $"Cell {monopoly.ActiveAuction.CellId}",
                CurrentBidAmount = monopoly.ActiveAuction.CurrentBidAmount,
                CurrentBidderPlayerId = monopoly.ActiveAuction.CurrentBidderPlayerId,
                EligiblePlayerIds = monopoly.ActiveAuction.EligiblePlayerIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                PassedPlayerIds = monopoly.ActiveAuction.PassedPlayerIds.OrderBy(x => x, StringComparer.Ordinal).ToArray()
            };
        }

        MonopolyTradeSnapshot? tradeSnapshot = null;
        if (monopoly.ActiveTradeOffer is not null)
        {
            tradeSnapshot = new MonopolyTradeSnapshot
            {
                FromPlayerId = monopoly.ActiveTradeOffer.FromPlayerId,
                ToPlayerId = monopoly.ActiveTradeOffer.ToPlayerId,
                CashGive = monopoly.ActiveTradeOffer.CashGive,
                CashReceive = monopoly.ActiveTradeOffer.CashReceive,
                GiveCells = monopoly.ActiveTradeOffer.GiveCells.ToArray(),
                ReceiveCells = monopoly.ActiveTradeOffer.ReceiveCells.ToArray()
            };
        }

        return new MonopolyStateSnapshot
        {
            Phase = monopoly.Phase,
            ActivePlayerId = monopoly.ActivePlayerId,
            PendingDecisionPlayerId = monopoly.PendingDecisionPlayerId,
            AvailableHouses = monopoly.AvailableHouses,
            AvailableHotels = monopoly.AvailableHotels,
            PendingPurchaseCellId = monopoly.PendingPurchaseCellId,
            PendingPurchasePrice = monopoly.PendingPurchasePrice,
            PendingPurchaseOwnerPlayerId = monopoly.PendingPurchaseOwnerPlayerId,
            PendingDebtToPlayerId = monopoly.PendingDebtToPlayerId,
            PendingDebtAmount = monopoly.PendingDebtAmount,
            PendingDebtReason = monopoly.PendingDebtReason,
            CurrentJailFine = ResolveCurrentJailFine(monopoly),
            ActiveAuction = auctionSnapshot,
            ActiveTradeOffer = tradeSnapshot,
            PlayerEconomy = playerEconomy
        };
    }

    private static MonopolyPlayerEconomySnapshot BuildMonopolyPlayerEconomySnapshot(
        MonopolyRoomState monopoly,
        PlayerState player)
    {
        var owned = monopoly.Cells
            .Where(x => string.Equals(x.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal))
            .ToArray();

        var houses = owned.Sum(x => Math.Max(0, x.HouseCount));
        var hotels = owned.Count(x => x.HasHotel && !x.HasLandmark);
        var landmarks = owned.Count(x => x.HasLandmark);
        var mortgaged = owned.Count(x => x.IsMortgaged);

        var assetValue = owned.Sum(cell =>
        {
            var baseValue = cell.IsMortgaged
                ? Math.Max(0, (int)Math.Floor(cell.Price / 2d))
                : Math.Max(0, cell.Price);
            var buildingValue = cell.HasLandmark
                ? cell.HouseCost * 7
                : cell.HasHotel
                    ? cell.HouseCost * 5
                    : Math.Max(0, cell.HouseCount) * cell.HouseCost;
            return baseValue + buildingValue;
        });

        var monopolySetCount = owned
            .Where(x => x.Type == MonopolyCellType.Property && !string.IsNullOrWhiteSpace(x.ColorGroup))
            .GroupBy(x => x.ColorGroup!, StringComparer.OrdinalIgnoreCase)
            .Count(group =>
            {
                var allInGroup = monopoly.Cells
                    .Where(cell =>
                        cell.Type == MonopolyCellType.Property &&
                        string.Equals(cell.ColorGroup, group.Key, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return allInGroup.Length > 0 &&
                       allInGroup.All(cell => string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal));
            });

        var netWorth = player.Cash + assetValue;

        return new MonopolyPlayerEconomySnapshot
        {
            PlayerId = player.PlayerId,
            Cash = player.Cash,
            AssetValue = assetValue,
            NetWorth = netWorth,
            PropertyCount = owned.Length,
            MonopolySetCount = monopolySetCount,
            Houses = houses,
            Hotels = hotels,
            Landmarks = landmarks,
            Mortgaged = mortgaged,
            InJail = player.JailTurnsRemaining > 0,
            IsBankrupt = player.IsBankrupt
        };
    }

    private static int ResolveCurrentJailFine(MonopolyRoomState monopoly)
    {
        var playerId = monopoly.PendingDecisionPlayerId ?? monopoly.ActivePlayerId;
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return MonopolyDefinitions.JailFine;
        }

        return monopoly.JailFineByPlayer.TryGetValue(playerId, out var fine) && fine > 0
            ? fine
            : MonopolyDefinitions.JailFine;
    }

    private static DateTimeOffset? ResolveNextActionDeadlineUnsafe(GameRoom room)
    {
        if (room.Status != GameStatus.Started)
        {
            return null;
        }

        var currentTurnPlayer = room.CurrentTurnPlayer;
        if (currentTurnPlayer is null)
        {
            return null;
        }

        PlayerState actionPlayer = currentTurnPlayer;

        if (room.GameKey.Equals(GameCatalog.Monopoly, StringComparison.OrdinalIgnoreCase) &&
            room.Monopoly is not null &&
            (room.Monopoly.Phase is MonopolyTurnPhase.AuctionInProgress or MonopolyTurnPhase.AwaitTradeResponse) &&
            !string.IsNullOrWhiteSpace(room.Monopoly.PendingDecisionPlayerId))
        {
            actionPlayer = room.FindPlayer(room.Monopoly.PendingDecisionPlayerId!) ?? currentTurnPlayer;
        }

        if (!actionPlayer.Connected)
        {
            return DateTimeOffset.UtcNow.AddMilliseconds(OfflineAutoRollDelayMs);
        }

        var rules = room.BoardOptions.RuleOptions;
        if (!rules.TurnTimerEnabled)
        {
            return room.GameKey.Equals(GameCatalog.Monopoly, StringComparison.OrdinalIgnoreCase)
                ? DateTimeOffset.UtcNow.AddSeconds(20)
                : null;
        }

        if (room.GameKey.Equals(GameCatalog.Monopoly, StringComparison.OrdinalIgnoreCase) &&
            room.Monopoly is not null &&
            room.Monopoly.Phase is MonopolyTurnPhase.AwaitManage or MonopolyTurnPhase.AwaitEndTurn)
        {
            return DateTimeOffset.UtcNow.AddSeconds(45);
        }

        var animationBufferSeconds = room.GameKey.Equals(GameCatalog.Monopoly, StringComparison.OrdinalIgnoreCase)
            ? 0
            : room.TurnCounter > 0
                ? TurnAnimationBufferSeconds
                : 0;

        return DateTimeOffset.UtcNow.AddSeconds(Math.Max(3, rules.TurnSeconds) + animationBufferSeconds);
    }
}
