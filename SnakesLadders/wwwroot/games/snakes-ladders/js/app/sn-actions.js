(() => {
  const root = window.SNL;
  const { state, el } = root;
  const CHARGE_MAX_MS = 1400;
  const CHARGE_MIN_POWER = 0.18;
  const DEFAULT_ROLL_POWER = 0.56;
  const ROLL_CLICK_SUPPRESS_MS = 320;
  const ROLL_REQUEST_COOLDOWN_MS = 1200;
  const ROLL_RELEASE_MS = 1220;
  const CONTROL_DICE_IDLE_FACE = 1;
  const DICE_POSE_BY_VALUE = {
    1: { x: -16, y: 14 },
    2: { x: -16, y: 194 },
    3: { x: -16, y: 104 },
    4: { x: -16, y: -76 },
    5: { x: 74, y: 14 },
    6: { x: -106, y: 14 },
  };

  let chargeRaf = 0;
  let chargeActive = false;
  let chargeStartedAt = 0;
  let chargePointerId = null;
  let chargeValue = 0;
  let keyboardCharging = false;
  let suppressClickUntil = 0;
  let rollRequestPending = false;
  let rollSettleTimer = 0;

  function wireForms() {
    el.nameForm.addEventListener("submit", root.session.onSaveProfileName);

    el.showCreatePanelBtn.addEventListener("click", () => {
      state.createPanelVisible = true;
      state.createGameKey = "";
      root.renderLobby.renderCreatePanel();
      root.ruleUi?.syncAll?.();
      void root.api.refreshAvailableGames?.();
    });

    el.hideCreatePanelBtn.addEventListener("click", () => {
      state.createPanelVisible = false;
      state.createGameKey = "";
      root.renderLobby.renderCreatePanel();
      root.ruleUi?.syncAll?.();
    });

    if (el.createGameList) {
      el.createGameList.addEventListener("click", onSelectCreateGame);
    }
    if (el.createBackToGamesBtn) {
      el.createBackToGamesBtn.addEventListener("click", onBackToGameList);
    }

    el.createForm.addEventListener("submit", onCreateRoom);
    el.joinForm.addEventListener("submit", onJoinRoom);
    if (el.waitingRoomList) {
      el.waitingRoomList.addEventListener("click", onJoinFromRoomList);
    }
    if (el.mainWaitingRoomList) {
      el.mainWaitingRoomList.addEventListener("click", onJoinFromRoomList);
    }

    if (el.refreshRoomsBtn) {
      el.refreshRoomsBtn.addEventListener("click", refreshLobbyLists);
    }
    if (el.refreshRoomsMainBtn) {
      el.refreshRoomsMainBtn.addEventListener("click", refreshLobbyLists);
    }

    el.startGameBtn.addEventListener("click", () => {
      if (!state.roomCode) return;
      if (!root.readyUi.canStartGame()) {
        return;
      }
      root.realtime.invokeHub("StartGame", { roomCode: state.roomCode });
    });

    el.refreshRoomBtn.addEventListener("click", () => {
      if (!state.roomCode) return;
      root.realtime.invokeHub("GetRoom", state.roomCode);
    });

    wireRollControls();
    el.toggleRollBtn.addEventListener("click", () =>
      root.roomUi.toggleRollButton(),
    );
    el.chatFabBtn.addEventListener("click", () =>
      root.roomUi.toggleChatPanel(),
    );
    el.toggleReadyBtn.addEventListener("click", onToggleReady);
    el.readyAvatarPicker?.addEventListener("click", onPickWaitingAvatar);

    el.leaveRoomBtn.addEventListener("click", onLeaveRoom);

    el.chatForm.addEventListener("submit", onSendChat);
    el.clearChatBtn.addEventListener("click", () =>
      root.renderChat.clearChat(),
    );
  }

  function wireRollControls() {
    if (!el.rollDiceFloatingBtn) {
      return;
    }

    el.rollDiceFloatingBtn.addEventListener("pointerdown", onRollPointerDown);
    el.rollDiceFloatingBtn.addEventListener("pointerup", onRollPointerUp);
    el.rollDiceFloatingBtn.addEventListener(
      "pointercancel",
      onRollPointerCancel,
    );
    el.rollDiceFloatingBtn.addEventListener(
      "lostpointercapture",
      onRollPointerCancel,
    );
    el.rollDiceFloatingBtn.addEventListener("keydown", onRollKeyDown);
    el.rollDiceFloatingBtn.addEventListener("keyup", onRollKeyUp);
    el.rollDiceFloatingBtn.addEventListener("click", onRollClickFallback);
    renderControlDiceFace(CONTROL_DICE_IDLE_FACE);
    renderRollCharge(0);
  }

  function onRollPointerDown(event) {
    if (!canRollNow() || chargeActive) {
      return;
    }

    event.preventDefault();
    suppressClickUntil = Date.now() + ROLL_CLICK_SUPPRESS_MS;
    startRollCharge(event.pointerId);
    if (
      event.pointerId != null &&
      el.rollDiceFloatingBtn.setPointerCapture &&
      !el.rollDiceFloatingBtn.hasPointerCapture?.(event.pointerId)
    ) {
      el.rollDiceFloatingBtn.setPointerCapture(event.pointerId);
    }
  }

  function onRollPointerUp(event) {
    if (!chargeActive) {
      return;
    }

    if (
      chargePointerId != null &&
      event.pointerId != null &&
      event.pointerId !== chargePointerId
    ) {
      return;
    }

    event.preventDefault();
    suppressClickUntil = Date.now() + ROLL_CLICK_SUPPRESS_MS;
    const power = stopRollCharge(true);
    void onRollDice(power);
  }

  function onRollPointerCancel(event) {
    if (!chargeActive) {
      return;
    }

    if (
      chargePointerId != null &&
      event?.pointerId != null &&
      event.pointerId !== chargePointerId
    ) {
      return;
    }

    stopRollCharge(false);
  }

  function onRollKeyDown(event) {
    if (event.repeat) {
      return;
    }

    if (event.key !== " " && event.key !== "Enter") {
      return;
    }

    if (!canRollNow() || chargeActive) {
      return;
    }

    event.preventDefault();
    keyboardCharging = true;
    startRollCharge(null);
  }

  function onRollKeyUp(event) {
    if (!keyboardCharging) {
      return;
    }

    if (event.key !== " " && event.key !== "Enter") {
      return;
    }

    event.preventDefault();
    keyboardCharging = false;
    suppressClickUntil = Date.now() + ROLL_CLICK_SUPPRESS_MS;
    const power = stopRollCharge(true);
    void onRollDice(power);
  }

  function onRollClickFallback(event) {
    if (Date.now() < suppressClickUntil || chargeActive) {
      event.preventDefault();
      return;
    }

    if (!canRollNow()) {
      return;
    }

    event.preventDefault();
    void onRollDice(DEFAULT_ROLL_POWER);
  }

  function startRollCharge(pointerId) {
    chargeActive = true;
    chargeStartedAt = performance.now();
    const parsedPointerId = Number.parseInt(String(pointerId ?? ""), 10);
    chargePointerId = Number.isFinite(parsedPointerId) ? parsedPointerId : null;
    chargeValue = 0;
    renderRollCharge(0);
    clearRollSettleState();
    el.rollDiceFloatingBtn.classList.add("charging");
    startControlDiceSpin();
    stepRollCharge();
  }

  function stepRollCharge() {
    if (!chargeActive) {
      return;
    }

    const elapsedMs = Math.max(0, performance.now() - chargeStartedAt);
    chargeValue = resolveChargeLoop(elapsedMs);
    renderRollCharge(chargeValue);
    chargeRaf = window.requestAnimationFrame(stepRollCharge);
  }

  function stopRollCharge(keepPower) {
    if (!chargeActive) {
      return keepPower ? DEFAULT_ROLL_POWER : 0;
    }

    if (chargeRaf) {
      window.cancelAnimationFrame(chargeRaf);
      chargeRaf = 0;
    }

    const elapsedMs = Math.max(0, performance.now() - chargeStartedAt);
    const currentCharge = resolveChargeLoop(elapsedMs);

    chargeActive = false;
    chargeStartedAt = 0;
    chargePointerId = null;
    chargeValue = 0;
    keyboardCharging = false;
    el.rollDiceFloatingBtn.classList.remove("charging");
    stopControlDiceSpin();
    renderRollCharge(0);

    if (!keepPower) {
      startRollSettle(CONTROL_DICE_IDLE_FACE);
      return 0;
    }

    const landedFace = 1 + Math.round(currentCharge * 5);
    startRollSettle(landedFace);
    return Math.max(CHARGE_MIN_POWER, currentCharge);
  }

  function startControlDiceSpin() {
    stopControlDiceSpin();
    if (!el.rollControlDice) {
      return;
    }

    el.rollControlDice.classList.add("spinning");
  }

  function stopControlDiceSpin() {
    if (el.rollControlDice) {
      el.rollControlDice.classList.remove("spinning");
    }
  }

  function startRollSettle(targetFace) {
    if (!el.rollControlDice) {
      renderControlDiceFace(targetFace);
      return;
    }
    clearRollSettleState();
    el.rollControlDice.classList.add("releasing");
    rollSettleTimer = window.setTimeout(() => {
      rollSettleTimer = 0;
      el.rollControlDice?.classList.remove("releasing");
      renderControlDiceFace(targetFace);
    }, ROLL_RELEASE_MS);
  }

  function clearRollSettleState() {
    if (rollSettleTimer) {
      window.clearTimeout(rollSettleTimer);
      rollSettleTimer = 0;
    }
    el.rollControlDice?.classList.remove("releasing");
  }

  function renderControlDiceFace(face) {
    const resolvedFace = normalizeDiceFace(face);
    const pose = DICE_POSE_BY_VALUE[resolvedFace] ??
      DICE_POSE_BY_VALUE[CONTROL_DICE_IDLE_FACE];
    if (el.rollControlDice) {
      el.rollControlDice.dataset.value = String(resolvedFace);
      el.rollControlDice.style.setProperty("--dice-rx", `${pose.x}deg`);
      el.rollControlDice.style.setProperty("--dice-ry", `${pose.y}deg`);
    }
  }

  function normalizeDiceFace(value) {
    const parsed = Number.parseInt(String(value ?? ""), 10);
    if (!Number.isFinite(parsed)) {
      return CONTROL_DICE_IDLE_FACE;
    }
    if (parsed <= 1) {
      return 1;
    }
    if (parsed >= 6) {
      return 6;
    }
    return parsed;
  }

  function syncRollInteraction() {
    const visible = !el.rollDiceFloatingBtn.classList.contains("hidden");
    if (canRollNow() && visible) {
      return;
    }

    stopRollCharge(false);
    clearRollSettleState();
    renderControlDiceFace(CONTROL_DICE_IDLE_FACE);
    renderRollCharge(0);
    suppressClickUntil = 0;
  }

  function canRollNow() {
    if (rollRequestPending) {
      return false;
    }

    const started = state.room?.status === root.GAME_STATUS.STARTED;
    if (!started || !state.roomCode || !state.room || state.animating) {
      return false;
    }

    return root.viewState.getDisplayTurnPlayerId() === state.playerId;
  }

  function clamp01(value) {
    if (!Number.isFinite(value)) {
      return 0;
    }
    if (value <= 0) {
      return 0;
    }
    if (value >= 1) {
      return 1;
    }
    return value;
  }

  function resolveChargeLoop(elapsedMs) {
    if (!Number.isFinite(elapsedMs) || elapsedMs <= 0) {
      return 0;
    }
    const halfCycleMs = Math.max(240, CHARGE_MAX_MS);
    const fullCycleMs = halfCycleMs * 2;
    const cyclePosMs = elapsedMs % fullCycleMs;
    const normalized = cyclePosMs / halfCycleMs;
    if (normalized <= 1) {
      return clamp01(normalized);
    }
    return clamp01(2 - normalized);
  }

  function renderRollCharge(progress) {
    const safeProgress = clamp01(progress);
    const percent = safeProgress >= 0.995
      ? 100
      : Math.max(0, Math.min(99, Math.floor(safeProgress * 100)));
    if (el.rollChargeFill) {
      el.rollChargeFill.style.width = `${percent}%`;
    }
    if (el.rollChargeValue) {
      el.rollChargeValue.textContent = `${percent}%`;
    }
    if (el.rollControlDice) {
      const spinMs = Math.round(1040 - safeProgress * 660);
      el.rollControlDice.style.setProperty(
        "--dice-spin-ms",
        `${Math.max(380, spinMs)}ms`,
      );
    }
  }

  async function onCreateRoom(event) {
    event.preventDefault();

    if (!state.createGameKey) {
      root.feedback.logEvent("กรุณาเลือกเกมก่อนสร้างห้อง", true);
      return;
    }

    const name = root.session.ensureProfileName(el.createName.value);
    if (!name) return;

    const gameKey = root.session.getSelectedGameKey?.() ||
      state.createGameKey ||
      root.GAME_KEYS?.SNAKES_LADDERS ||
      "snakes-ladders";
    const requiresBoardOptions = root.utils.gameSupportsBoardOptions(gameKey);
    const boardSize = parseInt(el.boardSize.value, 10);
    if (
      requiresBoardOptions &&
      (!Number.isFinite(boardSize) || boardSize < 50)
    ) {
      root.feedback.logEvent("ขนาดกระดานต้องอย่างน้อย 50 ช่อง", true);
      return;
    }

    state.createPanelVisible = false;
    root.renderLobby.renderCreatePanel();

    const avatarId = root.session.ensureProfileAvatarId();

    await root.realtime.invokeHub("CreateRoom", {
      playerName: name,
      avatarId,
      gameKey,
      boardOptions: root.session.buildBoardOptions(boardSize, gameKey),
    });
  }

  function onSelectCreateGame(event) {
    const button = event.target.closest(".game-create-card[data-game-key]");
    if (!button || button.disabled) {
      return;
    }

    const gameKey = String(button.dataset.gameKey ?? "").trim().toLowerCase();
    if (!gameKey) {
      return;
    }

    state.createGameKey = gameKey;
    if (el.gameKey) {
      el.gameKey.value = gameKey;
    }
    root.renderLobby.renderCreatePanel();
    root.ruleUi?.syncAll?.();
  }

  function onBackToGameList() {
    state.createGameKey = "";
    if (el.gameKey) {
      el.gameKey.value = "";
    }
    root.renderLobby.renderCreatePanel();
    root.ruleUi?.syncAll?.();
  }

  async function onJoinRoom(event) {
    event.preventDefault();

    const roomCode = String(el.joinRoomCode.value ?? "")
      .trim()
      .toUpperCase();
    if (!roomCode) {
      root.feedback.logEvent("กรุณาใส่รหัสห้องก่อนเข้าร่วม", true);
      return;
    }

    await joinRoomCode(roomCode);
  }

  async function onJoinFromRoomList(event) {
    const button = event.target.closest(".join-room-btn");
    if (!button) return;

    const roomCode = String(button.dataset.roomCode ?? "")
      .trim()
      .toUpperCase();
    if (!roomCode) return;

    el.joinRoomCode.value = roomCode;
    await joinRoomCode(roomCode);
  }

  async function joinRoomCode(roomCode) {
    const name = root.session.ensureProfileName(el.joinName.value);
    if (!name) return;
    const avatarId = root.session.ensureProfileAvatarId();

    const session = root.storage.getRoomSession(roomCode);
    await root.realtime.invokeHub("JoinRoom", {
      roomCode,
      playerName: name,
      avatarId,
      sessionId: session?.sessionId ?? null,
    });
  }

  async function onLeaveRoom() {
    if (!state.roomCode) return;

    stopRollCharge(false);
    clearRollSettleState();
    rollRequestPending = false;

    await root.realtime.invokeHub("LeaveRoom", { roomCode: state.roomCode });
    state.roomCode = "";
    state.playerId = "";
    state.sessionId = "";
    state.room = null;
    state.lastTurn = null;
    state.rollButtonHidden = false;
    state.chatPanelOpen = false;
    state.chatUnreadCount = 0;
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
    state.rollFxPowerHint = 0;
    state.rollFxPowerHintAt = 0;
    root.turnAnimation?.reset?.();
    root.boardFx?.reset?.();
    root.boardFocus?.clearState?.();
    root.boardBeacon?.hide?.();

    root.renderChat.clearChat();
    await root.api.refreshLobbyOnline();
    await root.api.refreshWaitingRooms();
    root.feedback.renderAll();
  }

  async function onSendChat(event) {
    event.preventDefault();
    if (!state.roomCode || !state.room) return;

    const message = String(el.chatInput.value ?? "").trim();
    if (!message) return;

    const sent = await root.realtime.invokeHub("SendChat", {
      roomCode: state.roomCode,
      message,
    });

    if (sent) {
      el.chatInput.value = "";
    }
  }

  async function onRollDice(powerHint = DEFAULT_ROLL_POWER) {
    if (!state.roomCode || !canRollNow()) {
      return;
    }

    rollRequestPending = true;
    state.rollFxPowerHint = clamp01(powerHint);
    state.rollFxPowerHintAt = Date.now();
    const sent = await root.realtime.invokeHub("RollDice", {
      roomCode: state.roomCode,
      useLuckyReroll: false,
      forkChoice: null,
    });
    if (!sent) {
      state.rollFxPowerHint = 0;
      state.rollFxPowerHintAt = 0;
      rollRequestPending = false;
      return;
    }

    window.setTimeout(() => {
      rollRequestPending = false;
    }, ROLL_REQUEST_COOLDOWN_MS);
  }

  async function onToggleReady() {
    if (
      !state.roomCode ||
      !state.room ||
      state.room.status !== root.GAME_STATUS.WAITING
    ) {
      return;
    }

    const me = state.room.players.find((x) => x.playerId === state.playerId);
    if (!me || me.playerId === state.room.hostPlayerId) {
      return;
    }

    await root.realtime.invokeHub("SetReady", {
      roomCode: state.roomCode,
      isReady: !me.isReady,
    });
  }

  async function onPickWaitingAvatar(event) {
    const button = event.target.closest("[data-avatar-id]");
    if (!button || button.disabled) {
      return;
    }

    if (
      !state.roomCode ||
      !state.room ||
      state.room.status !== root.GAME_STATUS.WAITING
    ) {
      return;
    }

    const me = state.room.players.find((x) => x.playerId === state.playerId);
    if (!me) {
      return;
    }

    const isHost = me.playerId === state.room.hostPlayerId;
    if (me.isReady && !isHost) {
      root.feedback.logEvent("ยกเลิกพร้อมก่อน แล้วค่อยเปลี่ยน Avatar");
      return;
    }

    const avatarId = root.utils.normalizeAvatarId(
      button.dataset.avatarId,
      me.avatarId,
    );
    if (avatarId === me.avatarId) {
      return;
    }

    await root.realtime.invokeHub("SetAvatar", {
      roomCode: state.roomCode,
      avatarId,
    });
  }

  async function refreshLobbyLists() {
    await root.api.refreshLobbyOnline();
    await root.api.refreshWaitingRooms();
  }

  root.actions = {
    wireForms,
    syncRollInteraction,
  };
})();
