(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const {
    botDifficultyClass,
    botDifficultyLabel,
    botPersonalityClass,
    botPersonalityLabel,
    escapeHtml,
    normalizeAvatarId,
    syncAvatarHost,
  } = root.utils;
  const viewCache = {
    readyAvatarHintText: "",
    readyAvatarPickerKey: "",
    readyListRows: new Map(),
  };

  function render() {
    const waiting = isWaitingRoom();
    el.readyPanel.classList.toggle("hidden", !waiting);
    if (!waiting) {
      viewCache.readyAvatarHintText = "";
      viewCache.readyAvatarPickerKey = "";
      viewCache.readyListRows.clear();
      if (el.readyBotControls) {
        el.readyBotControls.classList.add("hidden");
      }
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
    if (el.readyBotControls) {
      el.readyBotControls.classList.toggle("hidden", !amHost());
    }

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
      viewCache.readyAvatarPickerKey = "";
      viewCache.readyAvatarHintText = "";
      return;
    }

    const isHost = me.playerId === hostId;
    const locked = Boolean(!me.connected || (me.isReady && !isHost));
    const currentAvatarId = normalizeAvatarId(
      me.avatarId,
      state.profileAvatarId,
    );
    const pickerKey = [
      me.playerId,
      locked ? 1 : 0,
      isHost ? 1 : 0,
      me.connected ? 1 : 0,
      me.isReady ? 1 : 0,
    ].join("|");

    if (pickerKey !== viewCache.readyAvatarPickerKey) {
      el.readyAvatarPicker.replaceChildren();
      for (let avatarId = 1; avatarId <= 11; avatarId += 1) {
        const button = document.createElement("button");
        button.className = "avatar-choice";
        button.type = "button";
        button.dataset.avatarId = String(avatarId);
        button.setAttribute("aria-label", `เลือก Avatar ${avatarId}`);

        const visual = document.createElement("span");
        visual.className = "avatar-choice-host";
        button.appendChild(visual);
        syncAvatarHost(visual, avatarId, {
          className: "avatar-choice-visual",
          alt: `Avatar ${avatarId}`,
          variant: "picker",
        });

        el.readyAvatarPicker.appendChild(button);
      }
      viewCache.readyAvatarPickerKey = pickerKey;
    }

    for (const button of el.readyAvatarPicker.querySelectorAll("[data-avatar-id]")) {
      const avatarId = normalizeAvatarId(button.dataset.avatarId, 1);
      const selected = avatarId === currentAvatarId;
      button.classList.toggle("selected", selected);
      button.toggleAttribute("disabled", locked);
      button.setAttribute("aria-pressed", selected ? "true" : "false");
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

    const fragment = document.createDocumentFragment();
    const seenIds = new Set();

    for (const player of players) {
      const playerId = String(player.playerId ?? "");
      if (!playerId) {
        continue;
      }

      seenIds.add(playerId);
      let row = viewCache.readyListRows.get(playerId);
      if (!row) {
        row = createReadyRow();
        viewCache.readyListRows.set(playerId, row);
      }

      syncReadyRow(row, player, hostId);
      fragment.appendChild(row);
    }

    for (const [playerId, row] of viewCache.readyListRows.entries()) {
      if (seenIds.has(playerId)) {
        continue;
      }
      row.remove();
      viewCache.readyListRows.delete(playerId);
    }

    el.readyList.replaceChildren(fragment);
  }

  function createReadyRow() {
    const row = document.createElement("li");
    row.className = "ready-item";

    const nameWrap = document.createElement("span");
    nameWrap.className = "name-wrap";

    const avatarHost = document.createElement("span");
    avatarHost.className = "ready-avatar-host";

    const name = document.createElement("span");
    name.className = "name";

    const metaWrap = document.createElement("span");
    metaWrap.className = "ready-item-meta";

    const badgeList = document.createElement("span");
    badgeList.className = "ready-badge-list";

    const pill = document.createElement("span");
    pill.className = "ready-pill";

    const removeBtn = document.createElement("button");
    removeBtn.className = "btn ready-remove-bot-btn hidden";
    removeBtn.type = "button";
    removeBtn.textContent = "ลบ AI";

    nameWrap.append(avatarHost, name);
    metaWrap.append(badgeList, pill, removeBtn);
    row.append(nameWrap, metaWrap);
    return row;
  }

  function syncReadyRow(row, player, hostId) {
    const viewerIsHost = state.playerId === hostId;
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
    const avatarHost = row.querySelector(".ready-avatar-host");
    const name = row.querySelector(".name");
    const badgeList = row.querySelector(".ready-badge-list");
    const pill = row.querySelector(".ready-pill");
    const removeBtn = row.querySelector(".ready-remove-bot-btn");

    row.className = `ready-item ${tone}${player.isBot ? " bot-player" : ""}`;
    name.textContent = String(player.displayName ?? "");
    badgeList.innerHTML = [
      player.isBot ? '<span class="inline-pill bot">AI</span>' : "",
      player.isBot
        ? `<span class="inline-pill bot-difficulty ${escapeHtml(botDifficultyClass(player.botDifficulty))}">${escapeHtml(botDifficultyLabel(player.botDifficulty))}</span>`
        : "",
      player.isBot
        ? `<span class="inline-pill personality ${escapeHtml(botPersonalityClass(player.botPersonality))}">${escapeHtml(botPersonalityLabel(player.botPersonality))}</span>`
        : "",
      player.isBot &&
      player.activeBotPersonality != null &&
      player.activeBotPersonality !== player.botPersonality
        ? `<span class="inline-pill personality-active ${escapeHtml(botPersonalityClass(player.activeBotPersonality))}">ตอนนี้ ${escapeHtml(botPersonalityLabel(player.activeBotPersonality))}</span>`
        : "",
      !player.isBot && player.fullAutoEnabled
        ? '<span class="inline-pill auto">Full Auto</span>'
        : "",
    ].join("");
    pill.className = `ready-pill ${tone}`;
    pill.textContent = label;
    syncAvatarHost(avatarHost, safeAvatarId, {
      className: "inline-avatar",
      alt: `Avatar ${safeAvatarId}`,
      variant: "inline",
    });

    if (removeBtn) {
      const canRemove = Boolean(viewerIsHost && player.isBot);
      removeBtn.classList.toggle("hidden", !canRemove);
      if (canRemove) {
        removeBtn.dataset.removeBotPlayerId = String(player.playerId ?? "");
      } else {
        delete removeBtn.dataset.removeBotPlayerId;
      }
    }
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
