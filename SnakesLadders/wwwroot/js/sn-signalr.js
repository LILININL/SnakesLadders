(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { normalizeName } = root.utils;

  async function setupConnection() {
    state.connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/game")
      .withAutomaticReconnect()
      .build();

    state.connection.onreconnecting(() => {
      root.renderLobby.setConnectionStatus("Reconnecting...", "warning");
    });

    state.connection.onreconnected(async () => {
      root.renderLobby.setConnectionStatus("Connected", "online");
      await publishLobbyName();
      if (!state.roomCode) {
        await root.api.refreshLobbyOnline();
      }

      if (state.roomCode && state.sessionId) {
        await invokeHub("ResumeRoom", {
          roomCode: state.roomCode,
          sessionId: state.sessionId,
          playerName: state.profileName
        }, true);
      }
    });

    state.connection.onclose(() => {
      root.renderLobby.setConnectionStatus("Disconnected", "error");
    });

    state.connection.on("Error", (message) => {
      root.feedback.logEvent(`Server: ${message}`, true);
    });

    state.connection.on("RoomCreated", (payload) => bindRoomPayload(payload, "Room created"));
    state.connection.on("RoomJoined", (payload) => bindRoomPayload(payload, "Joined room"));
    state.connection.on("RoomResumed", (payload) => bindRoomPayload(payload, "Resumed room"));

    state.connection.on("RoomUpdated", (room) => {
      if (state.animating) {
        state.deferredRoom = room;
        return;
      }

      state.room = room;
      root.feedback.renderAll();
    });

    state.connection.on("GameStarted", (room) => {
      state.room = room;
      root.feedback.logEvent("Game started.");
      root.feedback.renderAll();
    });

    state.connection.on("TurnChanged", (playerId) => {
      if (state.animating) {
        return;
      }

      const player = state.room?.players?.find((x) => x.playerId === playerId);
      root.feedback.logEvent(`Turn: ${player ? player.displayName : playerId}`);
      root.feedback.renderAll();
    });

    state.connection.on("DiceRolled", async (payload) => {
      root.feedback.logEvent(root.feedback.formatTurnLine(payload.turn));
      await root.turnAnimation.queue(payload);
    });

    state.connection.on("GameFinished", (payload) => {
      root.feedback.logEvent(`Game finished: winner ${payload.turn.winnerPlayerId ?? "-"}`);
      if (state.animating) {
        state.deferredRoom = payload.room;
      } else {
        state.lastTurn = payload.turn;
        state.room = payload.room;
        root.feedback.renderAll();
      }
    });

    state.connection.on("ChatReceived", (message) => {
      if (!state.roomCode || message.roomCode !== state.roomCode) {
        return;
      }
      root.renderChat.addChatMessage(message);
    });

    await state.connection.start();
    root.renderLobby.setConnectionStatus("Connected", "online");
    await publishLobbyName();
  }

  async function invokeHub(methodName, payload, suppressError = false) {
    try {
      await state.connection.invoke(methodName, payload);
      return true;
    } catch (error) {
      if (!suppressError) {
        root.feedback.logEvent(`${methodName} failed: ${error.message ?? String(error)}`, true);
      }
      return false;
    }
  }

  async function publishLobbyName() {
    const name = normalizeName(state.profileName);
    if (!name || !state.connection || state.connection.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    await invokeHub("SetLobbyName", name, true);
  }

  function bindRoomPayload(payload, label) {
    state.roomCode = payload.roomCode;
    state.playerId = payload.playerId;
    state.sessionId = payload.sessionId;
    state.room = payload.room;
    state.lastTurn = null;
    state.chatMessages = [];
    state.rollButtonHidden = false;
    state.animating = false;
    state.animPlayerId = "";
    state.animPlayerPosition = 1;
    state.animTurnPlayerId = "";
    state.animTransitActive = false;
    state.animTransitPlayerId = "";
    state.deferredRoom = null;
    root.turnAnimation?.reset?.();
    root.boardFx?.reset?.();

    el.joinRoomCode.value = payload.roomCode;
    root.storage.saveRoomSession(payload.roomCode, payload.sessionId, payload.playerId);
    root.feedback.logEvent(`${label}: ${payload.roomCode}`);

    root.feedback.renderAll();
  }

  root.realtime = {
    setupConnection,
    invokeHub,
    publishLobbyName
  };
})();
