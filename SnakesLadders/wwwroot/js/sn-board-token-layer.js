(() => {
  const root = window.SNL;
  const { state, el } = root;
  let resizeTimer = 0;

  function render(players, turnPlayerId) {
    const layer = ensureLayer();
    if (!layer || !state.room?.board) {
      clear();
      return;
    }

    const grouped = new Map();
    for (const player of players ?? []) {
      if (state.animTransitActive && state.animTransitPlayerId === player.playerId) {
        continue;
      }
      const bucket = grouped.get(player.position) ?? [];
      bucket.push(player);
      grouped.set(player.position, bucket);
    }

    layer.replaceChildren();
    for (const [cellNumber, bucket] of grouped) {
      const cellEl = el.board.querySelector(`[data-cell="${cellNumber}"]`);
      if (!cellEl) {
        continue;
      }

      const offsets = buildOffsets(bucket.length);
      bucket.forEach((player, index) => {
        const token = document.createElement("div");
        token.className = "board-token";
        if (player.playerId === state.playerId) token.classList.add("me");
        if (player.playerId === turnPlayerId) token.classList.add("turn");
        if (!player.connected) token.classList.add("offline");
        token.textContent = (player.displayName?.[0] ?? "P").toUpperCase();
        token.title = `${player.displayName}${!player.connected ? " (Offline)" : ""}`;

        const offset = offsets[index] ?? { x: 0, y: 0 };
        placeToken(token, cellEl, offset);
        layer.appendChild(token);
      });
    }
  }

  function updateFromState() {
    if (!state.room?.board) {
      clear();
      return;
    }

    render(root.viewState.getDisplayPlayers(), root.viewState.getDisplayTurnPlayerId());
  }

  function clear() {
    const layer = ensureLayer();
    if (layer) {
      layer.replaceChildren();
    }
  }

  function placeToken(token, cellEl, offset) {
    const left = el.board.offsetLeft + cellEl.offsetLeft + (cellEl.offsetWidth / 2) - el.board.scrollLeft + offset.x;
    const top = el.board.offsetTop + cellEl.offsetTop + (cellEl.offsetHeight / 2) - el.board.scrollTop + offset.y;
    token.style.left = `${Math.round(left)}px`;
    token.style.top = `${Math.round(top)}px`;
  }

  function buildOffsets(count) {
    if (count <= 1) {
      return [{ x: 0, y: 0 }];
    }

    const radius = Math.min(14, 5 + count * 1.8);
    const offsets = [];
    for (let i = 0; i < count; i++) {
      const angle = ((Math.PI * 2) / count) * i - (Math.PI / 2);
      offsets.push({
        x: Math.cos(angle) * radius,
        y: Math.sin(angle) * radius
      });
    }
    return offsets;
  }

  function ensureLayer() {
    if (el.boardTokenLayer) {
      return el.boardTokenLayer;
    }

    if (!el.board) {
      return null;
    }

    const layer = document.createElement("div");
    layer.id = "boardTokenLayer";
    layer.className = "board-token-layer";
    (el.board.parentElement ?? el.board).appendChild(layer);
    el.boardTokenLayer = layer;
    return layer;
  }

  el.board?.addEventListener("scroll", updateFromState);
  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = window.setTimeout(updateFromState, 80);
  });

  root.boardTokens = {
    render,
    updateFromState,
    clear
  };
})();
