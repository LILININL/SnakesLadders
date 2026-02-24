(() => {
  const root = window.SNL;
  const { state } = root;

  init().catch((error) => {
    root.renderLobby.setConnectionStatus("Failed to initialize", "error");
    root.feedback.logEvent(error.message ?? String(error), true);
  });

  async function init() {
    root.actions.wireForms();
    root.session.seedProfileFromStorage();
    root.feedback.renderAll();

    await root.realtime.setupConnection();
    await root.api.refreshLobbyOnline();
    await root.api.refreshWaitingRooms();

    await tryResumeLastRoom();

    setInterval(pollLobbyLists, 8000);

    root.feedback.renderAll();
  }

  async function tryResumeLastRoom() {
    if (state.requireNamePrompt || !state.profileName) {
      return;
    }

    const last = root.storage.getLastRoomSession();
    if (!last) {
      return;
    }

    const resumed = await root.realtime.invokeHub("ResumeRoom", {
      roomCode: last.roomCode,
      sessionId: last.sessionId,
      playerName: state.profileName
    }, true);

    if (!resumed) {
      root.storage.clearRoomSession(last.roomCode);
    }
  }

  async function pollLobbyLists() {
    if (state.roomCode) {
      return;
    }

    await root.api.refreshLobbyOnline();
    await root.api.refreshWaitingRooms();
  }
})();
