(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml, shortId, densityLabel } = root.utils;

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
    const list = Array.isArray(state.lobbyOnlineUsers) ? state.lobbyOnlineUsers : [];
    el.onlineUsersCount.textContent = `${list.length} online`;

    if (list.length === 0) {
      el.onlineUsersList.innerHTML = "<li class='room-item'><div class='meta'>No users online.</div></li>";
      return;
    }

    const rows = list.map((user) => {
      const location = user.roomCode ? `Room: ${escapeHtml(user.roomCode)}` : "In Lobby";
      return `
        <li class="room-item">
          <strong>${escapeHtml(user.displayName)}</strong>
          <div class="meta">Connection: ${escapeHtml(shortId(user.connectionId))} | ${location}</div>
        </li>
      `;
    });

    el.onlineUsersList.innerHTML = rows.join("");
  }

  function renderWaitingRooms() {
    const rooms = Array.isArray(state.waitingRooms) ? state.waitingRooms : [];
    if (rooms.length === 0) {
      el.waitingRoomList.innerHTML = "<li class='room-item'><div class='meta'>No open rooms.</div></li>";
      return;
    }

    const currentRoomCode = state.room?.roomCode ?? "";
    const rows = rooms.map((room) => {
      const isCurrent = room.roomCode === currentRoomCode;
      return `
        <li class="room-item">
          <strong>${escapeHtml(room.roomCode)}</strong> by ${escapeHtml(room.hostName)}
          <div class="meta">Players: ${room.playerCount} | Board: ${room.boardSize} | Density: ${densityLabel(room.densityMode)}</div>
          <button class="btn join-room-btn" data-room-code="${escapeHtml(room.roomCode)}" ${isCurrent ? "disabled" : ""}>
            ${isCurrent ? "Current Room" : "Join This Room"}
          </button>
        </li>
      `;
    });

    el.waitingRoomList.innerHTML = rows.join("");
  }

  root.renderLobby = {
    setConnectionStatus,
    renderNameModal,
    renderCreatePanel,
    renderLobbyOnline,
    renderWaitingRooms
  };
})();
