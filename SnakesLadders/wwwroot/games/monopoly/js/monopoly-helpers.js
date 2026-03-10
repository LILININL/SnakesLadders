(() => {
  const root = window.SNL;
  const { state } = root;
  const MONOPOLY_KEY = String(root.GAME_KEYS?.MONOPOLY ?? "monopoly")
    .trim()
    .toLowerCase();

  const CELL_TYPE = {
    PROPERTY: 1,
    RAILROAD: 2,
    UTILITY: 3,
  };

  const DEFAULT_BOARD_CELL_COUNT = 40;
  const LANDMARK_RENT_MULTIPLIER = 170;
  const RENT_ACCELERATION_MULTIPLIER = 1.6;
  const RENT_GROWTH_PER_COMPLETED_ROUND = 0.1;
  const CITY_PRICE_GROWTH_PER_COMPLETED_ROUND = 0.07;
  const NEIGHBORHOOD_RADIUS = 2;
  const NEIGHBORHOOD_PRIMARY_BONUS = 0.55;
  const NEIGHBORHOOD_SECONDARY_BONUS = 0.32;
  const economyTracker = {
    roomCode: "",
    turnCounter: 0,
    completedRounds: 0,
    cityPriceGrowthRounds: 0,
  };

  function isMonopolyRoom(room = state.room) {
    const gameKey = String(room?.gameKey ?? "")
      .trim()
      .toLowerCase();
    return gameKey === MONOPOLY_KEY;
  }

  function getMonopolyState(room = state.room) {
    if (!isMonopolyRoom(room)) {
      return null;
    }

    return room?.monopolyState ?? null;
  }

  function resolveCells(room = state.room) {
    const board = room?.board;
    if (!board) {
      return [];
    }
    if (Array.isArray(board.monopolyCells)) {
      return board.monopolyCells;
    }
    if (Array.isArray(board.MonopolyCells)) {
      return board.MonopolyCells;
    }
    return [];
  }

  function findCell(cellId, room = state.room) {
    const target = Number.parseInt(String(cellId ?? ""), 10);
    if (!Number.isFinite(target) || target <= 0) {
      return null;
    }

    return (
      resolveCells(room).find((cell) => resolveCellNo(cell) === target) ?? null
    );
  }

  function resolveCellNo(cell) {
    return resolveNumber(cell?.cell ?? cell?.Cell ?? 0);
  }

  function resolveOwnerId(cell) {
    return String(cell?.ownerPlayerId ?? cell?.OwnerPlayerId ?? "").trim();
  }

  function resolveType(cell) {
    return resolveNumber(cell?.type ?? cell?.Type);
  }

  function resolveColorGroup(cell) {
    return String(cell?.colorGroup ?? cell?.ColorGroup ?? "")
      .trim()
      .toLowerCase();
  }

  function resolveHouseCount(cell) {
    return resolveNumber(cell?.houseCount ?? cell?.HouseCount);
  }

  function resolveRent(cell) {
    return resolveNumber(cell?.rent ?? cell?.Rent);
  }

  function resolvePrice(cell) {
    return resolveNumber(cell?.price ?? cell?.Price);
  }

  function resolveLastDiceTotal(room = state.room) {
    const monopoly = getMonopolyState(room);
    return Math.max(
      2,
      resolveNumber(monopoly?.lastDiceOne ?? monopoly?.LastDiceOne) +
        resolveNumber(monopoly?.lastDiceTwo ?? monopoly?.LastDiceTwo),
    );
  }

  function resolveCompletedRounds(room = state.room) {
    return resolveEconomyState(room).completedRounds;
  }

  function resolveCityPriceGrowthRounds(room = state.room) {
    return resolveEconomyState(room).cityPriceGrowthRounds;
  }

  function resolveEconomyState(room = state.room) {
    const roomCode = String(room?.roomCode ?? room?.RoomCode ?? "")
      .trim()
      .toUpperCase();
    const turnCounter = resolveNumber(room?.turnCounter ?? room?.TurnCounter);
    const rawCompletedRounds = resolveNumber(
      room?.completedRounds ?? room?.CompletedRounds,
    );
    const monopoly = getMonopolyState(room);
    const rawCityPriceGrowthRounds = resolveNumber(
      monopoly?.cityPriceGrowthRounds ?? monopoly?.CityPriceGrowthRounds,
    );
    const started =
      resolveNumber(room?.status ?? room?.Status) === root.GAME_STATUS.STARTED;

    if (!roomCode) {
      economyTracker.roomCode = "";
      economyTracker.turnCounter = 0;
      economyTracker.completedRounds = 0;
      economyTracker.cityPriceGrowthRounds = 0;
      return {
        completedRounds: rawCompletedRounds,
        cityPriceGrowthRounds: rawCityPriceGrowthRounds,
      };
    }

    const shouldReset =
      economyTracker.roomCode !== roomCode ||
      !started ||
      turnCounter === 0 ||
      (turnCounter <= 1 &&
        rawCompletedRounds === 0 &&
        rawCityPriceGrowthRounds === 0);

    if (shouldReset) {
      economyTracker.roomCode = roomCode;
      economyTracker.turnCounter = turnCounter;
      economyTracker.completedRounds = rawCompletedRounds;
      economyTracker.cityPriceGrowthRounds = rawCityPriceGrowthRounds;
    } else {
      economyTracker.turnCounter = Math.max(
        economyTracker.turnCounter,
        turnCounter,
      );
      economyTracker.completedRounds = Math.max(
        economyTracker.completedRounds,
        rawCompletedRounds,
      );
      economyTracker.cityPriceGrowthRounds = Math.max(
        economyTracker.cityPriceGrowthRounds,
        rawCityPriceGrowthRounds,
      );
    }

    return {
      completedRounds: economyTracker.completedRounds,
      cityPriceGrowthRounds: economyTracker.cityPriceGrowthRounds,
    };
  }

  function tollGrowthMultiplier(room = state.room) {
    return 1 + resolveCompletedRounds(room) * RENT_GROWTH_PER_COMPLETED_ROUND;
  }

  function tollGrowthPercent(room = state.room) {
    return Math.max(
      0,
      Math.round(
        resolveCompletedRounds(room) * RENT_GROWTH_PER_COMPLETED_ROUND * 100,
      ),
    );
  }

  function cityPriceGrowthMultiplier(room = state.room) {
    return (
      1 +
      resolveCityPriceGrowthRounds(room) * CITY_PRICE_GROWTH_PER_COMPLETED_ROUND
    );
  }

  function cityPriceGrowthPercent(room = state.room) {
    return Math.max(
      0,
      Math.round(
        resolveCityPriceGrowthRounds(room) *
          CITY_PRICE_GROWTH_PER_COMPLETED_ROUND *
          100,
      ),
    );
  }

  function scaleCityPrice(value, room = state.room) {
    const amount = resolveNumber(value);
    if (amount <= 0) {
      return 0;
    }

    return Math.max(1, Math.ceil(amount * cityPriceGrowthMultiplier(room)));
  }

  function scaleCityPriceForCell(cell, room = state.room) {
    return scaleCityPrice(resolvePrice(cell), room);
  }

  function applyRentScaling(amount, room = state.room) {
    const base = resolveNumber(amount);
    if (base <= 0) {
      return 0;
    }

    return Math.max(
      1,
      Math.ceil(
        base * RENT_ACCELERATION_MULTIPLIER * tollGrowthMultiplier(room),
      ),
    );
  }

  function previewCellToll(cell, room = state.room) {
    const type = resolveType(cell);
    if (
      type !== CELL_TYPE.PROPERTY &&
      type !== CELL_TYPE.RAILROAD &&
      type !== CELL_TYPE.UTILITY
    ) {
      return 0;
    }

    if (Boolean(cell?.isMortgaged ?? cell?.IsMortgaged)) {
      return 0;
    }

    const ownerId = resolveOwnerId(cell);
    const baseAmount =
      type === CELL_TYPE.PROPERTY
        ? previewPropertyRent(cell, room, ownerId)
        : type === CELL_TYPE.RAILROAD
          ? previewRailroadRent(room, ownerId)
          : previewUtilityRent(room, ownerId);

    if (baseAmount <= 0) {
      return 0;
    }

    const scaledBase = applyRentScaling(baseAmount, room);
    const surcharge = ownerId
      ? previewNeighborhoodSurcharge(room, cell, ownerId, scaledBase)
      : 0;
    return Math.max(0, scaledBase + surcharge);
  }

  function previewPropertyRent(cell, room, ownerId) {
    const baseRent = resolveRent(cell);
    if (baseRent <= 0) {
      return 0;
    }

    if (Boolean(cell?.hasLandmark ?? cell?.HasLandmark)) {
      return baseRent * LANDMARK_RENT_MULTIPLIER;
    }

    if (Boolean(cell?.hasHotel ?? cell?.HasHotel)) {
      return baseRent * 125;
    }

    const houses = resolveHouseCount(cell);
    if (houses > 0) {
      switch (Math.min(4, houses)) {
        case 1:
          return baseRent * 5;
        case 2:
          return baseRent * 15;
        case 3:
          return baseRent * 45;
        default:
          return baseRent * 80;
      }
    }

    return ownerId && ownsFullColorSet(room, ownerId, resolveColorGroup(cell))
      ? baseRent * 2
      : baseRent;
  }

  function previewRailroadRent(room, ownerId) {
    const ownedCount = ownerId
      ? resolveCells(room).filter(
          (candidate) =>
            resolveType(candidate) === CELL_TYPE.RAILROAD &&
            !Boolean(candidate?.isMortgaged ?? candidate?.IsMortgaged) &&
            resolveOwnerId(candidate) === ownerId,
        ).length
      : 1;

    const count = Math.max(1, ownedCount);
    return 40 * Math.pow(2, count - 1);
  }

  function previewUtilityRent(room, ownerId) {
    const ownedCount = ownerId
      ? resolveCells(room).filter(
          (candidate) =>
            resolveType(candidate) === CELL_TYPE.UTILITY &&
            !Boolean(candidate?.isMortgaged ?? candidate?.IsMortgaged) &&
            resolveOwnerId(candidate) === ownerId,
        ).length
      : 1;

    const diceTotal = resolveLastDiceTotal(room);
    return (Math.max(1, ownedCount) >= 2 ? 14 : 6) * diceTotal;
  }

  function previewNeighborhoodSurcharge(room, cell, ownerId, baseAmount) {
    if (!ownerId || baseAmount <= 0) {
      return 0;
    }

    let bonus = 0;
    resolveCells(room).forEach((candidate) => {
      if (resolveCellNo(candidate) === resolveCellNo(cell)) {
        return;
      }
      if (!resolveOwnerId(candidate) || resolveOwnerId(candidate) !== ownerId) {
        return;
      }

      const distance = boardDistance(
        resolveCellNo(cell),
        resolveCellNo(candidate),
        DEFAULT_BOARD_CELL_COUNT,
      );
      if (distance <= 0 || distance > NEIGHBORHOOD_RADIUS) {
        return;
      }

      bonus +=
        resolveNeighborhoodWeight(candidate) *
        (distance === 1
          ? NEIGHBORHOOD_PRIMARY_BONUS
          : NEIGHBORHOOD_SECONDARY_BONUS);
    });

    return Math.max(0, Math.ceil(baseAmount * bonus));
  }

  function resolveNeighborhoodWeight(cell) {
    if (Boolean(cell?.hasLandmark ?? cell?.HasLandmark)) {
      return 2.4;
    }

    if (Boolean(cell?.hasHotel ?? cell?.HasHotel)) {
      return 1.6;
    }

    const houses = resolveHouseCount(cell);
    if (houses > 0) {
      return 0.75 + houses * 0.22;
    }

    switch (resolveType(cell)) {
      case CELL_TYPE.RAILROAD:
        return 1.1;
      case CELL_TYPE.UTILITY:
        return 0.9;
      default:
        return 0.7;
    }
  }

  function ownsFullColorSet(room, ownerId, colorGroup) {
    if (!ownerId || !colorGroup) {
      return false;
    }

    const allInGroup = resolveCells(room).filter(
      (candidate) =>
        resolveType(candidate) === CELL_TYPE.PROPERTY &&
        resolveColorGroup(candidate) === colorGroup,
    );

    return (
      allInGroup.length > 0 &&
      allInGroup.every((candidate) => resolveOwnerId(candidate) === ownerId)
    );
  }

  function boardDistance(fromCell, toCell, boardSize) {
    const normalizedBoard = Math.max(1, resolveNumber(boardSize));
    const direct = Math.abs(resolveNumber(fromCell) - resolveNumber(toCell));
    return Math.min(direct, normalizedBoard - direct);
  }

  function activePlayerId(room = state.room) {
    const monopoly = getMonopolyState(room);
    return monopoly?.activePlayerId ?? "";
  }

  function pendingDecisionPlayerId(room = state.room) {
    const monopoly = getMonopolyState(room);
    return monopoly?.pendingDecisionPlayerId ?? "";
  }

  function phase(room = state.room) {
    const monopoly = getMonopolyState(room);
    const parsed = Number.parseInt(String(monopoly?.phase ?? ""), 10);
    return Number.isFinite(parsed)
      ? parsed
      : (root.MONOPOLY_PHASE?.AWAIT_ROLL ?? 1);
  }

  function canRollNow() {
    if (
      !state.room ||
      !state.roomCode ||
      state.animating ||
      !isMonopolyRoom()
    ) {
      return false;
    }
    if ((state.room?.status ?? -1) !== root.GAME_STATUS.STARTED) {
      return false;
    }

    return (
      activePlayerId() === state.playerId &&
      phase() === root.MONOPOLY_PHASE.AWAIT_ROLL
    );
  }

  function isMyDecisionTurn() {
    const pending = pendingDecisionPlayerId();
    if (!pending) {
      return activePlayerId() === state.playerId;
    }
    return pending === state.playerId;
  }

  async function submitAction(actionType, monopolyPayload = {}) {
    if (!state.roomCode) {
      return false;
    }

    return await root.realtime.invokeHub("SubmitGameAction", {
      roomCode: state.roomCode,
      actionType,
      monopoly: monopolyPayload,
    });
  }

  function parseCellList(input) {
    const value = String(input ?? "").trim();
    if (!value) {
      return [];
    }

    return value
      .split(",")
      .map((token) => Number.parseInt(token.trim(), 10))
      .filter((cellNo) => Number.isFinite(cellNo) && cellNo > 0)
      .filter((cellNo, index, arr) => arr.indexOf(cellNo) === index)
      .sort((a, b) => a - b);
  }

  function money(value) {
    const amount = resolveNumber(value);
    const abs = Math.abs(amount).toLocaleString("th-TH");
    return amount < 0 ? `-฿${abs}` : `฿${abs}`;
  }

  function playerName(playerId, room = state.room) {
    if (!playerId || !room?.players) {
      return "-";
    }

    const player = room.players.find((item) => item.playerId === playerId);
    return player?.displayName ?? playerId;
  }

  function phaseLabel(currentPhase) {
    switch (Number.parseInt(String(currentPhase ?? ""), 10)) {
      case root.MONOPOLY_PHASE.AWAIT_JAIL_DECISION:
        return "ตัดสินใจในคุก";
      case root.MONOPOLY_PHASE.AWAIT_ROLL:
        return "รอทอยเต๋า";
      case root.MONOPOLY_PHASE.RESOLVING:
        return "กำลังคำนวณผล";
      case root.MONOPOLY_PHASE.AWAIT_PURCHASE_DECISION:
        return "ตัดสินใจซื้อทรัพย์สิน";
      case root.MONOPOLY_PHASE.AUCTION_IN_PROGRESS:
        return "สถานะเก่า (ไม่ใช้งานแล้ว)";
      case root.MONOPOLY_PHASE.AWAIT_TRADE_RESPONSE:
        return "สถานะเก่า (ไม่ใช้งานแล้ว)";
      case root.MONOPOLY_PHASE.AWAIT_MANAGE:
        return "จัดการทรัพย์สิน";
      case root.MONOPOLY_PHASE.AWAIT_END_TURN:
        return "รอจบเทิร์น";
      case root.MONOPOLY_PHASE.FINISHED:
        return "เกมจบแล้ว";
      default:
        return "-";
    }
  }

  function resolveNumber(value) {
    return Number.parseInt(String(value ?? 0), 10) || 0;
  }

  root.monopolyHelpers = {
    isMonopolyRoom,
    getMonopolyState,
    resolveCells,
    findCell,
    resolveCellNo,
    resolveCompletedRounds,
    resolveCityPriceGrowthRounds,
    tollGrowthMultiplier,
    tollGrowthPercent,
    cityPriceGrowthMultiplier,
    cityPriceGrowthPercent,
    scaleCityPrice,
    scaleCityPriceForCell,
    previewCellToll,
    activePlayerId,
    pendingDecisionPlayerId,
    phase,
    phaseLabel,
    isMyDecisionTurn,
    canRollNow,
    submitAction,
    parseCellList,
    money,
    playerName,
  };
})();
