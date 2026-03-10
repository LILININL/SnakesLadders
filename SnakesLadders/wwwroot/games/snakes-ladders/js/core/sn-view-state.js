(() => {
  const root = window.SNL;
  const { state } = root;

  function normalizeRoomCode(room) {
    return String(room?.roomCode ?? room?.RoomCode ?? "")
      .trim()
      .toUpperCase();
  }

  function resolveRoomSnapshotRevision(room) {
    const parsed = Number.parseInt(
      String(room?.snapshotRevision ?? room?.SnapshotRevision ?? 0),
      10,
    );
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
  }

  function rememberLatestRevision(revision) {
    state.latestRoomSnapshotRevision = Math.max(
      Number.parseInt(String(state.latestRoomSnapshotRevision ?? 0), 10) || 0,
      Number.parseInt(String(revision ?? 0), 10) || 0,
    );
  }

  function acceptRoomSnapshot(room, options = {}) {
    if (!room) {
      return false;
    }

    const force = Boolean(options.force);
    const revision = resolveRoomSnapshotRevision(room);
    const incomingRoomCode = normalizeRoomCode(room);
    const currentRoomCode = normalizeRoomCode(state.room);
    const roomChanged =
      Boolean(incomingRoomCode) &&
      Boolean(currentRoomCode) &&
      incomingRoomCode !== currentRoomCode;

    if (force || roomChanged) {
      state.roomSnapshotRevision = 0;
      state.deferredRoomSnapshotRevision = 0;
      state.latestRoomSnapshotRevision = 0;
      state.deferredRoom = null;
    }

    const baseline = Math.max(
      Number.parseInt(String(state.roomSnapshotRevision ?? 0), 10) || 0,
      Number.parseInt(String(state.latestRoomSnapshotRevision ?? 0), 10) || 0,
    );

    if (!force && roomChanged) {
      return false;
    }

    if (!force && revision > 0 && revision < baseline) {
      return false;
    }

    state.room = room;
    state.roomSnapshotRevision = revision;
    rememberLatestRevision(revision);
    return true;
  }

  function stageDeferredRoom(room, options = {}) {
    if (!room) {
      return false;
    }

    const force = Boolean(options.force);
    const revision = resolveRoomSnapshotRevision(room);
    const incomingRoomCode = normalizeRoomCode(room);
    const currentRoomCode = normalizeRoomCode(state.room);
    const roomChanged =
      Boolean(incomingRoomCode) &&
      Boolean(currentRoomCode) &&
      incomingRoomCode !== currentRoomCode;
    const baseline = Math.max(
      Number.parseInt(String(state.roomSnapshotRevision ?? 0), 10) || 0,
      Number.parseInt(String(state.deferredRoomSnapshotRevision ?? 0), 10) || 0,
      Number.parseInt(String(state.latestRoomSnapshotRevision ?? 0), 10) || 0,
    );

    if (!force && roomChanged) {
      return false;
    }

    if (!force && revision > 0 && revision < baseline) {
      return false;
    }

    state.deferredRoom = room;
    state.deferredRoomSnapshotRevision = revision;
    rememberLatestRevision(revision);
    return true;
  }

  function clearDeferredRoom() {
    state.deferredRoom = null;
    state.deferredRoomSnapshotRevision = 0;
  }

  function resetRoomSnapshots() {
    state.room = null;
    clearDeferredRoom();
    state.roomSnapshotRevision = 0;
    state.latestRoomSnapshotRevision = 0;
  }

  function getEffectiveDeadlineUtc() {
    return state.deferredRoom?.turnDeadlineUtc ?? state.room?.turnDeadlineUtc ?? null;
  }

  function getDisplayTurnPlayerId() {
    if (!state.room) {
      return "";
    }

    if (state.animating && state.animTurnPlayerId) {
      return state.animTurnPlayerId;
    }

    return state.room.currentTurnPlayerId ?? "";
  }

  function getPlayerPosition(playerId) {
    if (!state.room || !playerId) {
      return null;
    }

    if (state.animating && state.animPlayerId === playerId) {
      return state.animPlayerPosition;
    }

    const player = state.room.players.find((x) => x.playerId === playerId);
    return player ? player.position : null;
  }

  function getDisplayPlayers() {
    if (!state.room) {
      return [];
    }

    return state.room.players.map((player) => ({
      ...player,
      position: getPlayerPosition(player.playerId) ?? player.position,
    }));
  }

  root.viewState = {
    acceptRoomSnapshot,
    clearDeferredRoom,
    getDisplayTurnPlayerId,
    getEffectiveDeadlineUtc,
    getPlayerPosition,
    getDisplayPlayers,
    normalizeRoomCode,
    resetRoomSnapshots,
    resolveRoomSnapshotRevision,
    stageDeferredRoom,
  };
})();
