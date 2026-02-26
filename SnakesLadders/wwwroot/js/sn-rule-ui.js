(() => {
  const root = window.SNL;
  const { el } = root;

  const settings = [
    { ruleId: "ruleTurnTimer", settingId: "settingTurnSeconds" },
    { ruleId: "ruleRoundLimit", settingId: "settingMaxRounds" },
    { ruleId: "ruleSnakeFrenzy", settingId: "settingFrenzyInterval" },
    { ruleId: "ruleCheckpointShield", settingId: "settingCheckpointInterval" },
    { ruleId: "ruleMercyLadder", settingId: "settingMercyBoost" },
    { ruleId: "ruleMarathonSpeedup", settingId: "settingMarathonThreshold" },
    { ruleId: "ruleMarathonSpeedup", settingId: "settingMarathonMultiplier" }
  ];

  function init() {
    if (el.gameMode) {
      el.gameMode.addEventListener("change", syncAll);
    }

    for (const item of settings) {
      const rule = document.getElementById(item.ruleId);
      if (rule) {
        rule.addEventListener("change", syncAll);
      }
    }

    syncAll();
  }

  function syncAll() {
    const customMode = isCustomMode();
    if (el.customRuleOptionsBlock) {
      el.customRuleOptionsBlock.classList.toggle("hidden", !customMode);
    }

    if (!customMode) {
      hideAllSettings();
      return;
    }

    for (const item of settings) {
      syncSetting(item);
    }
  }

  function hideAllSettings() {
    for (const item of settings) {
      const setting = document.getElementById(item.settingId);
      if (!setting) {
        continue;
      }

      setting.classList.add("hidden");
      for (const input of setting.querySelectorAll("input, select")) {
        input.disabled = true;
      }
    }
  }

  function syncSetting(item) {
    const rule = document.getElementById(item.ruleId);
    const setting = document.getElementById(item.settingId);
    if (!rule || !setting) {
      return;
    }

    const enabled = Boolean(rule.checked);
    setting.classList.toggle("hidden", !enabled);
    for (const input of setting.querySelectorAll("input, select")) {
      input.disabled = !enabled;
    }
  }

  function isCustomMode() {
    const mode = Number.parseInt(String(el.gameMode?.value ?? "1"), 10);
    return mode !== 0;
  }

  root.ruleUi = {
    init,
    syncAll
  };
})();
