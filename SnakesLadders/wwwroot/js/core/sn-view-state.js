(() => {
  const root = window.SNL;
  const { state } = root;

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
      position: getPlayerPosition(player.playerId) ?? player.position
    }));
  }

  root.viewState = {
    getDisplayTurnPlayerId,
    getPlayerPosition,
    getDisplayPlayers
  };
})();
