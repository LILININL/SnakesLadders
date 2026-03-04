(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml, shortId } = root.utils;

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
      return `
        <li class="room-item">
          <strong>${escapeHtml(room.roomCode)}</strong> โดย ${escapeHtml(room.hostName)}
          <div class="meta">ผู้เล่น: ${room.playerCount} | กระดาน: ${room.boardSize}</div>
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
