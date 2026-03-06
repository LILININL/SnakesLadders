(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml, avatarSrc, normalizeAvatarId } = root.utils;
  const viewCache = {
    readyListSignature: "",
    readyAvatarPickerSignature: "",
    readyAvatarHintText: "",
  };

  function render() {
    const waiting = isWaitingRoom();
    el.readyPanel.classList.toggle("hidden", !waiting);
    if (!waiting) {
      viewCache.readyListSignature = "";
      viewCache.readyAvatarPickerSignature = "";
      viewCache.readyAvatarHintText = "";
      if (el.readyList) {
        el.readyList.innerHTML = "";
      }
      if (el.readyAvatarPicker) {
        el.readyAvatarPicker.innerHTML = "";
      }
      return;
    }

    const hostId = state.room.hostPlayerId;
    const players = state.room.players;
    const readyCount = players.filter((x) =>
      x.playerId === hostId ? true : x.connected && x.isReady,
    ).length;
    el.readySummary.textContent = `พร้อม ${readyCount}/${players.length} คน`;

    renderReadyList(players, hostId);

    const me = state.room.players.find((x) => x.playerId === state.playerId);
    renderAvatarPicker(waiting, me, hostId);
    const canToggle = Boolean(me && me.playerId !== hostId && me.connected);
    el.toggleReadyBtn.classList.toggle("hidden", !canToggle);
    if (canToggle) {
      el.toggleReadyBtn.textContent = me.isReady
        ? "ขอยังไม่พร้อม"
        : "ฉันพร้อมแล้ว";
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
      viewCache.readyAvatarPickerSignature = "";
      viewCache.readyAvatarHintText = "";
      return;
    }

    const isHost = me.playerId === hostId;
    const locked = Boolean(!me.connected || (me.isReady && !isHost));
    const currentAvatarId = normalizeAvatarId(
      me.avatarId,
      state.profileAvatarId,
    );
    const pickerSignature = [
      me.playerId,
      currentAvatarId,
      locked ? 1 : 0,
      isHost ? 1 : 0,
      me.connected ? 1 : 0,
      me.isReady ? 1 : 0,
    ].join("|");

    if (pickerSignature !== viewCache.readyAvatarPickerSignature) {
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
      viewCache.readyAvatarPickerSignature = pickerSignature;
    }
    if (el.readyAvatarHint) {
      let hintText = "";
      if (!me.connected) {
        hintText = "กำลังออฟไลน์: ยังเปลี่ยนไม่ได้";
      } else if (locked) {
        hintText = "ยกเลิกพร้อมก่อน แล้วค่อยเปลี่ยน Avatar";
      } else if (isHost) {
        hintText = "หัวห้องเปลี่ยนได้ตลอดก่อนกดเริ่มเกม";
      } else {
        hintText = "เลือกได้จนกว่าจะกดพร้อม";
      }

      if (hintText !== viewCache.readyAvatarHintText) {
        el.readyAvatarHint.textContent = hintText;
        viewCache.readyAvatarHintText = hintText;
      }
    }
  }

  function renderReadyList(players, hostId) {
    if (!el.readyList) {
      return;
    }

    const signature = players
      .map((player) =>
        [
          player.playerId,
          player.displayName,
          normalizeAvatarId(player.avatarId, 1),
          player.connected ? 1 : 0,
          player.isReady ? 1 : 0,
          player.playerId === hostId ? 1 : 0,
        ].join(":"),
      )
      .join("|");

    if (signature === viewCache.readyListSignature) {
      return;
    }

    el.readyList.innerHTML = players
      .map((player) => {
        const host = player.playerId === hostId;
        const tone = host
          ? "host"
          : player.connected
            ? player.isReady
              ? "ready"
              : "not-ready"
            : "offline";
        const label = host
          ? "หัวห้อง"
          : tone === "ready"
            ? "พร้อม"
            : tone === "offline"
              ? "ออฟไลน์"
              : "ยังไม่พร้อม";
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
      })
      .join("");
    viewCache.readyListSignature = signature;
  }

  function amHost() {
    return Boolean(
      state.room &&
      state.playerId &&
      state.room.hostPlayerId === state.playerId,
    );
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
    return Boolean(
      state.roomCode && state.room && state.room.status === GAME_STATUS.WAITING,
    );
  }

  root.readyUi = {
    render,
    amHost,
    canStartGame,
  };
})();
