(() => {
  const root = window.SNL;
  const { state } = root;
  const MONOPOLY_KEY = String(root.GAME_KEYS?.MONOPOLY ?? "monopoly")
    .trim()
    .toLowerCase();

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

    return resolveCells(room).find((cell) => resolveCellNo(cell) === target) ?? null;
  }

  function resolveCellNo(cell) {
    return Number.parseInt(String(cell?.cell ?? cell?.Cell ?? 0), 10) || 0;
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
      : root.MONOPOLY_PHASE?.AWAIT_ROLL ?? 1;
  }

  function canRollNow() {
    if (!state.room || !state.roomCode || state.animating || !isMonopolyRoom()) {
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
    const amount = Number.parseInt(String(value ?? 0), 10) || 0;
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

  root.monopolyHelpers = {
    isMonopolyRoom,
    getMonopolyState,
    resolveCells,
    findCell,
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
