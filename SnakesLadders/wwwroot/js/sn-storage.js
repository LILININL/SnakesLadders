(() => {
  const root = window.SNL;
  const { normalizeName } = root.utils;

  const keys = {
    profileName: "snl_profile_name",
    roomSessions: "snl_room_sessions",
    lastRoomCode: "snl_last_room_code",
    focusMode: "snl_focus_mode"
  };

  function loadProfileName() {
    return normalizeName(localStorage.getItem(keys.profileName) ?? "");
  }

  function saveProfileName(name) {
    const normalized = normalizeName(name);
    if (!normalized) {
      localStorage.removeItem(keys.profileName);
      return;
    }
    localStorage.setItem(keys.profileName, normalized);
  }

  function loadSessionMap() {
    try {
      const raw = localStorage.getItem(keys.roomSessions);
      if (!raw) {
        return {};
      }
      const parsed = JSON.parse(raw);
      return parsed && typeof parsed === "object" ? parsed : {};
    } catch {
      return {};
    }
  }

  function saveSessionMap(map) {
    localStorage.setItem(keys.roomSessions, JSON.stringify(map));
  }

  function saveRoomSession(roomCode, sessionId, playerId) {
    const code = String(roomCode ?? "").trim().toUpperCase();
    if (!code || !sessionId) {
      return;
    }

    const map = loadSessionMap();
    map[code] = {
      sessionId: String(sessionId),
      playerId: String(playerId ?? ""),
      savedAtUtc: new Date().toISOString()
    };

    saveSessionMap(map);
    localStorage.setItem(keys.lastRoomCode, code);
  }

  function getRoomSession(roomCode) {
    const code = String(roomCode ?? "").trim().toUpperCase();
    if (!code) {
      return null;
    }

    const map = loadSessionMap();
    const entry = map[code];
    if (!entry || !entry.sessionId) {
      return null;
    }

    return {
      roomCode: code,
      sessionId: String(entry.sessionId),
      playerId: String(entry.playerId ?? "")
    };
  }

  function getLastRoomSession() {
    const code = String(localStorage.getItem(keys.lastRoomCode) ?? "").trim().toUpperCase();
    if (!code) {
      return null;
    }
    return getRoomSession(code);
  }

  function clearRoomSession(roomCode) {
    const code = String(roomCode ?? "").trim().toUpperCase();
    if (!code) {
      return;
    }

    const map = loadSessionMap();
    delete map[code];
    saveSessionMap(map);

    const last = String(localStorage.getItem(keys.lastRoomCode) ?? "").trim().toUpperCase();
    if (last === code) {
      localStorage.removeItem(keys.lastRoomCode);
    }
  }

  function loadFocusMode() {
    const value = String(localStorage.getItem(keys.focusMode) ?? "").trim().toLowerCase();
    return value === "turn" ? "turn" : "me";
  }

  function saveFocusMode(mode) {
    const value = mode === "turn" ? "turn" : "me";
    localStorage.setItem(keys.focusMode, value);
  }

  root.storage = {
    loadProfileName,
    saveProfileName,
    saveRoomSession,
    getRoomSession,
    getLastRoomSession,
    clearRoomSession,
    loadFocusMode,
    saveFocusMode
  };
})();
