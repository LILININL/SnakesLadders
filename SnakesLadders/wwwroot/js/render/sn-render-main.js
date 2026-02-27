(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { formatClock } = root.utils;

  function logEvent(message, isError = false) {
    const item = document.createElement("li");
    if (isError) {
      item.classList.add("error");
    }

    item.textContent = `[${formatClock(new Date())}] ${message}`;
    el.eventFeed.prepend(item);

    while (el.eventFeed.children.length > 40) {
      el.eventFeed.removeChild(el.eventFeed.lastElementChild);
    }
  }

  function renderAll() {
    root.renderLobby.renderNameModal();
    root.renderLobby.renderCreatePanel();
    root.renderLobby.renderLobbyOnline();
    root.renderLobby.renderWaitingRooms();

    root.renderGame.renderRoomHeader();
    root.renderGame.renderPlayers();
    root.renderGame.renderBoard();
    root.renderGame.renderLastTurn();
    root.renderGame.updateActionState();
    root.roomUi.renderAll();
    root.readyUi.render();

    root.renderChat.renderChat();
  }

  function formatTurnLine(turn) {
    const name = findPlayerName(turn.playerId);
    return `${name} ทอยได้ ${turn.diceValue} (${turn.startPosition} -> ${turn.endPosition})`;
  }

  function findPlayerName(playerId) {
    if (!playerId) {
      return "-";
    }

    const player = state.room?.players.find((x) => x.playerId === playerId);
    return player ? player.displayName : playerId;
  }

  root.feedback = {
    logEvent,
    renderAll,
    formatTurnLine
  };
})();
