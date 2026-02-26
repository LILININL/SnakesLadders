(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml } = root.utils;
  let resizeTimer = 0;

  function renderAll() {
    renderRoomShell();
    renderRoomRules();
    renderTurnBanner();
    updateDeadlineAlert();
    updateFloatingRollButton();
    renderChatBadge();
  }

  function renderRoomShell() {
    const inRoom = isInRoom();
    const started = isStarted();
    const waiting = isWaiting();
    const host = root.readyUi?.amHost?.() ?? false;

    document.body.classList.toggle("in-room", inRoom);
    document.body.classList.toggle("in-game", inRoom && started);
    document.body.classList.toggle("chat-open", inRoom && state.chatPanelOpen);
    el.layoutRoot.classList.toggle("in-room", inRoom);
    el.layoutRoot.classList.toggle("game-started", inRoom && started);
    el.lobbyPanel.classList.toggle("hidden", inRoom);
    el.mainLobbyRooms.classList.toggle("hidden", inRoom);
    el.roomGameShell.classList.toggle("hidden", !inRoom);
    el.roomRulesCard.classList.toggle("hidden", !inRoom);

    el.statusSplit.classList.toggle("hidden", !inRoom || !waiting);
    el.statusSplit.classList.toggle("waiting", waiting);
    el.turnSection.classList.toggle("hidden", waiting);
    el.waitingRoomActions.classList.toggle("hidden", !inRoom || started || !host);
    el.leaveRoomBtn.classList.toggle("hidden", !inRoom);

    if (!inRoom) {
      state.chatPanelOpen = false;
      state.chatUnreadCount = 0;
    }

    el.chatFabBtn.classList.toggle("hidden", !inRoom);
    el.chatFabBtn.classList.toggle("active", inRoom && state.chatPanelOpen);
    el.chatSection.classList.toggle("chat-sidebar", inRoom);
    el.chatSection.classList.toggle("hidden", !inRoom || !state.chatPanelOpen);
    el.eventSection.classList.toggle("hidden", inRoom);
  }

  function renderRoomRules() {
    if (!isInRoom()) {
      el.roomRuleList.innerHTML = "";
      return;
    }

    const options = state.room.boardOptions;
    const lines = root.ruleSummary?.buildRoomRuleLines?.(options) ?? [];

    el.roomRuleList.innerHTML = lines.map((line) => `<li>${escapeHtml(line)}</li>`).join("");
  }

  function renderTurnBanner() {
    if (!isInRoom()) {
      hideTurnBanner();
      return;
    }

    if (!isStarted()) {
      el.turnBanner.textContent = "รอหัวห้องกดเริ่มเกม";
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
    el.turnBanner.textContent = mine ? "ถึงตาคุณแล้ว" : `ตาของ ${current.displayName}`;
    el.turnBanner.className = `turn-banner ${mine ? "mine" : "other"}`;
  }

  function hideTurnBanner() {
    el.turnBanner.className = "turn-banner hidden";
    el.turnBanner.textContent = "";
  }

  function updateFloatingRollButton() {
    const myTurn = Boolean(
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
      el.toggleRollBtn.textContent = "แสดงปุ่มทอย";
      return;
    }

    el.rollDiceFloatingBtn.style.left = "50%";
    el.rollDiceFloatingBtn.style.top = "50%";
    el.rollDiceFloatingBtn.classList.remove("hidden");
    el.toggleRollBtn.textContent = "ซ่อนปุ่มทอย";
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

  function toggleChatPanel() {
    if (!isInRoom()) {
      return;
    }

    state.chatPanelOpen = !state.chatPanelOpen;
    if (state.chatPanelOpen) {
      state.chatUnreadCount = 0;
      renderChatBadge();
    }

    renderRoomShell();
    if (state.chatPanelOpen) {
      requestAnimationFrame(() => el.chatInput?.focus());
    }
  }

  function renderChatBadge() {
    if (!el.chatFabBadge) {
      return;
    }

    const unread = Math.max(0, Number.parseInt(String(state.chatUnreadCount ?? 0), 10) || 0);
    if (unread <= 0 || state.chatPanelOpen || !isInRoom()) {
      el.chatFabBadge.textContent = "0";
      el.chatFabBadge.classList.add("hidden");
      return;
    }

    el.chatFabBadge.textContent = unread > 99 ? "99+" : String(unread);
    el.chatFabBadge.classList.remove("hidden");
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
    toggleChatPanel,
    updateDeadlineAlert,
    renderChatBadge
  };
})();
