(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml } = root.utils;
  let resizeTimer = 0;

  function renderAll() {
    renderRoomShell();
    updateChatSidebarLayout();
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

    if (inRoom) {
      state.chatPanelOpen = true;
      state.chatUnreadCount = 0;
    } else {
      state.chatPanelOpen = false;
      state.chatUnreadCount = 0;
    }

    document.body.classList.toggle("in-room", inRoom);
    document.body.classList.toggle("in-game", inRoom && started);
    document.body.classList.toggle("chat-open", false);
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

    if (el.chatFabBtn) {
      el.chatFabBtn.classList.add("hidden");
      el.chatFabBtn.classList.remove("open");
      el.chatFabBtn.setAttribute("aria-label", "OpenChat");
    }
    if (el.chatFabLabel) {
      el.chatFabLabel.textContent = "OpenChat";
    }
    el.chatSection.classList.remove("chat-sidebar");
    el.chatSection.classList.toggle("hidden", !inRoom);
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

    state.chatPanelOpen = true;
    state.chatUnreadCount = 0;
    renderChatBadge();
    renderRoomShell();
    updateChatSidebarLayout();
    requestAnimationFrame(() => el.chatInput?.focus());
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

  function updateChatSidebarLayout() {
    if (!isInRoom() || !el.chatSection || !el.chatSection.classList.contains("chat-sidebar")) {
      resetChatSidebarLayout();
      return;
    }

    if (window.matchMedia("(max-width: 900px)").matches) {
      resetChatSidebarLayout();
      return;
    }

    const anchor = el.boardStage ?? el.boardWrap;
    if (!anchor) {
      resetChatSidebarLayout();
      return;
    }

    const rect = anchor.getBoundingClientRect();
    if (!Number.isFinite(rect.top) || !Number.isFinite(rect.height) || rect.height <= 0) {
      resetChatSidebarLayout();
      return;
    }

    const minTop = 76;
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;
    const top = Math.max(minTop, Math.round(rect.top));
    const maxHeight = Math.max(280, viewportHeight - top - 12);
    const height = Math.max(300, Math.min(Math.round(rect.height), maxHeight));

    el.chatSection.style.top = `${top}px`;
    el.chatSection.style.bottom = "auto";
    el.chatSection.style.height = `${height}px`;

    if (el.chatFabBtn) {
      el.chatFabBtn.style.top = `${top}px`;
      el.chatFabBtn.style.bottom = "auto";
    }
  }

  function resetChatSidebarLayout() {
    if (!el.chatSection) {
      return;
    }

    el.chatSection.style.top = "";
    el.chatSection.style.bottom = "";
    el.chatSection.style.height = "";
    if (el.chatFabBtn) {
      el.chatFabBtn.style.top = "";
      el.chatFabBtn.style.bottom = "";
    }
  }

  el.board.addEventListener("scroll", () => {
    updateFloatingRollButton();
  });

  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = window.setTimeout(() => {
      updateFloatingRollButton();
      updateChatSidebarLayout();
    }, 80);
  });

  window.addEventListener("scroll", () => {
    updateChatSidebarLayout();
  }, { passive: true });

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
