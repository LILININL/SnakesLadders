(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml } = root.utils;

  function render() {
    const waiting = isWaitingRoom();
    el.readyPanel.classList.toggle("hidden", !waiting);
    if (!waiting) {
      return;
    }

    const hostId = state.room.hostPlayerId;
    const others = state.room.players.filter((x) => x.playerId !== hostId);
    const readyCount = others.filter((x) => x.connected && x.isReady).length;
    el.readySummary.textContent = `${readyCount}/${others.length} ready`;

    el.readyList.innerHTML = state.room.players.map((player) => {
      const host = player.playerId === hostId;
      const tone = host ? "host" : player.connected ? (player.isReady ? "ready" : "not-ready") : "offline";
      const label = host ? "Host" : tone === "ready" ? "Ready" : tone === "offline" ? "Offline" : "Not ready";
      return `
        <li class="ready-item ${tone}">
          <span class="name">${escapeHtml(player.displayName)}</span>
          <span class="ready-pill ${tone}">${escapeHtml(label)}</span>
        </li>
      `;
    }).join("");

    const me = state.room.players.find((x) => x.playerId === state.playerId);
    const canToggle = Boolean(me && me.playerId !== hostId && me.connected);
    el.toggleReadyBtn.classList.toggle("hidden", !canToggle);
    if (canToggle) {
      el.toggleReadyBtn.textContent = me.isReady ? "Set Not Ready" : "I'm Ready";
    }
  }

  function amHost() {
    return Boolean(state.room && state.playerId && state.room.hostPlayerId === state.playerId);
  }

  function allNonHostReady() {
    if (!state.room) {
      return false;
    }

    return state.room.players
      .filter((x) => x.playerId !== state.room.hostPlayerId)
      .every((x) => x.connected && x.isReady);
  }

  function canStartGame() {
    if (!state.room || state.room.status !== GAME_STATUS.WAITING) {
      return false;
    }

    if (!amHost()) {
      return false;
    }

    if (state.room.players.length < 2) {
      return false;
    }

    return allNonHostReady();
  }

  function isWaitingRoom() {
    return Boolean(state.roomCode && state.room && state.room.status === GAME_STATUS.WAITING);
  }

  root.readyUi = {
    render,
    amHost,
    canStartGame
  };
})();
