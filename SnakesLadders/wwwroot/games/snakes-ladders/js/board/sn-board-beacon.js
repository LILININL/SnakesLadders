(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml } = root.utils;

  function init() {
    el.boardBeaconList?.addEventListener("click", onBeaconClick);
  }

  function render() {
    if (!el.boardBeaconList) {
      return;
    }

    const board = state.room?.board;
    if (!board) {
      hide();
      return;
    }

    const range = root.boardPage.getVisibleRange(board.size);
    const displayPlayers = root.viewState?.getDisplayPlayers?.() ?? [];
    const turnPlayerId = root.viewState?.getDisplayTurnPlayerId?.() ?? "";

    const offscreen = displayPlayers
      .filter((player) => !root.boardPage.isCellVisible(player.position, range))
      .sort((a, b) => comparePlayers(a, b, turnPlayerId));

    if (offscreen.length === 0) {
      hide();
      return;
    }

    const rows = offscreen.map((player) => {
      const direction = player.position > range.end ? "↗" : "↙";
      const pending = state.pendingBeaconTargetPlayerId === player.playerId;
      const turn = player.playerId === turnPlayerId;
      const me = player.playerId === state.playerId;
      const offline = !player.connected;
      const labels = [];
      if (turn) labels.push("ถึงตา");
      if (me) labels.push("คุณ");
      if (offline) labels.push("ออฟไลน์");

      return `
        <button class="beacon-chip ${pending ? "pending" : ""} ${turn ? "turn" : ""} ${offline ? "offline" : ""}" data-player-id="${escapeHtml(player.playerId)}" type="button">
          <span class="arrow">${direction}</span>
          <span class="name">${escapeHtml(player.displayName)}</span>
          <span class="cell">ช่อง ${player.position}</span>
          <span class="tags">${escapeHtml(labels.join(" • "))}</span>
        </button>
      `;
    });

    el.boardBeaconList.innerHTML = rows.join("");
    el.boardBeaconList.classList.remove("hidden");
  }

  function hide() {
    el.boardBeaconList.innerHTML = "";
    el.boardBeaconList.classList.add("hidden");
  }

  function onBeaconClick(event) {
    const button = event.target.closest(".beacon-chip");
    if (!button) {
      return;
    }

    const playerId = String(button.dataset.playerId ?? "").trim();
    if (!playerId) {
      return;
    }

    root.boardFocus?.jumpToPlayer?.(playerId, { animate: true });
  }

  function comparePlayers(a, b, turnPlayerId) {
    const scoreA = playerScore(a, turnPlayerId);
    const scoreB = playerScore(b, turnPlayerId);
    if (scoreA !== scoreB) {
      return scoreA - scoreB;
    }

    if (a.position !== b.position) {
      return b.position - a.position;
    }

    return String(a.displayName).localeCompare(String(b.displayName), "th");
  }

  function playerScore(player, turnPlayerId) {
    if (player.playerId === state.pendingBeaconTargetPlayerId) return 0;
    if (player.playerId === turnPlayerId) return 1;
    if (player.playerId === state.playerId) return 2;
    if (!player.connected) return 4;
    return 3;
  }

  root.boardBeacon = {
    init,
    render,
    hide,
  };
})();
