(() => {
  const root = window.SNL;
  const { state, el } = root;
  let resizeTimer = 0;
  const tokenByPlayerId = new Map();

  function render(players, turnPlayerId, range = null) {
    const layer = ensureLayer();
    const board = state.room?.board;
    if (!layer || !board || !el.board) {
      clear();
      return;
    }

    const markerMap =
      root.utils?.buildPlayerMarkerMap?.(players ?? []) ?? new Map();

    const visibleRange =
      range ??
      root.boardPage.getVisibleRange(board.size, state.visiblePageStart);
    const grouped = new Map();

    for (const player of players ?? []) {
      if (
        state.animTransitActive &&
        state.animTransitPlayerId === player.playerId
      ) {
        continue;
      }

      if (!root.boardPage.isCellVisible(player.position, visibleRange)) {
        continue;
      }

      const bucket = grouped.get(player.position) ?? [];
      bucket.push(player);
      grouped.set(player.position, bucket);
    }

    const activePlayerIds = new Set();

    for (const [cellNumber, bucket] of grouped) {
      const cellEl = el.board.querySelector(`[data-cell="${cellNumber}"]`);
      if (!cellEl) {
        continue;
      }

      const offsets = buildOffsets(bucket.length);
      bucket.forEach((player, index) => {
        const token = getOrCreateToken(player.playerId);
        hydrateToken(token, player, turnPlayerId, markerMap);
        const offset = offsets[index] ?? { x: 0, y: 0 };
        if (placeToken(token, cellEl, offset)) {
          token.classList.remove("hidden");
          layer.appendChild(token);
          activePlayerIds.add(player.playerId);
        }
      });
    }

    const roomPlayerIds = new Set(
      (state.room?.players ?? []).map((player) => player.playerId),
    );
    for (const [playerId, token] of tokenByPlayerId.entries()) {
      if (!roomPlayerIds.has(playerId)) {
        token.remove();
        tokenByPlayerId.delete(playerId);
        continue;
      }

      if (activePlayerIds.has(playerId)) {
        continue;
      }

      token.classList.add("hidden");
      if (token.parentElement === layer) {
        token.remove();
      }
    }
  }

  function updateFromState() {
    const board = state.room?.board;
    if (!board) {
      clear();
      return;
    }

    render(
      root.viewState.getDisplayPlayers(),
      root.viewState.getDisplayTurnPlayerId(),
      root.boardPage.getVisibleRange(board.size, state.visiblePageStart),
    );
  }

  function clear() {
    const layer = ensureLayer();
    if (layer) {
      layer.replaceChildren();
    }
  }

  function getOrCreateToken(playerId) {
    const key = String(playerId ?? "");
    if (!key) {
      return document.createElement("div");
    }

    const cached = tokenByPlayerId.get(key);
    if (cached) {
      return cached;
    }

    const token = document.createElement("div");
    token.className = "board-token";
    token.dataset.playerId = key;
    tokenByPlayerId.set(key, token);
    return token;
  }

  function hydrateToken(token, player, turnPlayerId, markerMap) {
    token.className = "board-token";
    if (player.playerId === state.playerId) {
      token.classList.add("me");
    }
    if (!player.connected) {
      token.classList.add("offline");
    }

    const anchorTurnsLeft =
      Number.parseInt(String(player.anchorTurnsLeft ?? 0), 10) || 0;
    if (
      player.playerId === turnPlayerId &&
      player.anchorActive &&
      anchorTurnsLeft > 0
    ) {
      token.classList.add("anchor-on-turn");
    }

    const safeAvatarId =
      root.utils?.normalizeAvatarId?.(player.avatarId, 1) ?? 1;
    const safeAvatarSrc = root.utils?.avatarSrc?.(safeAvatarId) ?? "";
    if (safeAvatarSrc) {
      token.classList.add("avatar");
      let avatar = token.querySelector(".token-avatar-img");
      if (!avatar) {
        avatar = document.createElement("img");
        avatar.className = "token-avatar-img";
        token.replaceChildren(avatar);
      }

      if (token.dataset.avatarSrc !== safeAvatarSrc) {
        avatar.src = safeAvatarSrc;
        token.dataset.avatarSrc = safeAvatarSrc;
      }

      const expectedAlt = `Avatar ${safeAvatarId}`;
      if (avatar.alt !== expectedAlt) {
        avatar.alt = expectedAlt;
      }
    } else {
      delete token.dataset.avatarSrc;
      token.classList.remove("avatar");
      const marker =
        root.utils?.resolvePlayerMarker?.(
          player.playerId,
          player.displayName,
          markerMap,
        ) ?? "ผ";
      if (token.textContent !== marker || token.querySelector(".token-avatar-img")) {
        token.replaceChildren();
        token.textContent = marker;
      }
    }

    const title = `${player.displayName}${!player.connected ? " (ออฟไลน์)" : ""}${player.anchorActive && anchorTurnsLeft > 0 ? ` • Anchor ${anchorTurnsLeft}` : ""}`;
    if (token.title !== title) {
      token.title = title;
    }
  }

  function placeToken(token, cellEl, offset) {
    const stageEl = el.boardStage ?? el.board.parentElement ?? el.board;
    const stageRect = stageEl.getBoundingClientRect();
    const cellRect = cellEl.getBoundingClientRect();

    const left = cellRect.left - stageRect.left + cellRect.width / 2 + offset.x;
    const top = cellRect.top - stageRect.top + cellRect.height / 2 + offset.y;

    if (
      ![left, top, stageRect.width, stageRect.height].every(Number.isFinite)
    ) {
      return false;
    }

    if (
      left < -40 ||
      top < -40 ||
      left > stageRect.width + 40 ||
      top > stageRect.height + 40
    ) {
      return false;
    }

    token.style.left = `${Math.round(left)}px`;
    token.style.top = `${Math.round(top)}px`;
    return true;
  }

  function buildOffsets(count) {
    if (count <= 1) {
      return [{ x: 0, y: 0 }];
    }

    const radius = Math.min(16, 5 + count * 2.2);
    const offsets = [];
    for (let i = 0; i < count; i++) {
      const angle = ((Math.PI * 2) / count) * i - Math.PI / 2;
      offsets.push({
        x: Math.cos(angle) * radius,
        y: Math.sin(angle) * radius,
      });
    }
    return offsets;
  }

  function ensureLayer() {
    if (el.boardTokenLayer) {
      return el.boardTokenLayer;
    }

    const board = el.board;
    if (!board) {
      return null;
    }

    const layer = document.createElement("div");
    layer.id = "boardTokenLayer";
    layer.className = "board-token-layer";
    (board.parentElement ?? board).appendChild(layer);
    el.boardTokenLayer = layer;
    return layer;
  }

  window.addEventListener("resize", () => {
    clearTimeout(resizeTimer);
    resizeTimer = window.setTimeout(updateFromState, 80);
  });

  root.boardTokens = {
    render,
    updateFromState,
    clear,
  };
})();
