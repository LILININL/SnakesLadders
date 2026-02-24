(() => {
  const root = window.SNL;
  const { state } = root;

  async function refreshWaitingRooms(force = false) {
    if (!force && isInRoom()) {
      return;
    }

    const rooms = await fetchJson("/rooms/waiting");
    if (!rooms) {
      return;
    }

    state.waitingRooms = Array.isArray(rooms) ? rooms : [];
    root.renderLobby.renderWaitingRooms();
  }

  async function refreshLobbyOnline(force = false) {
    if (!force && isInRoom()) {
      return;
    }

    const users = await fetchJson("/lobby/online");
    if (!users) {
      return;
    }

    state.lobbyOnlineUsers = Array.isArray(users) ? users : [];
    root.renderLobby.renderLobbyOnline();
  }

  async function fetchJson(path) {
    try {
      const response = await fetch(path, {
        headers: { Accept: "application/json" }
      });

      if (!response.ok) {
        return null;
      }

      return await response.json();
    } catch {
      return null;
    }
  }

  function isInRoom() {
    return Boolean(state.roomCode && state.room);
  }

  root.api = {
    refreshWaitingRooms,
    refreshLobbyOnline
  };
})();
