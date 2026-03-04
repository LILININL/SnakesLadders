(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { normalizeName, normalizeAvatarId } = root.utils;

  function seedProfileFromStorage() {
    const saved = root.storage.loadProfileName();
    if (saved) {
      applyProfileName(saved);
      state.requireNamePrompt = false;
    } else {
      state.requireNamePrompt = true;
      el.profileNameInput.value = "";
    }

    const savedAvatarId = root.storage.loadProfileAvatarId();
    applyProfileAvatarId(savedAvatarId);
  }

  async function onSaveProfileName(event) {
    event.preventDefault();
    const name = normalizeName(el.profileNameInput.value);
    if (!name) {
      root.feedback.logEvent("กรุณาตั้งชื่อก่อนเริ่มเล่น", true);
      return;
    }

    applyProfileName(name);
    state.requireNamePrompt = false;
    await root.realtime.publishLobbyName();
    await root.api.refreshLobbyOnline();
    root.feedback.renderAll();
  }

  function ensureProfileName(inputName) {
    const name = normalizeName(inputName) || normalizeName(state.profileName);
    if (!name) {
      state.requireNamePrompt = true;
      root.feedback.renderAll();
      return "";
    }

    applyProfileName(name);
    root.realtime.publishLobbyName();
    return name;
  }

  function ensureProfileAvatarId(inputAvatarId) {
    const avatarId = normalizeAvatarId(inputAvatarId, state.profileAvatarId);
    applyProfileAvatarId(avatarId);
    return avatarId;
  }

  function applyProfileName(name) {
    state.profileName = normalizeName(name);
    root.storage.saveProfileName(state.profileName);

    el.profileNameInput.value = state.profileName;
    el.createName.value = state.profileName;
    el.joinName.value = state.profileName;
  }

  function applyProfileAvatarId(avatarId) {
    const safeAvatarId = normalizeAvatarId(avatarId, 1);
    state.profileAvatarId = safeAvatarId;
    root.storage.saveProfileAvatarId(safeAvatarId);

    if (el.createAvatarId) {
      el.createAvatarId.value = String(safeAvatarId);
    }
    syncAvatarPickerSelection(el.createAvatarPicker, safeAvatarId);
    if (el.joinAvatarId) {
      el.joinAvatarId.value = String(safeAvatarId);
    }
  }

  function syncAvatarPickerSelection(container, avatarId) {
    if (!container) {
      return;
    }

    const safeAvatarId = normalizeAvatarId(avatarId, 1);
    for (const button of container.querySelectorAll("[data-avatar-id]")) {
      const buttonId = normalizeAvatarId(button.dataset.avatarId, 1);
      button.classList.toggle("selected", buttonId === safeAvatarId);
      button.setAttribute(
        "aria-selected",
        buttonId === safeAvatarId ? "true" : "false",
      );
    }
  }

  function syncProfileAvatarFromRoom(room, playerId) {
    if (!room || !playerId) {
      return;
    }

    const me = room.players?.find((x) => x.playerId === playerId);
    if (!me) {
      return;
    }

    applyProfileAvatarId(me.avatarId);
  }

  function buildBoardOptions(boardSize) {
    const gameMode = parseInt(el.gameMode?.value ?? "1", 10);
    const classicMode = gameMode === 0;
    const chaosMode = gameMode === 2;

    const ruleOptions = {
      itemsEnabled: Boolean(el.ruleItemsEnabled?.checked),
      checkpointShieldEnabled: el.ruleCheckpointShield.checked,
      comebackBoostEnabled: el.ruleComebackBoost.checked,
      luckyRerollEnabled: false,
      luckyRerollPerPlayer: 0,
      forkPathEnabled: false,
      snakeFrenzyEnabled: el.ruleSnakeFrenzy.checked,
      snakeFrenzyIntervalTurns: parseInt(el.frenzyInterval.value, 10),
      mercyLadderEnabled: el.ruleMercyLadder.checked,
      mercyLadderBoost: parseInt(el.mercyBoost.value, 10),
      checkpointInterval: parseInt(el.checkpointInterval.value, 10),
      turnTimerEnabled: el.ruleTurnTimer.checked,
      turnSeconds: parseInt(el.turnSeconds.value, 10),
      roundLimitEnabled: el.ruleRoundLimit.checked,
      maxRounds: parseInt(el.maxRounds.value, 10),
      marathonSpeedupEnabled: el.ruleMarathonSpeedup.checked,
      marathonThreshold: parseInt(el.marathonThreshold.value, 10),
      marathonLadderMultiplier: parseFloat(el.marathonMultiplier.value),
    };

    if (classicMode) {
      ruleOptions.itemsEnabled = false;
      ruleOptions.checkpointShieldEnabled = false;
      ruleOptions.comebackBoostEnabled = false;
      ruleOptions.snakeFrenzyEnabled = false;
      ruleOptions.mercyLadderEnabled = false;
      ruleOptions.turnTimerEnabled = false;
      ruleOptions.roundLimitEnabled = false;
      ruleOptions.marathonSpeedupEnabled = false;
    } else if (chaosMode) {
      applyChaosRuleProfile(ruleOptions);
    }

    return {
      boardSize,
      gameMode,
      densityMode: 0,
      overflowMode: parseInt(el.overflowMode.value, 10),
      ruleOptions,
    };
  }

  function applyChaosRuleProfile(ruleOptions) {
    ruleOptions.itemsEnabled = true;
    ruleOptions.checkpointShieldEnabled = false;
    ruleOptions.comebackBoostEnabled = false;
    ruleOptions.luckyRerollEnabled = false;
    ruleOptions.luckyRerollPerPlayer = 0;
    ruleOptions.forkPathEnabled = false;
    ruleOptions.snakeFrenzyEnabled = true;
    ruleOptions.snakeFrenzyIntervalTurns = 4;
    ruleOptions.mercyLadderEnabled = false;
    ruleOptions.turnTimerEnabled = true;
    ruleOptions.turnSeconds = 12;
    ruleOptions.roundLimitEnabled = true;
    ruleOptions.maxRounds = 90;
    ruleOptions.marathonSpeedupEnabled = false;
  }

  root.session = {
    seedProfileFromStorage,
    onSaveProfileName,
    ensureProfileName,
    ensureProfileAvatarId,
    applyProfileAvatarId,
    syncProfileAvatarFromRoom,
    buildBoardOptions,
  };
})();
