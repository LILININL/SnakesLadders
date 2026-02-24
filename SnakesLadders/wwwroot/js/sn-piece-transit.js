(() => {
  const root = window.SNL;
  const { state, el } = root;

  async function run(segment) {
    if (!root.boardOverlay?.getJumpPathData || !root.boardOverlay?.pointAtPath || !el.board) {
      return false;
    }

    const pathData = root.boardOverlay.getJumpPathData(segment.from, segment.to, segment.jumpType, segment.seed);
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

  function reset() {
    resetTransitState();
    hideToken();
  }

  async function animate(pathData, token, duration) {
    await new Promise((resolve) => {
      const start = performance.now();
      const frame = (now) => {
        const raw = clamp((now - start) / duration, 0, 1);
        const t = 0.5 - (Math.cos(Math.PI * raw) / 2);
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
      return 2400;
    }

    let length = 0;
    let prev = sampler(pathData, 0);
    for (let i = 1; i <= 14; i++) {
      const next = sampler(pathData, i / 14);
      length += Math.hypot(next.x - prev.x, next.y - prev.y);
      prev = next;
    }

    const speedFactor = pathData.kind === "snake" ? 6.6 : 6.0;
    return clamp(length * speedFactor, 1800, 6200);
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
    const marker = (player?.displayName?.[0] ?? "P").toUpperCase();
    token.textContent = marker;
    token.title = player?.displayName ?? playerId;
    token.classList.toggle("me", playerId === state.playerId);
  }

  function placeToken(token, point, angle) {
    const left = el.board.offsetLeft + point.x - el.board.scrollLeft;
    const top = el.board.offsetTop + point.y - el.board.scrollTop;
    token.style.left = `${Math.round(left)}px`;
    token.style.top = `${Math.round(top)}px`;
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
    reset
  };
})();
