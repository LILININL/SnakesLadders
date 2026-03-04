(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { normalizeName } = root.utils;

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
        state.deferredRoom = room;
        if (room?.status === root.GAME_STATUS.STARTED) {
          state.pendingTurnChangedPlayerId = room.currentTurnPlayerId ?? "";
        }
        return;
      }

      state.room = room;
      root.session.syncProfileAvatarFromRoom(room, state.playerId);
      flushPendingTurnTrigger(room);
      root.boardFocus?.onRoomBound?.(false);
      root.feedback.renderAll();
    });

    state.connection.on("GameStarted", (room) => {
      state.room = room;
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

    state.connection.on("DiceRolled", async (payload) => {
      root.feedback.logEvent(formatDiceEvent(payload.turn, payload.room));
      await root.turnAnimation.queue(payload);
    });

    state.connection.on("GameFinished", (payload) => {
      const winnerId = payload.turn.winnerPlayerId ?? "";
      const winnerName =
        payload.room?.players?.find((x) => x.playerId === winnerId)
          ?.displayName ??
        winnerId ??
        "-";
      root.feedback.logEvent(`จบเกมแล้ว ผู้ชนะคือ ${winnerName || "-"}`);
      if (state.animating) {
        state.deferredRoom = payload.room;
      } else {
        state.lastTurn = payload.turn;
        state.room = payload.room;
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
    state.room = payload.room;
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
    state.deferredRoom = null;
    state.pendingTurnChangedPlayerId = "";
    state.lastAnnouncedTurnCounter = -1;
    state.pendingBeaconTargetPlayerId = "";
    state.pageTransitioning = false;
    state.pageTransitionDirection = 0;
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

  root.realtime = {
    setupConnection,
    invokeHub,
    publishLobbyName,
    flushPendingTurnTrigger,
    seedTurnTriggerCounter,
  };
})();
