(() => {
  const root = window.SNL;
  const { state, el } = root;

  function wireForms() {
    el.nameForm.addEventListener("submit", root.session.onSaveProfileName);

    el.showCreatePanelBtn.addEventListener("click", () => {
      state.createPanelVisible = true;
      root.renderLobby.renderCreatePanel();
    });

    el.hideCreatePanelBtn.addEventListener("click", () => {
      state.createPanelVisible = false;
      root.renderLobby.renderCreatePanel();
    });

    el.createForm.addEventListener("submit", onCreateRoom);
    el.joinForm.addEventListener("submit", onJoinRoom);
    if (el.waitingRoomList) {
      el.waitingRoomList.addEventListener("click", onJoinFromRoomList);
    }
    if (el.mainWaitingRoomList) {
      el.mainWaitingRoomList.addEventListener("click", onJoinFromRoomList);
    }

    if (el.refreshRoomsBtn) {
      el.refreshRoomsBtn.addEventListener("click", refreshLobbyLists);
    }
    if (el.refreshRoomsMainBtn) {
      el.refreshRoomsMainBtn.addEventListener("click", refreshLobbyLists);
    }

    el.startGameBtn.addEventListener("click", () => {
      if (!state.roomCode) return;
      if (!root.readyUi.canStartGame()) {
        return;
      }
      root.realtime.invokeHub("StartGame", { roomCode: state.roomCode });
    });

    el.refreshRoomBtn.addEventListener("click", () => {
      if (!state.roomCode) return;
      root.realtime.invokeHub("GetRoom", state.roomCode);
    });

    el.rollDiceFloatingBtn.addEventListener("click", onRollDice);
    el.toggleRollBtn.addEventListener("click", () =>
      root.roomUi.toggleRollButton(),
    );
    el.chatFabBtn.addEventListener("click", () =>
      root.roomUi.toggleChatPanel(),
    );
    el.toggleReadyBtn.addEventListener("click", onToggleReady);
    el.readyAvatarPicker?.addEventListener("click", onPickWaitingAvatar);

    el.leaveRoomBtn.addEventListener("click", onLeaveRoom);

    el.chatForm.addEventListener("submit", onSendChat);
    el.clearChatBtn.addEventListener("click", () =>
      root.renderChat.clearChat(),
    );
  }

  async function onCreateRoom(event) {
    event.preventDefault();

    const name = root.session.ensureProfileName(el.createName.value);
    if (!name) return;

    const boardSize = parseInt(el.boardSize.value, 10);
    if (!Number.isFinite(boardSize) || boardSize < 50) {
      root.feedback.logEvent("ขนาดกระดานต้องอย่างน้อย 50 ช่อง", true);
      return;
    }

    state.createPanelVisible = false;
    root.renderLobby.renderCreatePanel();

    const avatarId = root.session.ensureProfileAvatarId();

    await root.realtime.invokeHub("CreateRoom", {
      playerName: name,
      avatarId,
      boardOptions: root.session.buildBoardOptions(boardSize),
    });
  }

  async function onJoinRoom(event) {
    event.preventDefault();

    const roomCode = String(el.joinRoomCode.value ?? "")
      .trim()
      .toUpperCase();
    if (!roomCode) {
      root.feedback.logEvent("กรุณาใส่รหัสห้องก่อนเข้าร่วม", true);
      return;
    }

    await joinRoomCode(roomCode);
  }

  async function onJoinFromRoomList(event) {
    const button = event.target.closest(".join-room-btn");
    if (!button) return;

    const roomCode = String(button.dataset.roomCode ?? "")
      .trim()
      .toUpperCase();
    if (!roomCode) return;

    el.joinRoomCode.value = roomCode;
    await joinRoomCode(roomCode);
  }

  async function joinRoomCode(roomCode) {
    const name = root.session.ensureProfileName(el.joinName.value);
    if (!name) return;
    const avatarId = root.session.ensureProfileAvatarId();

    const session = root.storage.getRoomSession(roomCode);
    await root.realtime.invokeHub("JoinRoom", {
      roomCode,
      playerName: name,
      avatarId,
      sessionId: session?.sessionId ?? null,
    });
  }

  async function onLeaveRoom() {
    if (!state.roomCode) return;

    await root.realtime.invokeHub("LeaveRoom", { roomCode: state.roomCode });
    state.roomCode = "";
    state.playerId = "";
    state.sessionId = "";
    state.room = null;
    state.lastTurn = null;
    state.rollButtonHidden = false;
    state.chatPanelOpen = false;
    state.chatUnreadCount = 0;
    state.animating = false;
    state.animPlayerId = "";
    state.animPlayerPosition = 1;
    state.animTurnPlayerId = "";
    state.animFrenzySnake = null;
    state.animTransitActive = false;
    state.animTransitPlayerId = "";
    state.deferredRoom = null;
    state.pendingTurnChangedPlayerId = "";
    state.lastAnnouncedTurnCounter = -1;
    state.pendingBeaconTargetPlayerId = "";
    state.pageTransitioning = false;
    state.pageTransitionDirection = 0;
    root.turnAnimation?.reset?.();
    root.boardFx?.reset?.();
    root.boardFocus?.clearState?.();
    root.boardBeacon?.hide?.();

    root.renderChat.clearChat();
    await root.api.refreshLobbyOnline();
    await root.api.refreshWaitingRooms();
    root.feedback.renderAll();
  }

  async function onSendChat(event) {
    event.preventDefault();
    if (!state.roomCode || !state.room) return;

    const message = String(el.chatInput.value ?? "").trim();
    if (!message) return;

    const sent = await root.realtime.invokeHub("SendChat", {
      roomCode: state.roomCode,
      message,
    });

    if (sent) {
      el.chatInput.value = "";
    }
  }

  async function onRollDice() {
    if (!state.roomCode) return;

    await root.realtime.invokeHub("RollDice", {
      roomCode: state.roomCode,
      useLuckyReroll: false,
      forkChoice: null,
    });
  }

  async function onToggleReady() {
    if (
      !state.roomCode ||
      !state.room ||
      state.room.status !== root.GAME_STATUS.WAITING
    ) {
      return;
    }

    const me = state.room.players.find((x) => x.playerId === state.playerId);
    if (!me || me.playerId === state.room.hostPlayerId) {
      return;
    }

    await root.realtime.invokeHub("SetReady", {
      roomCode: state.roomCode,
      isReady: !me.isReady,
    });
  }

  async function onPickWaitingAvatar(event) {
    const button = event.target.closest("[data-avatar-id]");
    if (!button || button.disabled) {
      return;
    }

    if (
      !state.roomCode ||
      !state.room ||
      state.room.status !== root.GAME_STATUS.WAITING
    ) {
      return;
    }

    const me = state.room.players.find((x) => x.playerId === state.playerId);
    if (!me) {
      return;
    }

    const isHost = me.playerId === state.room.hostPlayerId;
    if (me.isReady && !isHost) {
      root.feedback.logEvent("ยกเลิกพร้อมก่อน แล้วค่อยเปลี่ยน Avatar");
      return;
    }

    const avatarId = root.utils.normalizeAvatarId(
      button.dataset.avatarId,
      me.avatarId,
    );
    if (avatarId === me.avatarId) {
      return;
    }

    await root.realtime.invokeHub("SetAvatar", {
      roomCode: state.roomCode,
      avatarId,
    });
  }

  async function refreshLobbyLists() {
    await root.api.refreshLobbyOnline();
    await root.api.refreshWaitingRooms();
  }

  root.actions = { wireForms };
})();
