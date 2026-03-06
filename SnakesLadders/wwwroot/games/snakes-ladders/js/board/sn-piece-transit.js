(() => {
  const root = window.SNL;
  const { state, el } = root;

  async function run(segment) {
    if (
      !canRenderSegment(segment) ||
      !root.boardOverlay?.getJumpPathData ||
      !root.boardOverlay?.pointAtPath ||
      !el.board
    ) {
      return false;
    }

    const pathData = root.boardOverlay.getJumpPathData(
      segment.from,
      segment.to,
      segment.jumpType,
      segment.seed,
    );
    if (!pathData) {
      return false;
    }

    const token = ensureToken();
    if (!token) {
      return false;
    }

    state.animTransitActive = true;
    state.animTransitPlayerId = segment.playerId;
    root.feedback.renderAll();

    setTokenIdentity(token, segment.playerId);
    token.classList.toggle("snake", pathData.kind === "snake");
    token.classList.remove("hidden");

    await animate(pathData, token, getPathDurationMs(pathData));

    state.animPlayerPosition = segment.to;
    resetTransitState();
    hideToken();
    root.feedback.renderAll();
    return true;
  }

  function canRenderSegment(segment) {
    const boardSize =
      state.room?.board?.size ?? state.deferredRoom?.board?.size ?? 0;
    if (!boardSize || !segment) {
      return false;
    }

    const range = root.boardPage.getVisibleRange(
      boardSize,
      state.visiblePageStart,
    );
    return (
      root.boardPage.isCellVisible(segment.from, range) &&
      root.boardPage.isCellVisible(segment.to, range)
    );
  }

  function reset() {
    resetTransitState();
    hideToken();
  }

  async function animate(pathData, token, duration) {
    await new Promise((resolve) => {
      const start = performance.now();
      const frame = (now) => {
        const raw = clamp((now - start) / duration, 0, 1);
        const t = 0.5 - Math.cos(Math.PI * raw) / 2;
        const point = root.boardOverlay.pointAtPath(pathData, t);
        const angle = root.boardOverlay.angleAtPath(pathData, t);
        if (point) {
          placeToken(token, point, angle);
        }

        if (raw < 1) {
          requestAnimationFrame(frame);
        } else {
          resolve();
        }
      };

      requestAnimationFrame(frame);
    });
  }

  function getPathDurationMs(pathData) {
    const sampler = root.boardOverlay?.pointAtPath;
    if (!sampler) {
      return 3000;
    }

    let length = 0;
    let prev = sampler(pathData, 0);
    for (let i = 1; i <= 14; i++) {
      const next = sampler(pathData, i / 14);
      length += Math.hypot(next.x - prev.x, next.y - prev.y);
      prev = next;
    }

    const speedFactor = pathData.kind === "snake" ? 8.8 : 8.1;
    return clamp(length * speedFactor, 2200, 7200);
  }

  function ensureToken() {
    if (!el.board) {
      return null;
    }

    let token = document.getElementById("transitToken");
    if (token) {
      return token;
    }

    token = document.createElement("div");
    token.id = "transitToken";
    token.className = "transit-token hidden";
    token.setAttribute("aria-hidden", "true");
    (el.board.parentElement ?? el.board).appendChild(token);
    return token;
  }

  function setTokenIdentity(token, playerId) {
    const players = state.room?.players ?? state.deferredRoom?.players ?? [];
    const player = players.find((x) => x.playerId === playerId);
    const safeAvatarId =
      root.utils?.normalizeAvatarId?.(player?.avatarId, 1) ?? 1;
    const safeAvatarSrc = root.utils?.avatarSrc?.(safeAvatarId) ?? "";
    token.dataset.playerId = playerId ?? "";
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
      const markerMap = root.utils?.buildPlayerMarkerMap?.(players) ?? new Map();
      const marker =
        root.utils?.resolvePlayerMarker?.(
          playerId,
          player?.displayName,
          markerMap,
        ) ?? "ผ";
      token.classList.remove("avatar");
      delete token.dataset.avatarSrc;
      if (token.textContent !== marker || token.querySelector(".token-avatar-img")) {
        token.replaceChildren();
        token.textContent = marker;
      }
    }
    token.title = player?.displayName ?? playerId;
    token.classList.toggle("me", playerId === state.playerId);
  }

  function placeToken(token, point, angle) {
    const stageEl = el.boardStage ?? el.board.parentElement ?? el.board;
    const stageRect = stageEl.getBoundingClientRect();

    const left =
      point.x + (el.board.getBoundingClientRect().left - stageRect.left);
    const top =
      point.y + (el.board.getBoundingClientRect().top - stageRect.top);

    if (![left, top].every(Number.isFinite)) {
      return;
    }

    token.style.left = `${Math.round(left)}px`;
    token.style.top = `${Math.round(top)}px`;
    if (token.classList.contains("avatar")) {
      token.style.transform = "translate(-50%, -50%)";
      return;
    }

    token.style.transform = `translate(-50%, -50%) rotate(${Math.round(angle)}deg)`;
  }

  function hideToken() {
    const token = document.getElementById("transitToken");
    if (!token) {
      return;
    }

    token.classList.add("hidden");
    token.classList.remove("snake");
    token.style.transform = "translate(-50%, -50%) rotate(0deg)";
  }

  function resetTransitState() {
    state.animTransitActive = false;
    state.animTransitPlayerId = "";
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  root.pieceTransit = {
    run,
    reset,
  };
})();
