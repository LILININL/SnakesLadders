using SnakesLadders.Contracts;
using SnakesLadders.Domain;

namespace SnakesLadders.Services;

public sealed partial class GameRoomService
{
    public ServiceResult<RoomSnapshot> ResetFinishedGame(string roomCode)
    {
        lock (_sync)
        {
            var normalized = NormalizeRoomCode(roomCode);
            if (!_rooms.TryGetValue(normalized, out var room))
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
            }

            if (room.Status != GameStatus.Finished)
            {
                return ServiceResult<RoomSnapshot>.Fail("ห้องนี้ยังไม่จบเกม");
            }

            room.Players.RemoveAll(x => !x.Connected);
            if (room.Players.Count == 0)
            {
                _rooms.Remove(normalized);
                _chatByRoom.Remove(normalized);
                return ServiceResult<RoomSnapshot>.Fail("ห้องถูกปิดเพราะไม่มีผู้เล่นออนไลน์อยู่แล้ว");
            }

            if (!room.Players.Any(x => x.PlayerId == room.HostPlayerId))
            {
                room.HostPlayerId = ResolvePromotedHostPlayerUnsafe(room).PlayerId;
            }

            if (!TryGetGameModule(room.GameKey, out var gameModule))
            {
                return ServiceResult<RoomSnapshot>.Fail($"ยังไม่รองรับเกม {room.GameKey}");
            }
            
            gameModule.ResetFinishedGame(room);

            return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
        }
    }

    public ServiceResult<ChatMessage> SendChat(string connectionId, SendChatRequest request)
    {
        lock (_sync)
        {
            if (!_connections.TryGetValue(connectionId, out var binding))
            {
                return ServiceResult<ChatMessage>.Fail("คุณยังไม่ได้เข้าห้อง");
            }

            var roomCode = NormalizeRoomCode(request.RoomCode);
            if (!binding.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<ChatMessage>.Fail("คุณไม่ได้อยู่ในห้องนี้");
            }

            if (!_rooms.TryGetValue(roomCode, out var room))
            {
                return ServiceResult<ChatMessage>.Fail("ไม่พบห้องที่ระบุ");
            }

            var player = room.FindPlayer(binding.PlayerId);
            if (player is null)
            {
                return ServiceResult<ChatMessage>.Fail("ไม่พบผู้เล่น");
            }

            var message = (request.Message ?? string.Empty).Trim();
            if (message.Length == 0)
            {
                return ServiceResult<ChatMessage>.Fail("ข้อความว่างเปล่า");
            }

            if (message.Length > 300)
            {
                return ServiceResult<ChatMessage>.Fail("ข้อความยาวเกินไป");
            }

            var chat = new ChatMessage
            {
                MessageId = Guid.NewGuid().ToString("N")[..12],
                RoomCode = roomCode,
                PlayerId = player.PlayerId,
                DisplayName = player.DisplayName,
                Message = message,
                SentAtUtc = DateTimeOffset.UtcNow
            };

            if (!_chatByRoom.TryGetValue(roomCode, out var roomChat))
            {
                roomChat = new List<ChatMessage>();
                _chatByRoom[roomCode] = roomChat;
            }

            roomChat.Add(chat);
            if (roomChat.Count > 100)
            {
                roomChat.RemoveRange(0, roomChat.Count - 100);
            }

            return ServiceResult<ChatMessage>.Ok(chat);
        }
    }

    public ServiceResult<RoomSnapshot> LeaveRoom(string connectionId, string? roomCode = null)
    {
        lock (_sync)
        {
            if (!_connections.TryGetValue(connectionId, out var binding))
            {
                return ServiceResult<RoomSnapshot>.Fail("การเชื่อมต่อยังไม่ได้ผูกกับห้อง");
            }

            if (roomCode is not null &&
                !binding.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<RoomSnapshot>.Fail("การเชื่อมต่อนี้ไม่ได้อยู่ในห้องที่ระบุ");
            }

            var keepSeat = _rooms.TryGetValue(binding.RoomCode, out var room) &&
                           room.Status == GameStatus.Started;
            return RemovePlayerUnsafe(connectionId, binding.RoomCode, binding.PlayerId, keepAsDisconnected: keepSeat);
        }
    }

    public ServiceResult<RoomSnapshot> HandleDisconnect(string connectionId)
    {
        lock (_sync)
        {
            _lobbyPresence.Remove(connectionId);

            if (!_connections.TryGetValue(connectionId, out var binding))
            {
                return ServiceResult<RoomSnapshot>.Fail("การเชื่อมต่อยังไม่ได้ผูกกับห้อง");
            }

            var keepSeat = _rooms.TryGetValue(binding.RoomCode, out var room) &&
                           room.Status == GameStatus.Started;
            return RemovePlayerUnsafe(connectionId, binding.RoomCode, binding.PlayerId, keepAsDisconnected: keepSeat);
        }
    }

    public ServiceResult<RoomSnapshot> GetRoom(string roomCode)
    {
        lock (_sync)
        {
            var normalized = NormalizeRoomCode(roomCode);
            if (!_rooms.TryGetValue(normalized, out var room))
            {
                return ServiceResult<RoomSnapshot>.Fail("ไม่พบห้องที่ระบุ");
            }

            return ServiceResult<RoomSnapshot>.Ok(ToSnapshot(room));
        }
    }

    public IReadOnlyList<PublicRoomSummary> GetPublicRooms()
    {
        lock (_sync)
        {
            return _rooms.Values
                .Where(x => x.Status == GameStatus.Waiting)
                .OrderByDescending(x => x.Players.Count)
                .ThenBy(x => x.RoomCode, StringComparer.Ordinal)
                .Take(100)
                .Select(x =>
                {
                    var host = x.FindPlayer(x.HostPlayerId);
                    return new PublicRoomSummary
                    {
                        RoomCode = x.RoomCode,
                        GameKey = x.GameKey,
                        Status = x.Status,
                        HostName = host?.DisplayName ?? "หัวห้อง",
                        PlayerCount = x.Players.Count,
                        BoardSize = x.BoardOptions.BoardSize,
                        DensityMode = x.BoardOptions.DensityMode
                    };
                })
                .ToArray();
        }
    }

    public IReadOnlyList<PublicGameSummary> GetAvailableGames()
    {
        lock (_sync)
        {
            return _gameModules.Values
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new PublicGameSummary
                {
                    GameKey = x.GameKey,
                    DisplayName = x.DisplayName,
                    Description = x.Description,
                    IsAvailable = x.IsAvailable
                })
                .ToArray();
        }
    }

    public void UpsertLobbyPresence(string connectionId, string displayName)
    {
        lock (_sync)
        {
            UpsertLobbyPresenceUnsafe(connectionId, displayName);
        }
    }

    public IReadOnlyList<LobbyOnlineUser> GetLobbyOnlineUsers()
    {
        lock (_sync)
        {
            return _lobbyPresence
                .Select(x =>
                {
                    _connections.TryGetValue(x.Key, out var binding);
                    return new LobbyOnlineUser
                    {
                        ConnectionId = x.Key,
                        DisplayName = x.Value.DisplayName,
                        RoomCode = binding?.RoomCode
                    };
                })
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ConnectionId, StringComparer.Ordinal)
                .Take(200)
                .ToArray();
        }
    }

    public IReadOnlyList<AutoActionDispatch> ProcessExpiredActions()
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var dispatches = new List<AutoActionDispatch>();

            foreach (var room in _rooms.Values.Where(x =>
                         x.Status == GameStatus.Started &&
                         x.TurnDeadlineUtc is not null &&
                         x.TurnDeadlineUtc <= now))
            {
                var currentPlayer = room.CurrentTurnPlayer;
                if (currentPlayer is null)
                {
                    continue;
                }

                if (!TryGetGameModule(room.GameKey, out var gameModule))
                {
                    continue;
                }
                if (!gameModule.IsAvailable)
                {
                    continue;
                }

                var autoRequest = BuildAutoTimeoutActionRequestUnsafe(room, currentPlayer);
                var actor = ResolveAutoActionPlayerUnsafe(room, autoRequest.ActionType) ?? currentPlayer;
                if (!CanActorPerformActionUnsafe(room, actor, autoRequest.ActionType))
                {
                    actor = currentPlayer;
                }

                var result = gameModule.SubmitGameAction(room, actor, autoRequest, isAutoAction: true);
                if (!result.Success || result.Value is null)
                {
                    room.TurnDeadlineUtc = ResolveNextActionDeadlineUnsafe(room);
                    continue;
                }

                room.TurnDeadlineUtc = ResolveNextActionDeadlineUnsafe(room);

                dispatches.Add(new AutoActionDispatch
                {
                    RoomCode = room.RoomCode,
                    PlayerId = actor.PlayerId,
                    ActionType = autoRequest.ActionType,
                    EmitDiceRolled =
                        autoRequest.ActionType == GameActionType.RollDice &&
                        !room.GameKey.Equals(GameCatalog.Monopoly, StringComparison.OrdinalIgnoreCase),
                    Payload = new TurnEnvelope
                    {
                        Turn = result.Value,
                        Room = ToSnapshot(room)
                    }
                });
            }

            return dispatches;
        }
    }

    private static SubmitGameActionRequest BuildAutoTimeoutActionRequestUnsafe(
        GameRoom room,
        PlayerState currentPlayer)
    {
        if (!room.GameKey.Equals(GameCatalog.Monopoly, StringComparison.OrdinalIgnoreCase) ||
            room.Monopoly is null)
        {
            return BuildAutoRequest(room, GameActionType.RollDice);
        }

        return room.Monopoly.Phase switch
        {
            MonopolyTurnPhase.AwaitRoll => BuildAutoRequest(room, GameActionType.RollDice),
            MonopolyTurnPhase.AwaitJailDecision => BuildMonopolyAutoJailRequest(room, room.Monopoly, currentPlayer),
            MonopolyTurnPhase.AwaitPurchaseDecision => BuildMonopolyAutoPurchaseRequest(room, room.Monopoly, currentPlayer),
            MonopolyTurnPhase.AuctionInProgress => BuildAutoRequest(room, GameActionType.PassAuction),
            MonopolyTurnPhase.AwaitTradeResponse => BuildAutoRequest(
                room,
                GameActionType.RejectTrade,
                targetPlayerId: room.Monopoly.PendingDecisionPlayerId),
            MonopolyTurnPhase.AwaitManage => BuildMonopolyAutoManageRequest(room, room.Monopoly, currentPlayer),
            MonopolyTurnPhase.AwaitEndTurn => BuildMonopolyAutoManageRequest(room, room.Monopoly, currentPlayer),
            _ => BuildAutoRequest(room, GameActionType.EndTurn)
        };
    }

    private static SubmitGameActionRequest BuildAutoRequest(
        GameRoom room,
        GameActionType actionType,
        int? cellId = null,
        string? targetPlayerId = null)
    {
        return new SubmitGameActionRequest
        {
            RoomCode = room.RoomCode,
            ActionType = actionType,
            ForkChoice = ForkPathChoice.Safe,
            UseLuckyReroll = false,
            Monopoly = new MonopolyActionPayload
            {
                CellId = cellId,
                TargetPlayerId = targetPlayerId
            }
        };
    }

    private static SubmitGameActionRequest BuildMonopolyAutoJailRequest(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player)
    {
        var fine = ResolveAutoCurrentJailFine(state, player.PlayerId);
        var reserve = ResolveAutoCashReserve(room, state, player);
        var ownedCount = CountOwnedAssets(state, player.PlayerId);
        var shouldPayFine = fine > 0 &&
                            player.Cash >= fine &&
                            (player.Cash >= fine + Math.Max(120, reserve / 2) ||
                             ownedCount >= 5 ||
                             fine <= Math.Max(120, player.Cash / 6));

        return BuildAutoRequest(
            room,
            shouldPayFine ? GameActionType.PayJailFine : GameActionType.TryJailRoll);
    }

    private static SubmitGameActionRequest BuildMonopolyAutoPurchaseRequest(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player)
    {
        var cellId = state.PendingPurchaseCellId;
        var cell = cellId is > 0 ? state.FindCell(cellId.Value) : null;
        if (cell is null)
        {
            return BuildAutoRequest(room, GameActionType.DeclinePurchase);
        }

        var price = state.PendingPurchasePrice > 0
            ? state.PendingPurchasePrice
            : string.IsNullOrWhiteSpace(state.PendingPurchaseOwnerPlayerId)
                ? ResolveAutoCityPrice(cell, state.CityPriceGrowthRounds)
                : ResolveAutoTakeoverPrice(cell, state.CityPriceGrowthRounds);
        if (price <= 0 || player.Cash < price)
        {
            return BuildAutoRequest(room, GameActionType.DeclinePurchase, cell.Cell);
        }

        var difficulty = ResolveAutoDifficulty(player);
        var personality = ResolveAutoPersonality(room, state, player);
        var reserve = ResolveAutoCashReserve(room, state, player);
        var remainingCash = player.Cash - price;
        var completesSet = WouldAutoCompleteColorSet(state, player.PlayerId, cell);
        var priority = ResolveAutoPropertyPriority(room, state, player, cell);
        var directBankBuy = string.IsNullOrWhiteSpace(state.PendingPurchaseOwnerPlayerId);
        var strongReserve = Math.Max(80, reserve);
        var softReserve = Math.Max(40, reserve - 140);
        var opportunisticReserve = Math.Max(20, reserve / 3);
        var strongThreshold = ResolveAutoPurchaseThreshold(personality, difficulty, directBankBuy, conservative: false);
        var softThreshold = ResolveAutoPurchaseThreshold(personality, difficulty, directBankBuy, conservative: true);
        var cheapEnough = price <= Math.Max(150, player.Cash / 5);

        var buy = completesSet ||
                  (directBankBuy &&
                   ((remainingCash >= strongReserve && priority >= strongThreshold) ||
                    (remainingCash >= softReserve && priority >= softThreshold) ||
                    (cheapEnough && remainingCash >= opportunisticReserve && personality != BotPersonality.Banker))) ||
                  (!directBankBuy &&
                   ((remainingCash >= softReserve && priority >= strongThreshold) ||
                    (remainingCash >= opportunisticReserve && priority >= softThreshold)));

        return BuildAutoRequest(
            room,
            buy ? GameActionType.BuyProperty : GameActionType.DeclinePurchase,
            cell.Cell);
    }

    private static SubmitGameActionRequest BuildMonopolyAutoManageRequest(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player)
    {
        if (player.Cash < 0 || state.PendingDebtAmount > 0)
        {
            var debtAction = BuildMonopolyAutoDebtRequest(room, state, player);
            return debtAction ?? BuildAutoRequest(room, GameActionType.DeclareBankruptcy);
        }

        var upgradeAction = BuildMonopolyAutoUpgradeRequest(room, state, player);
        if (upgradeAction is not null)
        {
            return upgradeAction;
        }

        var unmortgageAction = BuildMonopolyAutoUnmortgageRequest(room, state, player);
        if (unmortgageAction is not null)
        {
            return unmortgageAction;
        }

        return BuildAutoRequest(room, GameActionType.EndTurn);
    }

    private static SubmitGameActionRequest? BuildMonopolyAutoDebtRequest(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player)
    {
        var requiredCash = Math.Max(Math.Max(0, -player.Cash), Math.Max(0, state.PendingDebtAmount));
        var mortgageCandidate = state.Cells
            .Where(cell =>
                string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal) &&
                !cell.IsMortgaged &&
                cell.HouseCount <= 0 &&
                !cell.HasHotel &&
                !cell.HasLandmark)
            .Select(cell => new
            {
                Cell = cell,
                Value = ResolveAutoMortgageValue(cell, state.CityPriceGrowthRounds),
                Score = ResolveAutoStrategicExitScore(room, state, player, cell)
            })
            .Where(entry => entry.Value > 0)
            .OrderBy(entry => entry.Value >= requiredCash ? 0 : 1)
            .ThenBy(entry => entry.Value >= requiredCash ? entry.Value : int.MaxValue)
            .ThenBy(entry => entry.Score)
            .ThenByDescending(entry => entry.Value)
            .FirstOrDefault();

        if (mortgageCandidate is not null)
        {
            return BuildAutoRequest(room, GameActionType.Mortgage, mortgageCandidate.Cell.Cell);
        }

        var creditor = !string.IsNullOrWhiteSpace(state.PendingDebtToPlayerId)
            ? room.FindPlayer(state.PendingDebtToPlayerId!)
            : null;
        var propertySaleCandidate = state.Cells
            .Where(cell =>
                string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal) &&
                (!cell.HasLandmark || creditor is null))
            .Select(cell => new
            {
                Cell = cell,
                Value = creditor is null
                    ? ResolveAutoBankLiquidationValue(cell, state.CityPriceGrowthRounds)
                    : ResolveAutoTakeoverPrice(cell, state.CityPriceGrowthRounds),
                Score = ResolveAutoStrategicExitScore(room, state, player, cell)
            })
            .Where(entry =>
            {
                if (entry.Value <= 0)
                {
                    return false;
                }

                if (creditor is null || creditor.IsBankrupt)
                {
                    return true;
                }

                var surplus = Math.Max(0, entry.Value - requiredCash);
                return creditor.Cash >= surplus;
            })
            .OrderBy(entry => entry.Score)
            .ThenBy(entry => entry.Value >= requiredCash ? 0 : 1)
            .ThenBy(entry => entry.Value)
            .FirstOrDefault();

        if (propertySaleCandidate is not null)
        {
            return BuildAutoRequest(room, GameActionType.SellProperty, propertySaleCandidate.Cell.Cell);
        }

        var buildLiquidationCandidate = state.Cells
            .Where(cell =>
                string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal) &&
                (cell.HouseCount > 0 || cell.HasHotel || cell.HasLandmark) &&
                CanAutoSellEvenly(state, player.PlayerId, cell))
            .Select(cell => new
            {
                Cell = cell,
                Value = ResolveAutoBuildingLiquidationValue(cell),
                Score = ResolveAutoStrategicExitScore(room, state, player, cell)
            })
            .Where(entry => entry.Value > 0)
            .OrderBy(entry => entry.Score)
            .ThenBy(entry => entry.Value >= requiredCash ? 0 : 1)
            .ThenBy(entry => entry.Value)
            .FirstOrDefault();

        if (buildLiquidationCandidate is not null)
        {
            return BuildAutoRequest(room, GameActionType.SellHouse, buildLiquidationCandidate.Cell.Cell);
        }

        return null;
    }

    private static SubmitGameActionRequest? BuildMonopolyAutoUpgradeRequest(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player)
    {
        if (state.UpgradeUsedThisTurn || state.UpgradeEligibleCellIds.Count == 0)
        {
            return null;
        }

        var difficulty = ResolveAutoDifficulty(player);
        var personality = ResolveAutoPersonality(room, state, player);
        var reserve = ResolveAutoCashReserve(room, state, player);
        var candidate = state.UpgradeEligibleCellIds
            .Select(state.FindCell)
            .Where(cell =>
                cell is not null &&
                string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal) &&
                !cell.IsMortgaged &&
                !cell.HasLandmark)
            .Select(cell => new
            {
                Cell = cell!,
                Cost = ResolveAutoNextUpgradeCost(cell!),
                Gain = ResolveAutoUpgradeGain(room, state, player.PlayerId, cell!)
            })
            .Where(entry => entry.Cost > 0 && player.Cash >= entry.Cost)
            .OrderByDescending(entry => entry.Gain)
            .ThenBy(entry => entry.Cost)
            .FirstOrDefault();

        if (candidate is null)
        {
            return null;
        }

        var remainingCash = player.Cash - candidate.Cost;
        var minimumGain = ResolveAutoUpgradeMinimumGain(personality, difficulty);
        var minimumReserveAfterUpgrade = ResolveAutoUpgradeReserveFloor(reserve, personality, difficulty);
        if (remainingCash < minimumReserveAfterUpgrade && candidate.Gain < minimumGain)
        {
            return null;
        }

        return BuildAutoRequest(room, GameActionType.BuildHouse, candidate.Cell.Cell);
    }

    private static SubmitGameActionRequest? BuildMonopolyAutoUnmortgageRequest(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player)
    {
        var difficulty = ResolveAutoDifficulty(player);
        var personality = ResolveAutoPersonality(room, state, player);
        var reserve = ResolveAutoCashReserve(room, state, player);
        var reserveBuffer = ResolveAutoUnmortgageReserveBuffer(personality, difficulty);
        var candidate = state.Cells
            .Where(cell =>
                string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal) &&
                cell.IsMortgaged)
            .Select(cell => new
            {
                Cell = cell,
                Cost = ResolveAutoUnmortgageCost(cell, state.CityPriceGrowthRounds),
                Score = ResolveAutoStrategicExitScore(room, state, player, cell)
            })
            .Where(entry => entry.Cost > 0 && player.Cash - entry.Cost >= reserve + reserveBuffer)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Cost)
            .FirstOrDefault();

        return candidate is null
            ? null
            : BuildAutoRequest(room, GameActionType.Unmortgage, candidate.Cell.Cell);
    }

    private static int ResolveAutoCashReserve(GameRoom room, MonopolyRoomState state, PlayerState player)
    {
        var difficulty = ResolveAutoDifficulty(player);
        var baseReserve = ResolveBaseAutoCashReserve(room, state, player, difficulty);
        var personality = ResolveAutoPersonality(room, state, player, difficulty, baseReserve);
        var personalityAdjustment = personality switch
        {
            BotPersonality.Collector => -35,
            BotPersonality.Builder => -15,
            BotPersonality.Banker => 85,
            _ => 0
        };
        return Math.Max(40, baseReserve + personalityAdjustment);
    }

    private static int ResolveBaseAutoCashReserve(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        BotDifficulty difficulty)
    {
        var ownedCount = CountOwnedAssets(state, player.PlayerId);
        return difficulty switch
        {
            BotDifficulty.Aggressive => 90 + (room.CompletedRounds * 28) + Math.Min(90, ownedCount * 10),
            _ => 140 + (room.CompletedRounds * 35) + Math.Min(120, ownedCount * 12)
        };
    }

    private static int CountOwnedAssets(MonopolyRoomState state, string playerId)
    {
        return state.Cells.Count(cell =>
            string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal));
    }

    private static int ResolveAutoCurrentJailFine(MonopolyRoomState state, string playerId)
    {
        return state.JailFineByPlayer.TryGetValue(playerId, out var fine) && fine > 0
            ? fine
            : MonopolyDefinitions.JailFine;
    }

    private static BotDifficulty ResolveAutoDifficulty(PlayerState player)
    {
        if (player.IsBot)
        {
            return player.BotDifficulty;
        }

        return BotDifficulty.Aggressive;
    }

    private static BotPersonality ResolveDisplayedBotPersonality(GameRoom room, PlayerState player)
    {
        if (!player.IsBot)
        {
            return BotPersonality.Adaptive;
        }

        if (!room.GameKey.Equals(GameCatalog.Monopoly, StringComparison.OrdinalIgnoreCase) ||
            room.Monopoly is null ||
            room.Status != GameStatus.Started)
        {
            return NormalizeBotPersonality(player.BotPersonality);
        }

        return ResolveAutoPersonality(room, room.Monopoly, player, persist: false);
    }

    private static BotPersonality ResolveAutoPersonality(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        bool persist = true)
    {
        var difficulty = ResolveAutoDifficulty(player);
        var baseReserve = ResolveBaseAutoCashReserve(room, state, player, difficulty);
        return ResolveAutoPersonality(room, state, player, difficulty, baseReserve, persist);
    }

    private static BotPersonality ResolveAutoPersonality(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        BotDifficulty difficulty,
        int baseReserve,
        bool persist = true)
    {
        var configured = player.IsBot
            ? NormalizeBotPersonality(player.BotPersonality)
            : BotPersonality.Adaptive;

        BotPersonality resolved;
        if (configured != BotPersonality.Adaptive)
        {
            resolved = configured;
        }
        else if (state.PendingDebtAmount > 0 || player.Cash < Math.Max(70, (int)Math.Ceiling(baseReserve * 0.7d)))
        {
            resolved = BotPersonality.Banker;
        }
        else if (HasAffordableAutoUpgradeOpportunity(state, player) || CountAutoMonopolySets(state, player.PlayerId) > 0)
        {
            resolved = BotPersonality.Builder;
        }
        else if (CountOwnedAssets(state, player.PlayerId) < 5 || room.CompletedRounds < 3)
        {
            resolved = BotPersonality.Collector;
        }
        else
        {
            resolved = difficulty == BotDifficulty.Aggressive
                ? BotPersonality.Collector
                : BotPersonality.Builder;
        }

        if (persist)
        {
            player.ActiveBotPersonality = resolved;
        }

        return resolved;
    }

    private static bool HasAffordableAutoUpgradeOpportunity(MonopolyRoomState state, PlayerState player)
    {
        foreach (var cellId in state.UpgradeEligibleCellIds)
        {
            var cell = state.FindCell(cellId);
            if (cell is null ||
                !string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal) ||
                cell.IsMortgaged ||
                cell.HasLandmark)
            {
                continue;
            }

            if (player.Cash >= ResolveAutoNextUpgradeCost(cell))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountAutoMonopolySets(MonopolyRoomState state, string playerId)
    {
        return state.Cells
            .Where(cell =>
                cell.Type == MonopolyCellType.Property &&
                !string.IsNullOrWhiteSpace(cell.ColorGroup))
            .GroupBy(cell => cell.ColorGroup!, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.All(cell => string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal)));
    }

    private static double ResolveAutoPurchaseThreshold(
        BotPersonality personality,
        BotDifficulty difficulty,
        bool directBankBuy,
        bool conservative)
    {
        var baseThreshold = (personality, directBankBuy, conservative) switch
        {
            (BotPersonality.Collector, true, false) => 0.72d,
            (BotPersonality.Collector, true, true) => 1.08d,
            (BotPersonality.Collector, false, false) => 1.12d,
            (BotPersonality.Collector, false, true) => 1.46d,
            (BotPersonality.Builder, true, false) => 0.92d,
            (BotPersonality.Builder, true, true) => 1.22d,
            (BotPersonality.Builder, false, false) => 1.28d,
            (BotPersonality.Builder, false, true) => 1.62d,
            (BotPersonality.Banker, true, false) => 1.45d,
            (BotPersonality.Banker, true, true) => 1.9d,
            (BotPersonality.Banker, false, false) => 1.65d,
            (BotPersonality.Banker, false, true) => 2.1d,
            _ => conservative ? 1.25d : 0.95d
        };

        return difficulty switch
        {
            BotDifficulty.Aggressive => Math.Max(0.45d, baseThreshold - 0.16d),
            _ => baseThreshold
        };
    }

    private static int ResolveAutoUpgradeMinimumGain(BotPersonality personality, BotDifficulty difficulty)
    {
        var baseGain = personality switch
        {
            BotPersonality.Builder => 40,
            BotPersonality.Collector => 75,
            BotPersonality.Banker => 150,
            _ => 70
        };

        return difficulty switch
        {
            BotDifficulty.Aggressive => Math.Max(25, baseGain - 15),
            _ => baseGain
        };
    }

    private static int ResolveAutoUpgradeReserveFloor(int reserve, BotPersonality personality, BotDifficulty difficulty)
    {
        var floor = personality switch
        {
            BotPersonality.Builder => Math.Max(20, reserve - 170),
            BotPersonality.Collector => Math.Max(30, reserve - 135),
            BotPersonality.Banker => Math.Max(70, reserve - 40),
            _ => Math.Max(40, reserve - 120)
        };

        return difficulty switch
        {
            BotDifficulty.Aggressive => Math.Max(20, floor - 30),
            _ => floor
        };
    }

    private static int ResolveAutoUnmortgageReserveBuffer(BotPersonality personality, BotDifficulty difficulty)
    {
        var buffer = personality switch
        {
            BotPersonality.Builder => 40,
            BotPersonality.Collector => 70,
            BotPersonality.Banker => 180,
            _ => 80
        };

        return difficulty switch
        {
            BotDifficulty.Aggressive => Math.Max(20, buffer - 25),
            _ => buffer
        };
    }

    private static double ResolveAutoPropertyPriority(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        MonopolyCellState cell)
    {
        var price = string.IsNullOrWhiteSpace(state.PendingPurchaseOwnerPlayerId)
            ? ResolveAutoCityPrice(cell, state.CityPriceGrowthRounds)
            : ResolveAutoTakeoverPrice(cell, state.CityPriceGrowthRounds);
        if (price <= 0)
        {
            return 0;
        }

        var alreadyOwnedByPlayer = string.Equals(cell.OwnerPlayerId, player.PlayerId, StringComparison.Ordinal);
        var incomeScore = cell.Type switch
        {
            MonopolyCellType.Property => Math.Max(1, cell.Rent) *
                (WouldAutoCompleteColorSet(state, player.PlayerId, cell) ? 6d : 2.2d),
            MonopolyCellType.Railroad => 35d * Math.Pow(
                OwnedRailroadCount(state, player.PlayerId) + (alreadyOwnedByPlayer ? 0 : 1), 2),
            MonopolyCellType.Utility => OwnedUtilityCount(state, player.PlayerId) + (alreadyOwnedByPlayer ? 0 : 1) >= 2
                ? 90d
                : 36d,
            _ => cell.Rent
        };

        incomeScore += cell.HouseCount * 120d;
        if (cell.HasHotel)
        {
            incomeScore += 360d;
        }
        if (cell.HasLandmark)
        {
            incomeScore += 700d;
        }

        var reserve = ResolveAutoCashReserve(room, state, player);
        var affordabilityBonus = price <= Math.Max(180, player.Cash - reserve) ? 1.4d : 0d;
        return (incomeScore / Math.Max(60d, price)) + affordabilityBonus;
    }

    private static double ResolveAutoStrategicExitScore(
        GameRoom room,
        MonopolyRoomState state,
        PlayerState player,
        MonopolyCellState cell)
    {
        var baseScore = ResolveAutoPropertyPriority(room, state, player, cell);
        if (cell.IsMortgaged)
        {
            baseScore -= 0.8d;
        }
        return baseScore;
    }

    private static bool WouldAutoCompleteColorSet(
        MonopolyRoomState state,
        string playerId,
        MonopolyCellState cell)
    {
        if (cell.Type != MonopolyCellType.Property || string.IsNullOrWhiteSpace(cell.ColorGroup))
        {
            return false;
        }

        return state.Cells
            .Where(candidate =>
                candidate.Type == MonopolyCellType.Property &&
                string.Equals(candidate.ColorGroup, cell.ColorGroup, StringComparison.OrdinalIgnoreCase))
            .All(candidate =>
                candidate.Cell == cell.Cell ||
                string.Equals(candidate.OwnerPlayerId, playerId, StringComparison.Ordinal));
    }

    private static int OwnedRailroadCount(MonopolyRoomState state, string playerId)
    {
        return state.Cells.Count(cell =>
            cell.Type == MonopolyCellType.Railroad &&
            string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal));
    }

    private static int OwnedUtilityCount(MonopolyRoomState state, string playerId)
    {
        return state.Cells.Count(cell =>
            cell.Type == MonopolyCellType.Utility &&
            string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal));
    }

    private static int ResolveAutoCityPrice(MonopolyCellState cell, int growthRounds)
    {
        if (cell.Price <= 0)
        {
            return 0;
        }

        var growthMultiplier = 1d + (Math.Max(0, growthRounds) * MonopolyDefinitions.CityPriceGrowthPerCompletedRound);
        return Math.Max(1, (int)Math.Ceiling(cell.Price * growthMultiplier));
    }

    private static int ResolveAutoMortgageValue(MonopolyCellState cell, int growthRounds)
    {
        return Math.Max(0, (int)Math.Floor(ResolveAutoCityPrice(cell, growthRounds) / 2d));
    }

    private static int ResolveAutoUnmortgageCost(MonopolyCellState cell, int growthRounds)
    {
        return (int)Math.Ceiling(ResolveAutoMortgageValue(cell, growthRounds) * 1.1d);
    }

    private static int ResolveAutoLandmarkCost(MonopolyCellState cell)
    {
        return Math.Max(0, cell.HouseCost * MonopolyDefinitions.LandmarkCostMultiplier);
    }

    private static int ResolveAutoTakeoverPrice(MonopolyCellState cell, int growthRounds)
    {
        var baseValue = cell.IsMortgaged
            ? ResolveAutoMortgageValue(cell, growthRounds)
            : ResolveAutoCityPrice(cell, growthRounds);
        var buildingValue = cell.HasLandmark
            ? cell.HouseCost * 7
            : cell.HasHotel
                ? cell.HouseCost * 5
                : Math.Max(0, cell.HouseCount) * cell.HouseCost;
        return Math.Max(1, baseValue + buildingValue);
    }

    private static int ResolveAutoBankLiquidationValue(MonopolyCellState cell, int growthRounds)
    {
        var baseValue = cell.IsMortgaged
            ? ResolveAutoMortgageValue(cell, growthRounds)
            : Math.Max(1, (int)Math.Floor(ResolveAutoCityPrice(cell, growthRounds) * MonopolyDefinitions.BankLiquidationBaseRatio));
        var buildingRefund = cell.HasLandmark
            ? cell.HouseCost * 4
            : cell.HasHotel
                ? (int)Math.Ceiling(cell.HouseCost * 2.5d)
                : (int)Math.Ceiling(Math.Max(0, cell.HouseCount) * cell.HouseCost * 0.5d);
        return Math.Max(1, baseValue + buildingRefund);
    }

    private static int ResolveAutoBuildingLiquidationValue(MonopolyCellState cell)
    {
        if (cell.HasLandmark)
        {
            return Math.Max(1, ResolveAutoLandmarkCost(cell) / 2);
        }

        if (cell.HasHotel || cell.HouseCount > 0)
        {
            return Math.Max(1, cell.HouseCost / 2);
        }

        return 0;
    }

    private static int ResolveAutoNextUpgradeCost(MonopolyCellState cell)
    {
        return cell.HasHotel
            ? ResolveAutoLandmarkCost(cell)
            : Math.Max(0, cell.HouseCost);
    }

    private static int ResolveAutoUpgradeGain(
        GameRoom room,
        MonopolyRoomState state,
        string ownerPlayerId,
        MonopolyCellState cell)
    {
        var currentRent = ResolveAutoScaledRent(room, state, ownerPlayerId, cell);
        var nextRent = ResolveAutoNextScaledRent(room, state, ownerPlayerId, cell);
        return Math.Max(0, nextRent - currentRent);
    }

    private static int ResolveAutoScaledRent(
        GameRoom room,
        MonopolyRoomState state,
        string ownerPlayerId,
        MonopolyCellState cell)
    {
        if (cell.IsMortgaged)
        {
            return 0;
        }

        var baseRent = cell.Type switch
        {
            MonopolyCellType.Property => ResolveAutoPropertyRent(state, ownerPlayerId, cell, cell.HouseCount, cell.HasHotel, cell.HasLandmark),
            MonopolyCellType.Railroad => ResolveAutoRailroadRent(state, ownerPlayerId),
            MonopolyCellType.Utility => ResolveAutoUtilityRent(state, ownerPlayerId),
            _ => Math.Max(0, cell.Rent)
        };

        return ApplyAutoRentScaling(baseRent, room.CompletedRounds);
    }

    private static int ResolveAutoNextScaledRent(
        GameRoom room,
        MonopolyRoomState state,
        string ownerPlayerId,
        MonopolyCellState cell)
    {
        if (cell.HasLandmark)
        {
            return ResolveAutoScaledRent(room, state, ownerPlayerId, cell);
        }

        if (cell.HasHotel)
        {
            var baseRent = ResolveAutoPropertyRent(state, ownerPlayerId, cell, 0, true, true);
            return ApplyAutoRentScaling(baseRent, room.CompletedRounds);
        }

        if (cell.HouseCount < 4)
        {
            var baseRent = ResolveAutoPropertyRent(state, ownerPlayerId, cell, cell.HouseCount + 1, false, false);
            return ApplyAutoRentScaling(baseRent, room.CompletedRounds);
        }

        var hotelRent = ResolveAutoPropertyRent(state, ownerPlayerId, cell, 0, true, false);
        return ApplyAutoRentScaling(hotelRent, room.CompletedRounds);
    }

    private static int ResolveAutoPropertyRent(
        MonopolyRoomState state,
        string ownerPlayerId,
        MonopolyCellState cell,
        int houseCount,
        bool hasHotel,
        bool hasLandmark)
    {
        var baseRent = Math.Max(0, cell.Rent);
        if (baseRent <= 0)
        {
            return 0;
        }

        if (hasLandmark)
        {
            return baseRent * MonopolyDefinitions.LandmarkRentMultiplier;
        }

        if (hasHotel)
        {
            return baseRent * 125;
        }

        if (houseCount > 0)
        {
            return houseCount switch
            {
                1 => baseRent * 5,
                2 => baseRent * 15,
                3 => baseRent * 45,
                _ => baseRent * 80
            };
        }

        return WouldAutoCompleteColorSet(state, ownerPlayerId, cell)
            ? baseRent * 2
            : baseRent;
    }

    private static int ResolveAutoRailroadRent(MonopolyRoomState state, string ownerPlayerId)
    {
        var count = state.Cells.Count(cell =>
            cell.Type == MonopolyCellType.Railroad &&
            !cell.IsMortgaged &&
            string.Equals(cell.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal));
        return count <= 0 ? 0 : 40 * (int)Math.Pow(2, count - 1);
    }

    private static int ResolveAutoUtilityRent(MonopolyRoomState state, string ownerPlayerId)
    {
        var count = state.Cells.Count(cell =>
            cell.Type == MonopolyCellType.Utility &&
            !cell.IsMortgaged &&
            string.Equals(cell.OwnerPlayerId, ownerPlayerId, StringComparison.Ordinal));
        if (count <= 0)
        {
            return 0;
        }

        var diceTotal = Math.Max(2, state.LastDiceOne + state.LastDiceTwo);
        return (count >= 2 ? 14 : 6) * diceTotal;
    }

    private static int ApplyAutoRentScaling(int amount, int completedRounds)
    {
        if (amount <= 0)
        {
            return 0;
        }

        var accelerated = amount * MonopolyDefinitions.RentAccelerationMultiplier;
        var growthMultiplier = 1d + (Math.Max(0, completedRounds) * MonopolyDefinitions.RentGrowthPerCompletedRound);
        return Math.Max(1, (int)Math.Ceiling(accelerated * growthMultiplier));
    }

    private static bool CanAutoSellEvenly(
        MonopolyRoomState state,
        string playerId,
        MonopolyCellState target)
    {
        if (string.IsNullOrWhiteSpace(target.ColorGroup))
        {
            return true;
        }

        var groupCells = state.Cells
            .Where(cell =>
                cell.Type == MonopolyCellType.Property &&
                string.Equals(cell.ColorGroup, target.ColorGroup, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cell.OwnerPlayerId, playerId, StringComparison.Ordinal))
            .ToArray();
        if (groupCells.Length == 0)
        {
            return false;
        }

        var targetLevel = ResolveAutoBuildingLevel(target);
        var maxLevel = groupCells.Max(ResolveAutoBuildingLevel);
        return targetLevel == maxLevel;
    }

    private static int ResolveAutoBuildingLevel(MonopolyCellState cell)
    {
        if (cell.HasLandmark)
        {
            return 6;
        }

        if (cell.HasHotel)
        {
            return 5;
        }

        return Math.Max(0, cell.HouseCount);
    }
}
