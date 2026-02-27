(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml, avatarSrc, normalizeAvatarId } = root.utils;

  function render() {
    const waiting = isWaitingRoom();
    el.readyPanel.classList.toggle("hidden", !waiting);
    if (!waiting) {
      return;
    }

    const hostId = state.room.hostPlayerId;
    const players = state.room.players;
    const readyCount = players.filter((x) =>
      x.playerId === hostId ? true : x.connected && x.isReady
    ).length;
    el.readySummary.textContent = `พร้อม ${readyCount}/${players.length} คน`;

    el.readyList.innerHTML = players.map((player) => {
      const host = player.playerId === hostId;
      const tone = host ? "host" : player.connected ? (player.isReady ? "ready" : "not-ready") : "offline";
      const label = host ? "หัวห้อง" : tone === "ready" ? "พร้อม" : tone === "offline" ? "ออฟไลน์" : "ยังไม่พร้อม";
      const safeAvatarId = normalizeAvatarId(player.avatarId, 1);
      return `
        <li class="ready-item ${tone}">
          <span class="name-wrap">
            <img class="inline-avatar" src="${avatarSrc(safeAvatarId)}" alt="Avatar ${safeAvatarId}">
            <span class="name">${escapeHtml(player.displayName)}</span>
          </span>
          <span class="ready-pill ${tone}">${escapeHtml(label)}</span>
        </li>
      `;
    }).join("");

    const me = state.room.players.find((x) => x.playerId === state.playerId);
    renderAvatarPicker(waiting, me, hostId);
    const canToggle = Boolean(me && me.playerId !== hostId && me.connected);
    el.toggleReadyBtn.classList.toggle("hidden", !canToggle);
    if (canToggle) {
      el.toggleReadyBtn.textContent = me.isReady ? "ขอยังไม่พร้อม" : "ฉันพร้อมแล้ว";
    }
  }

  function renderAvatarPicker(waiting, me, hostId) {
    if (!el.readyAvatarSection || !el.readyAvatarPicker) {
      return;
    }

    const show = Boolean(waiting && me);
    el.readyAvatarSection.classList.toggle("hidden", !show);
    if (!show) {
      el.readyAvatarPicker.innerHTML = "";
      return;
    }

    const isHost = me.playerId === hostId;
    const locked = Boolean(!me.connected || (me.isReady && !isHost));
    const currentAvatarId = normalizeAvatarId(me.avatarId, state.profileAvatarId);
    const choices = [];

    for (let avatarId = 1; avatarId <= 8; avatarId += 1) {
      const selected = avatarId === currentAvatarId;
      choices.push(`
        <button
          class="avatar-choice ${selected ? "selected" : ""}"
          type="button"
          data-avatar-id="${avatarId}"
          aria-label="เลือก Avatar ${avatarId}"
          ${locked ? "disabled" : ""}
        >
          <img src="${avatarSrc(avatarId)}" alt="Avatar ${avatarId}">
        </button>
      `);
    }

    el.readyAvatarPicker.innerHTML = choices.join("");
    if (el.readyAvatarHint) {
      if (!me.connected) {
        el.readyAvatarHint.textContent = "กำลังออฟไลน์: ยังเปลี่ยนไม่ได้";
      } else if (locked) {
        el.readyAvatarHint.textContent = "ยกเลิกพร้อมก่อน แล้วค่อยเปลี่ยน Avatar";
      } else if (isHost) {
        el.readyAvatarHint.textContent = "หัวห้องเปลี่ยนได้ตลอดก่อนกดเริ่มเกม";
      } else {
        el.readyAvatarHint.textContent = "เลือกได้จนกว่าจะกดพร้อม";
      }
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
