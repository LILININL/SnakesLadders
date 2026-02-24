(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml, densityLabel } = root.utils;
  let resizeTimer = 0;

  function renderAll() {
    renderRoomShell();
    renderRoomRules();
    renderTurnBanner();
    updateDeadlineAlert();
    updateFloatingRollButton();
  }

  function renderRoomShell() {
    const inRoom = isInRoom();
    const started = isStarted();
    const waiting = isWaiting();
    const host = root.readyUi?.amHost?.() ?? false;

    document.body.classList.toggle("in-room", inRoom);
    document.body.classList.toggle("in-game", inRoom && started);
    el.layoutRoot.classList.toggle("in-room", inRoom);
    el.layoutRoot.classList.toggle("game-started", inRoom && started);
    el.lobbyPanel.classList.toggle("hidden", inRoom);
    el.roomRulesCard.classList.toggle("hidden", !inRoom);

    el.statusSplit.classList.toggle("hidden", !inRoom || !waiting);
    el.statusSplit.classList.toggle("waiting", waiting);
    el.turnSection.classList.toggle("hidden", waiting);
    el.waitingRoomActions.classList.toggle("hidden", !inRoom || started || !host);
    el.leaveRoomBtn.classList.toggle("hidden", !inRoom);

    // Keep room page focused to board + rules as requested.
    el.chatSection.classList.toggle("hidden", inRoom);
    el.eventSection.classList.toggle("hidden", inRoom);
  }

  function renderRoomRules() {
    if (!isInRoom()) {
      el.roomRuleList.innerHTML = "";
      return;
    }

    const options = state.room.boardOptions;
    const rules = options.ruleOptions ?? {};
    const lines = [
      `Board: ${options.boardSize} cells`,
      `Density: ${densityLabel(options.densityMode)}`,
      `Overflow: ${options.overflowMode === 1 ? "Back by overflow x2" : "Stay put"}`
    ];

    if (rules.turnTimerEnabled) lines.push(`Turn timer: ${rules.turnSeconds}s`);
    if (rules.roundLimitEnabled) lines.push(`Round limit: ${rules.maxRounds}`);
    if (rules.snakeFrenzyEnabled) lines.push(`Snake frenzy: Every ${rules.snakeFrenzyIntervalTurns} turns`);
    if (rules.checkpointShieldEnabled) lines.push(`Checkpoint shield: Every ${rules.checkpointInterval} cells`);
    if (rules.comebackBoostEnabled) lines.push("Comeback boost");
    if (rules.mercyLadderEnabled) lines.push(`Mercy ladder: +${rules.mercyLadderBoost}`);
    if (rules.marathonSpeedupEnabled) lines.push("Marathon speedup");

    el.roomRuleList.innerHTML = lines.map((line) => `<li>${escapeHtml(line)}</li>`).join("");
  }

  function renderTurnBanner() {
    if (!isInRoom()) {
      hideTurnBanner();
      return;
    }

    if (!isStarted()) {
      el.turnBanner.textContent = "Waiting for host to start";
      el.turnBanner.className = "turn-banner waiting";
      return;
    }

    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const current = state.room.players.find((x) => x.playerId === displayTurnPlayerId);
    if (!current) {
      hideTurnBanner();
      return;
    }

    const mine = current.playerId === state.playerId;
    el.turnBanner.textContent = mine ? "Your Turn" : `Turn: ${current.displayName}`;
    el.turnBanner.className = `turn-banner ${mine ? "mine" : "other"}`;
  }

  function hideTurnBanner() {
    el.turnBanner.className = "turn-banner hidden";
    el.turnBanner.textContent = "";
  }

  function updateFloatingRollButton() {
    const mePosition = root.viewState.getPlayerPosition(state.playerId);
    const myTurn = Boolean(
      Number.isFinite(mePosition) &&
      isStarted() &&
      !state.animating &&
      root.viewState.getDisplayTurnPlayerId() === state.playerId
    );
    if (!myTurn || !state.room?.board) {
      state.rollButtonHidden = false;
      el.rollDiceFloatingBtn.classList.add("hidden");
      el.toggleRollBtn.classList.add("hidden");
      return;
    }

    el.toggleRollBtn.classList.remove("hidden");
    if (state.rollButtonHidden) {
      el.rollDiceFloatingBtn.classList.add("hidden");
      el.toggleRollBtn.textContent = "Show Roll";
      return;
    }

    const cell = el.board.querySelector(`[data-cell="${mePosition}"]`);
    if (!cell) {
      el.rollDiceFloatingBtn.classList.add("hidden");
      return;
    }

    const left = cell.offsetLeft + (cell.offsetWidth / 2) - el.board.scrollLeft;
    const top = cell.offsetTop + (cell.offsetHeight / 2) - el.board.scrollTop;
    el.rollDiceFloatingBtn.style.left = `${Math.round(left)}px`;
    el.rollDiceFloatingBtn.style.top = `${Math.round(top)}px`;
    el.rollDiceFloatingBtn.classList.remove("hidden");
    el.toggleRollBtn.textContent = "Hide Roll";
  }

  function updateDeadlineAlert() {
    if (state.animating) {
      hideDeadlineAlert();
      return;
    }

    if (!isStarted()) {
      hideDeadlineAlert();
      return;
    }

    const deadline = state.room?.turnDeadlineUtc;
    if (!deadline) {
      hideDeadlineAlert();
      return;
    }

    const remainMs = new Date(deadline).getTime() - Date.now();
    if (!Number.isFinite(remainMs) || remainMs <= 0 || remainMs > 5000) {
      hideDeadlineAlert();
      return;
    }

    const remainSec = Math.max(1, Math.ceil(remainMs / 1000));
    el.turnDeadlineAlert.textContent = String(remainSec);
    el.turnDeadlineAlert.className = `turn-deadline-alert show sec-${remainSec}`;
  }

  function hideDeadlineAlert() {
    el.turnDeadlineAlert.className = "turn-deadline-alert hidden";
    el.turnDeadlineAlert.textContent = "";
  }

  function toggleRollButton() {
    state.rollButtonHidden = !state.rollButtonHidden;
    updateFloatingRollButton();
  }

  function isInRoom() {
    return Boolean(state.roomCode && state.room);
  }

  function isStarted() {
    return state.room?.status === GAME_STATUS.STARTED;
  }

  function isWaiting() {
    return state.room?.status === GAME_STATUS.WAITING;
  }

  el.board.addEventListener("scroll", () => {
    updateFloatingRollButton();
  });

  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = window.setTimeout(() => updateFloatingRollButton(), 80);
  });

  window.setInterval(updateDeadlineAlert, 200);

  root.roomUi = {
    renderAll,
    updateFloatingRollButton,
    toggleRollButton,
    updateDeadlineAlert
  };
})();
