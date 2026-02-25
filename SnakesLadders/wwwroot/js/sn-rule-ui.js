(() => {
  const root = window.SNL;

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
    for (const item of settings) {
      const rule = document.getElementById(item.ruleId);
      if (rule) {
        rule.addEventListener("change", syncAll);
      }
    }

    syncAll();
  }

  function syncAll() {
    for (const item of settings) {
      syncSetting(item);
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

  root.ruleUi = {
    init,
    syncAll
  };
})();
