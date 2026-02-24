(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { normalizeName } = root.utils;

  function seedProfileFromStorage() {
    const saved = root.storage.loadProfileName();
    if (saved) {
      applyProfileName(saved);
      state.requireNamePrompt = false;
    } else {
      state.requireNamePrompt = true;
      el.profileNameInput.value = "";
    }
  }

  async function onSaveProfileName(event) {
    event.preventDefault();
    const name = normalizeName(el.profileNameInput.value);
    if (!name) {
      root.feedback.logEvent("Please set your name first.", true);
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

  function applyProfileName(name) {
    state.profileName = normalizeName(name);
    root.storage.saveProfileName(state.profileName);

    el.profileNameInput.value = state.profileName;
    el.createName.value = state.profileName;
    el.joinName.value = state.profileName;
  }

  function buildBoardOptions(boardSize) {
    return {
      boardSize,
      densityMode: parseInt(el.densityMode.value, 10),
      overflowMode: parseInt(el.overflowMode.value, 10),
      ruleOptions: {
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
        marathonSpeedupEnabled: el.ruleMarathonSpeedup.checked
      }
    };
  }

  root.session = {
    seedProfileFromStorage,
    onSaveProfileName,
    ensureProfileName,
    buildBoardOptions
  };
})();
