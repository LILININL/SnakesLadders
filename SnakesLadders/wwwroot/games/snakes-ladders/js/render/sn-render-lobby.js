(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml, shortId, gameLabel, normalizeGameKey } = root.utils;

  function setConnectionStatus(text, tone) {
    el.connectionStatus.textContent = text;
    el.connectionStatus.className = `status-chip ${tone ?? ""}`.trim();
  }

  function renderNameModal() {
    el.nameModal.classList.toggle("hidden", !state.requireNamePrompt);
  }

  function renderCreatePanel() {
    el.createPanel.classList.toggle("hidden", !state.createPanelVisible);
    el.showCreatePanelBtn.classList.toggle("hidden", state.createPanelVisible);
    el.hideCreatePanelBtn.classList.toggle("hidden", !state.createPanelVisible);

    const games = getAvailableGames();
    renderCreateGameList(games);

    const selected = resolveSelectedCreateGame(games);
    const selectingGame = !selected;
    if (el.createGameListShell) {
      el.createGameListShell.classList.toggle("hidden", !selectingGame);
    }
    if (el.createForm) {
      el.createForm.classList.toggle("hidden", selectingGame);
    }

    if (!selected) {
      if (el.gameKey) {
        el.gameKey.value = "";
      }
      if (el.selectedCreateGameTitle) {
        el.selectedCreateGameTitle.textContent = "เลือกเกมก่อนสร้างห้อง";
      }
      if (el.createSubmitBtn) {
        el.createSubmitBtn.textContent = "เปิดห้อง";
      }
      return;
    }

    if (el.gameKey) {
      el.gameKey.value = selected.gameKey;
    }
    if (el.selectedCreateGameTitle) {
      el.selectedCreateGameTitle.textContent = `เกม: ${selected.displayName}`;
    }
    if (el.createSubmitBtn) {
      el.createSubmitBtn.textContent = `สร้างห้อง ${selected.displayName}`;
    }
  }

  function getAvailableGames() {
    const games = Array.isArray(state.availableGames) ? state.availableGames : [];
    if (games.length > 0) {
      return games.map((game) => ({
        gameKey: normalizeGameKey(game.gameKey),
        displayName: String(game.displayName ?? gameLabel(game.gameKey)),
        description: String(game.description ?? ""),
        isAvailable: Boolean(game.isAvailable ?? true),
      }));
    }

    return [
      {
        gameKey: normalizeGameKey(root.GAME_KEYS?.SNAKES_LADDERS ?? "snakes-ladders"),
        displayName: gameLabel(root.GAME_KEYS?.SNAKES_LADDERS),
        description: "เกมบันไดงูแบบเรียลไทม์ ปรับกติกาได้",
        isAvailable: true,
      },
    ];
  }

  function resolveSelectedCreateGame(games) {
    const selectedKey = normalizeGameKey(state.createGameKey, "");
    if (!selectedKey) {
      state.createGameKey = "";
      return null;
    }
    const selected = games.find(
      (game) =>
        normalizeGameKey(game.gameKey) === selectedKey &&
        game.isAvailable,
    );
    if (!selected) {
      state.createGameKey = "";
      return null;
    }

    state.createGameKey = normalizeGameKey(selected.gameKey);
    return selected;
  }

  function renderCreateGameList(games) {
    if (!el.createGameList) {
      return;
    }

    const rows = games.map((game) => {
      const gameKey = normalizeGameKey(game.gameKey);
      const available = Boolean(game.isAvailable);
      const title = escapeHtml(String(game.displayName ?? gameLabel(gameKey)));
      const desc = escapeHtml(String(game.description ?? ""));
      const disabled = available ? "" : "disabled";
      const stateClass = available ? "is-available" : "is-unavailable";
      const cta = available ? "สร้างห้องเกมนี้" : "ยังไม่พร้อมใช้งาน";
      return `
        <button type="button" class="game-create-card ${stateClass}" data-game-key="${escapeHtml(gameKey)}" ${disabled}>
          <span class="game-create-title">${title}</span>
          <span class="game-create-desc">${desc}</span>
          <span class="game-create-cta">${escapeHtml(cta)}</span>
        </button>
      `;
    });

    el.createGameList.innerHTML = rows.join("");
  }

  function renderLobbyOnline() {
    const list = Array.isArray(state.lobbyOnlineUsers)
      ? state.lobbyOnlineUsers
      : [];
    el.onlineUsersCount.textContent = `ออนไลน์ ${list.length} คน`;

    if (list.length === 0) {
      el.onlineUsersList.innerHTML =
        "<li class='room-item'><div class='meta'>ตอนนี้ยังไม่มีใครออนไลน์</div></li>";
      return;
    }

    const rows = list.map((user) => {
      const location = user.roomCode
        ? `อยู่ในห้อง: ${escapeHtml(user.roomCode)}`
        : "อยู่ที่ล็อบบี้";
      return `
        <li class="room-item">
          <strong>${escapeHtml(user.displayName)}</strong>
          <div class="meta">การเชื่อมต่อ: ${escapeHtml(shortId(user.connectionId))} | ${location}</div>
        </li>
      `;
    });

    el.onlineUsersList.innerHTML = rows.join("");
  }

  function renderWaitingRooms() {
    const rooms = Array.isArray(state.waitingRooms) ? state.waitingRooms : [];
    const currentRoomCode = state.room?.roomCode ?? "";

    renderWaitingRoomList(
      el.waitingRoomList,
      rooms,
      currentRoomCode,
      "ยังไม่มีห้องที่เปิดรอ",
    );
    renderWaitingRoomList(
      el.mainWaitingRoomList,
      rooms,
      currentRoomCode,
      "ตอนนี้ยังไม่มีห้องที่เปิดรอ ลองกดสร้างห้องแรกได้เลย",
    );
  }

  function renderWaitingRoomList(target, rooms, currentRoomCode, emptyMessage) {
    if (!target) {
      return;
    }

    if (rooms.length === 0) {
      target.innerHTML = `<li class='room-item'><div class='meta'>${escapeHtml(emptyMessage)}</div></li>`;
      return;
    }

    const rows = rooms.map((room) => {
      const isCurrent = room.roomCode === currentRoomCode;
      const gameName = escapeHtml(gameLabel(room.gameKey));
      const boardSizeValue = Number.parseInt(String(room.boardSize ?? ""), 10);
      const boardSize = Number.isFinite(boardSizeValue) ? boardSizeValue : "-";
      return `
        <li class="room-item">
          <strong>${escapeHtml(room.roomCode)}</strong> โดย ${escapeHtml(room.hostName)}
          <div class="meta">เกม: ${gameName} | ผู้เล่น: ${room.playerCount} | กระดาน: ${boardSize}</div>
          <button class="btn join-room-btn" data-room-code="${escapeHtml(room.roomCode)}" ${isCurrent ? "disabled" : ""}>
            ${isCurrent ? "ห้องที่คุณอยู่" : "เข้าห้องนี้"}
          </button>
        </li>
      `;
    });

    target.innerHTML = rows.join("");
  }

  root.renderLobby = {
    setConnectionStatus,
    renderNameModal,
    renderCreatePanel,
    renderLobbyOnline,
    renderWaitingRooms,
  };
})();
