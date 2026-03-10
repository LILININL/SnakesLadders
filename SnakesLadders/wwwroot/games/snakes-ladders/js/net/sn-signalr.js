(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { normalizeName } = root.utils;
  let lastActionFxRoom = null;
  let lastActionFxRevision = 0;

  async function setupConnection() {
    state.connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/game")
      .withAutomaticReconnect()
      .build();

    state.connection.onreconnecting(() => {
      root.renderLobby.setConnectionStatus("กำลังเชื่อมต่อใหม่...", "warning");
    });

    state.connection.onreconnected(async () => {
      root.renderLobby.setConnectionStatus("เชื่อมต่อแล้ว", "online");
      await publishLobbyName();
      if (!state.roomCode) {
        await root.api.refreshLobbyOnline();
      }

      if (state.roomCode && state.sessionId) {
        await invokeHub(
          "ResumeRoom",
          {
            roomCode: state.roomCode,
            sessionId: state.sessionId,
            playerName: state.profileName,
            avatarId: state.profileAvatarId,
          },
          true,
        );
      }
    });

    state.connection.onclose(() => {
      root.renderLobby.setConnectionStatus("หลุดการเชื่อมต่อ", "error");
    });

    state.connection.on("Error", (message) => {
      root.feedback.logEvent(`เซิร์ฟเวอร์: ${message}`, true);
    });

    state.connection.on("RoomCreated", (payload) =>
      bindRoomPayload(payload, "สร้างห้องสำเร็จ"),
    );
    state.connection.on("RoomJoined", (payload) =>
      bindRoomPayload(payload, "เข้าห้องสำเร็จ"),
    );
    state.connection.on("RoomResumed", (payload) =>
      bindRoomPayload(payload, "กลับเข้าห้องเดิมแล้ว"),
    );

    state.connection.on("RoomUpdated", (room) => {
      if (root.turnAnimation?.isBusy?.()) {
        if (room?.status === root.GAME_STATUS.STARTED) {
          state.pendingTurnChangedPlayerId = room.currentTurnPlayerId ?? "";
        }
        return;
      }

      if (!root.viewState.acceptRoomSnapshot(room)) {
        return;
      }

      syncActionFxBaseline(room);
      root.session.syncProfileAvatarFromRoom(room, state.playerId);
      flushPendingTurnTrigger(room);
      root.boardFocus?.onRoomBound?.(false);
      root.feedback.renderAll();
    });

    state.connection.on("FullAutoUpdated", (room) => {
      if (!room) {
        return;
      }

      if (!root.viewState.acceptRoomSnapshot(room)) {
        return;
      }

      syncActionFxBaseline(room);
      root.feedback.renderAll();
    });

    state.connection.on("GameStarted", (room) => {
      root.viewState.acceptRoomSnapshot(room, { force: true });
      syncActionFxBaseline(room, { force: true });
      root.session.syncProfileAvatarFromRoom(room, state.playerId);
      seedTurnTriggerCounter(room);
      root.boardFocus?.onRoomBound?.(true);
      root.feedback.logEvent("เกมเริ่มแล้ว ลุยได้เลย");
      root.feedback.renderAll();
      root.boardFx?.showTurnStart?.(
        room,
        room.currentTurnPlayerId,
        "ผู้เริ่มเกม",
      );
    });

    state.connection.on("TurnChanged", (playerId) => {
      state.pendingTurnChangedPlayerId = playerId ?? "";
      if (root.turnAnimation?.isBusy?.()) {
        return;
      }

      flushPendingTurnTrigger(state.room);
      root.feedback.renderAll();
    });

    state.connection.on("GameActionApplied", async (payload) => {
      await handleGameActionApplied(payload, false);
    });

    state.connection.on("DiceRolled", async (payload) => {
      await handleGameActionApplied(payload, true);
    });

    state.connection.on("GameFinished", (payload) => {
      const winnerId = payload.turn.winnerPlayerId ?? "";
      const winnerName =
        payload.room?.players?.find((x) => x.playerId === winnerId)
          ?.displayName ??
        winnerId ??
        "-";
      const topThree =
        payload.room?.gameResult?.placements?.slice(0, 3)
          ?.map((entry) => `#${entry.rank} ${entry.displayName}`)
          ?.join(" | ") ?? "";
      root.feedback.logEvent(
        topThree
          ? `จบเกมแล้ว ผู้ชนะคือ ${winnerName || "-"} | ${topThree}`
          : `จบเกมแล้ว ผู้ชนะคือ ${winnerName || "-"}`,
      );
      if (root.turnAnimation?.isBusy?.()) {
        root.viewState.stageDeferredRoom(payload.room);
      } else {
        state.lastTurn = payload.turn;
        root.viewState.acceptRoomSnapshot(payload.room);
        syncActionFxBaseline(payload.room, { force: true });
        seedTurnTriggerCounter(payload.room);
        root.feedback.renderAll();
      }
    });

    state.connection.on("ChatReceived", (message) => {
      if (!state.roomCode || message.roomCode !== state.roomCode) {
        return;
      }
      root.renderChat.addChatMessage(message);
    });

    await state.connection.start();
    root.renderLobby.setConnectionStatus("เชื่อมต่อแล้ว", "online");
    await publishLobbyName();
  }

  async function invokeHub(methodName, payload, suppressError = false) {
    try {
      await state.connection.invoke(methodName, payload);
      return true;
    } catch (error) {
      if (!suppressError) {
        root.feedback.logEvent(
          `คำสั่งไม่สำเร็จ: ${error.message ?? String(error)}`,
          true,
        );
      }
      return false;
    }
  }

  async function publishLobbyName() {
    const name = normalizeName(state.profileName);
    if (
      !name ||
      !state.connection ||
      state.connection.state !== signalR.HubConnectionState.Connected
    ) {
      return;
    }

    await invokeHub("SetLobbyName", name, true);
  }

  function bindRoomPayload(payload, label) {
    state.roomCode = payload.roomCode;
    state.playerId = payload.playerId;
    state.sessionId = payload.sessionId;
    root.viewState.acceptRoomSnapshot(payload.room, { force: true });
    syncActionFxBaseline(payload.room, { force: true });
    state.lastTurn = null;
    state.chatMessages = [];
    state.chatPanelOpen = true;
    state.chatUnreadCount = 0;
    state.rollButtonHidden = false;
    state.animating = false;
    state.animPlayerId = "";
    state.animPlayerPosition = 1;
    state.animTurnPlayerId = "";
    state.animFrenzySnake = null;
    state.animTransitActive = false;
    state.animTransitPlayerId = "";
    root.viewState.clearDeferredRoom();
    state.pendingTurnChangedPlayerId = "";
    state.lastAnnouncedTurnCounter = -1;
    state.pendingBeaconTargetPlayerId = "";
    state.pageTransitioning = false;
    state.pageTransitionDirection = 0;
    state.rollFxPowerHint = 0;
    state.rollFxPowerHintAt = 0;
    seedTurnTriggerCounter(payload.room);
    root.session.syncProfileAvatarFromRoom(payload.room, payload.playerId);
    root.turnAnimation?.reset?.();
    root.boardFx?.reset?.();
    root.boardFocus?.onRoomBound?.(true);
    root.roomUi?.renderChatBadge?.();

    el.joinRoomCode.value = payload.roomCode;
    root.storage.saveRoomSession(
      payload.roomCode,
      payload.sessionId,
      payload.playerId,
    );
    root.feedback.logEvent(`${label}: ${payload.roomCode}`);

    root.feedback.renderAll();
  }

  function flushPendingTurnTrigger(roomOverride = null) {
    if (root.turnAnimation?.isBusy?.()) {
      return false;
    }

    const room = roomOverride ?? state.room;
    if (!room || room.status !== root.GAME_STATUS.STARTED) {
      state.pendingTurnChangedPlayerId = "";
      state.lastAnnouncedTurnCounter = -1;
      return false;
    }

    const turnCounter = resolveTurnCounter(room.turnCounter);
    if (turnCounter < 0 || turnCounter <= state.lastAnnouncedTurnCounter) {
      state.pendingTurnChangedPlayerId = "";
      return false;
    }

    const playerId =
      state.pendingTurnChangedPlayerId || room.currentTurnPlayerId || "";
    state.pendingTurnChangedPlayerId = "";
    state.lastAnnouncedTurnCounter = turnCounter;

    if (!playerId) {
      return false;
    }

    const player = room.players?.find((x) => x.playerId === playerId);
    root.boardFocus?.refreshPendingBeaconTarget?.();
    root.feedback.logEvent(
      `ตอนนี้เป็นตาของ ${player ? player.displayName : playerId}`,
    );
    root.boardFx?.showTurnStart?.(room, playerId, "ถึงเทิร์นของ");
    return true;
  }

  function seedTurnTriggerCounter(room) {
    if (!room || room.status !== root.GAME_STATUS.STARTED) {
      state.lastAnnouncedTurnCounter = -1;
      state.pendingTurnChangedPlayerId = "";
      return;
    }

    state.lastAnnouncedTurnCounter = resolveTurnCounter(room.turnCounter);
    state.pendingTurnChangedPlayerId = "";
  }

  function resolveTurnCounter(value) {
    const parsed = Number.parseInt(String(value ?? ""), 10);
    return Number.isFinite(parsed) ? parsed : -1;
  }

  function formatDiceEvent(turn, room) {
    const baseLine = root.feedback.formatTurnLine(turn);
    const comebackLine = formatComebackLine(turn);
    const frenzyLine = formatFrenzyLine(turn);
    const itemLine = formatItemLine(turn);
    const extraLines = [comebackLine, frenzyLine, itemLine]
      .filter(Boolean)
      .join(" | ");
    if (!turn?.autoRollReason) {
      return extraLines ? `${baseLine} | ${extraLines}` : baseLine;
    }

    const playerName =
      room?.players?.find((x) => x.playerId === turn.playerId)?.displayName ??
      state.room?.players?.find((x) => x.playerId === turn.playerId)
        ?.displayName ??
      turn.playerId;

    if (turn.autoRollReason === "Disconnected") {
      const line = `${playerName} ออฟไลน์ ระบบทอยให้อัตโนมัติ (${turn.diceValue})`;
      return extraLines ? `${line} | ${extraLines}` : line;
    }

    if (turn.autoRollReason === "TimerExpired") {
      const line = `${playerName} หมดเวลา ระบบทอยให้อัตโนมัติ (${turn.diceValue})`;
      return extraLines ? `${line} | ${extraLines}` : line;
    }

    return extraLines ? `${baseLine} | ${extraLines}` : baseLine;
  }

  async function handleGameActionApplied(payload, fromDiceEvent) {
    if (!payload?.turn || !payload?.room) {
      return;
    }

    const incomingRevision =
      root.viewState?.resolveRoomSnapshotRevision?.(payload.room) ?? 0;
    const latestRevision = Math.max(
      Number.parseInt(String(state.roomSnapshotRevision ?? 0), 10) || 0,
      Number.parseInt(String(state.deferredRoomSnapshotRevision ?? 0), 10) || 0,
      Number.parseInt(String(state.latestRoomSnapshotRevision ?? 0), 10) || 0,
    );
    if (incomingRevision > 0 && incomingRevision < latestRevision) {
      return;
    }

    const room = payload.room;
    const actionType =
      Number.parseInt(
        String(payload.turn.actionType ?? root.GAME_ACTION_TYPES.ROLL_DICE),
        10,
      ) || root.GAME_ACTION_TYPES.ROLL_DICE;
    const isRollAction = actionType === root.GAME_ACTION_TYPES.ROLL_DICE;
    const isMonopoly =
      root.monopolyHelpers?.isMonopolyRoom?.(room) ??
      String(room?.gameKey ?? "").trim().toLowerCase() ===
        String(root.GAME_KEYS?.MONOPOLY ?? "monopoly")
          .trim()
          .toLowerCase();

    if (!fromDiceEvent && !isMonopoly && isRollAction) {
      return;
    }

    if (isMonopoly) {
      const previousActionRoom = resolvePreviousActionFxRoom(room, incomingRevision);
      payload.turn.clientFx = buildMonopolyClientFx(
        payload.turn,
        previousActionRoom,
        room,
      );
      syncActionFxBaseline(room, { force: true, revision: incomingRevision });
    }

    root.feedback.logEvent(formatActionEvent(payload.turn, room));
    await root.turnAnimation.queue(payload);
  }

  function formatActionEvent(turn, room) {
    const actionType =
      Number.parseInt(
        String(turn?.actionType ?? root.GAME_ACTION_TYPES.ROLL_DICE),
        10,
      ) || root.GAME_ACTION_TYPES.ROLL_DICE;
    const isRollAction = actionType === root.GAME_ACTION_TYPES.ROLL_DICE;
    const isMonopoly = root.monopolyHelpers?.isMonopolyRoom?.(room);

    if (!isMonopoly && isRollAction) {
      return formatDiceEvent(turn, room);
    }

    const actionLogs = Array.isArray(turn?.actionLogs) ? turn.actionLogs : [];
    if (actionLogs.length > 0) {
      return selectActionHeadline(actionLogs, actionType);
    }

    const playerName =
      room?.players?.find((x) => x.playerId === turn?.playerId)?.displayName ??
      turn?.playerId ??
      "-";

    if (isRollAction) {
      const d1 = Number.parseInt(String(turn?.diceOne ?? 0), 10) || 0;
      const d2 = Number.parseInt(String(turn?.diceTwo ?? 0), 10) || 0;
      return `${playerName} ทอยเต๋า ${d1}+${d2} -> ${turn?.endPosition ?? "-"}`;
    }

    const summary = String(turn?.actionSummary ?? "").trim();
    if (summary) {
      return `${playerName}: ${summary}`;
    }

    return `${playerName}: ${actionTypeLabel(actionType)}`;
  }

  function selectActionHeadline(actionLogs, actionType) {
    const lines = actionLogs
      .map((line) => String(line ?? "").trim())
      .filter(Boolean);
    if (lines.length === 0) {
      return "มีการอัปเดตสถานะเกม";
    }

    if (actionType === root.GAME_ACTION_TYPES.ROLL_DICE) {
      const eventLine = lines.find(
        (line) =>
          line.startsWith("โอกาส:") ||
          line.startsWith("การ์ดชุมชน:") ||
          line.startsWith("เศรษฐกิจเมือง:"),
      );
      if (eventLine) {
        return eventLine;
      }

      const landingLine = lines.find(
        (line) =>
          line.includes("ยังไม่มีเจ้าของ") ||
          line.includes("ค่าผ่านทาง") ||
          line.includes("จ่ายภาษี") ||
          line.includes("เข้าคุก"),
      );
      if (landingLine) {
        return landingLine;
      }
    }

    if (
      actionType === root.GAME_ACTION_TYPES.ROLL_DICE ||
      actionType === root.GAME_ACTION_TYPES.TRY_JAIL_ROLL
    ) {
      const rollLine = lines.find((line) => line.startsWith("ทอย"));
      const doubleLine = lines.find((line) => line.startsWith("ผลการทอย:"));
      if (rollLine && doubleLine) {
        return `${rollLine} | ${doubleLine.replace("ผลการทอย: ", "")}`;
      }
    }

    return lines[0];
  }

  function actionTypeLabel(actionType) {
    const map = {
      [root.GAME_ACTION_TYPES.PAY_JAIL_FINE]: "จ่ายค่าประกันออกจากคุก",
      [root.GAME_ACTION_TYPES.TRY_JAIL_ROLL]: "ทอยแก้คุก",
      [root.GAME_ACTION_TYPES.BUY_PROPERTY]: "ซื้อทรัพย์สิน",
      [root.GAME_ACTION_TYPES.DECLINE_PURCHASE]: "ปฏิเสธการซื้อ",
      [root.GAME_ACTION_TYPES.BID_AUCTION]: "ระบบประมูลเก่า",
      [root.GAME_ACTION_TYPES.PASS_AUCTION]: "ระบบประมูลเก่า",
      [root.GAME_ACTION_TYPES.BUILD_HOUSE]: "สร้างบ้าน",
      [root.GAME_ACTION_TYPES.SELL_HOUSE]: "ขายบ้าน",
      [root.GAME_ACTION_TYPES.MORTGAGE]: "จำนอง",
      [root.GAME_ACTION_TYPES.UNMORTGAGE]: "ไถ่ถอน",
      [root.GAME_ACTION_TYPES.SELL_PROPERTY]: "ขายอสังหา",
      [root.GAME_ACTION_TYPES.DECLARE_BANKRUPTCY]: "ล้มละลาย",
      [root.GAME_ACTION_TYPES.END_TURN]: "จบเทิร์น",
    };
    return map[actionType] ?? "ดำเนินการ";
  }

  function formatComebackLine(turn) {
    if (!turn?.comebackBoostApplied) {
      return "";
    }

    const baseDice =
      Number.parseInt(String(turn.baseDiceValue ?? turn.diceValue), 10) ||
      turn.diceValue;
    const boostAmount =
      Number.parseInt(String(turn.comebackBoostAmount ?? 0), 10) || 0;
    if (boostAmount > 0) {
      return `เร่งแซง +${boostAmount} (${baseDice}->${turn.diceValue})`;
    }

    return `เร่งแซงติดเพดาน (${baseDice}->${turn.diceValue})`;
  }

  function formatFrenzyLine(turn) {
    if (!turn?.frenzySnake) {
      return "";
    }

    if (turn.frenzySnakeTriggered) {
      return `งูคลุ้มคลั่งทำงาน ${turn.frenzySnake.from}->${turn.frenzySnake.to}`;
    }

    if (turn.frenzySnakeBlockedByShield) {
      return `งูคลุ้มคลั่งโผล่ที่ ${turn.frenzySnake.from} แต่โดนกันได้`;
    }

    return `งูคลุ้มคลั่งโผล่ที่ ${turn.frenzySnake.from}`;
  }

  function formatItemLine(turn) {
    if (!turn) {
      return "";
    }

    const chunks = [];
    if (turn.snakeRepellentBlockedSnake) {
      chunks.push("Repellent กันงูสำเร็จ");
    }
    if (turn.ladderHackApplied) {
      chunks.push(`Ladder Hack +${turn.ladderHackBoostAmount ?? 0}`);
    }

    if (Array.isArray(turn.itemEffects)) {
      for (const effect of turn.itemEffects.slice(0, 2)) {
        const meta = root.utils.boardItemMeta(effect.itemType);
        chunks.push(`${meta.name}: ${effect.summary ?? "-"}`);
      }
      if (turn.itemEffects.length > 2) {
        chunks.push(`ไอเท็มทำงาน ${turn.itemEffects.length} รายการ`);
      }
    }

    return chunks.join(" | ");
  }

  function buildMonopolyClientFx(turn, previousRoom, nextRoom) {
    const moneyEvents = buildMoneyEvents(turn, previousRoom, nextRoom);
    return {
      eventNotice: resolveEventNotice(turn),
      moneyEvents,
      currentPlayerMoneySummary:
        moneyEvents.find((entry) => entry.playerId === turn?.playerId) ?? null,
      gameResult: nextRoom?.gameResult ?? null,
    };
  }

  function resolvePreviousActionFxRoom(nextRoom, incomingRevision) {
    const nextRoomCode = normalizeRoomCode(nextRoom);
    const latestKnownRoomCode = normalizeRoomCode(lastActionFxRoom);
    if (
      lastActionFxRoom &&
      nextRoomCode &&
      latestKnownRoomCode === nextRoomCode &&
      incomingRevision > 0 &&
      lastActionFxRevision > 0 &&
      lastActionFxRevision < incomingRevision
    ) {
      return lastActionFxRoom;
    }

    return state.deferredRoom ?? state.room;
  }

  function syncActionFxBaseline(room, options = {}) {
    const revision =
      Number.parseInt(
        String(
          options.revision ??
            root.viewState?.resolveRoomSnapshotRevision?.(room) ??
            0,
        ),
        10,
      ) || 0;
    if (!options.force && revision > 0 && revision < lastActionFxRevision) {
      return;
    }

    lastActionFxRoom = room ?? null;
    lastActionFxRevision = revision;
  }

  function normalizeRoomCode(room) {
    return String(room?.roomCode ?? room?.RoomCode ?? "")
      .trim()
      .toUpperCase();
  }

  function resolveEventNotice(turn) {
    const actionLogs = Array.isArray(turn?.actionLogs) ? turn.actionLogs : [];
    const line = actionLogs
      .map((entry) => String(entry ?? "").trim())
      .find(
        (entry) =>
          entry.startsWith("โอกาส:") ||
          entry.startsWith("การ์ดชุมชน:") ||
          entry.startsWith("เศรษฐกิจเมือง:"),
      );
    if (!line) {
      return null;
    }

    const tone = line.startsWith("โอกาส:")
      ? "chance"
      : line.startsWith("การ์ดชุมชน:")
        ? "community"
        : "economy";
    const title =
      tone === "chance"
        ? "โอกาส"
        : tone === "community"
          ? "การ์ดชุมชน"
          : "เศรษฐกิจเมือง";
    return {
      tone,
      title,
      text: line,
      holdMs: tone === "economy" ? 3000 : 3400,
    };
  }

  function buildMoneyEvents(turn, previousRoom, nextRoom) {
    const prevPlayers = Array.isArray(previousRoom?.players)
      ? previousRoom.players
      : [];
    const nextPlayers = Array.isArray(nextRoom?.players) ? nextRoom.players : [];
    if (prevPlayers.length === 0 || nextPlayers.length === 0) {
      return [];
    }

    const actionLogs = Array.isArray(turn?.actionLogs) ? turn.actionLogs : [];
    const primaryLog = actionLogs
      .map((entry) => String(entry ?? "").trim())
      .find((entry) => /฿|ค่าผ่านทาง|ซื้อ|จ่าย|รับ|ขาย/.test(entry));

    const moneyEvents = [];
    for (const nextPlayer of nextPlayers) {
      const prevPlayer = prevPlayers.find(
        (entry) => entry.playerId === nextPlayer.playerId,
      );
      if (!prevPlayer) {
        continue;
      }

      const beforeCash = parseCash(prevPlayer.cash);
      const afterCash = parseCash(nextPlayer.cash);
      const delta = afterCash - beforeCash;
      if (delta === 0) {
        continue;
      }

      const incoming = delta > 0;
      moneyEvents.push({
        playerId: nextPlayer.playerId,
        playerName: nextPlayer.displayName,
        delta,
        amount: Math.abs(delta),
        beforeCash,
        afterCash,
        incoming,
        title: resolveMoneyEventTitle(turn, incoming, primaryLog),
        detail: primaryLog || "",
      });
    }

    return moneyEvents.sort((left, right) => {
      if (left.playerId === turn?.playerId && right.playerId !== turn?.playerId) {
        return -1;
      }
      if (right.playerId === turn?.playerId && left.playerId !== turn?.playerId) {
        return 1;
      }
      if (left.incoming !== right.incoming) {
        return left.incoming ? 1 : -1;
      }
      return right.amount - left.amount;
    });
  }

  function resolveMoneyEventTitle(turn, incoming, primaryLog) {
    const log = String(primaryLog ?? "");
    if (log.includes("ค่าผ่านทาง")) {
      return incoming ? "รับค่าผ่านทาง" : "จ่ายค่าผ่านทาง";
    }
    if (log.includes("จ่ายค่าประกัน")) {
      return incoming ? "เงินเข้า" : "จ่ายค่าประกัน";
    }
    if (log.includes("ซื้อ")) {
      return incoming ? "ขายสิทธิ์ต่อ" : "ซื้ออสังหา";
    }
    if (log.includes("เวนคืน")) {
      return incoming ? "รับเงินคืน" : "ทรัพย์ถูกเวนคืน";
    }
    if (
      Number.parseInt(
        String(turn?.actionType ?? root.GAME_ACTION_TYPES.ROLL_DICE),
        10,
      ) === root.GAME_ACTION_TYPES.BUILD_HOUSE
    ) {
      return incoming ? "เงินเข้า" : "อัปเกรดอสังหา";
    }
    return incoming ? "รับเงิน" : "จ่ายเงิน";
  }

  function parseCash(value) {
    return Number.parseInt(String(value ?? 0), 10) || 0;
  }

  root.realtime = {
    setupConnection,
    invokeHub,
    publishLobbyName,
    flushPendingTurnTrigger,
    seedTurnTriggerCounter,
  };
})();
