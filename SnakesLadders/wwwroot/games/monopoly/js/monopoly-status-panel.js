(() => {
  const root = window.SNL;
  const { state, el } = root;

  function render() {
    const helpers = root.monopolyHelpers;
    const monopoly = helpers?.getMonopolyState?.();
    const started = state.room?.status === root.GAME_STATUS.STARTED;
    if (!started || !helpers?.isMonopolyRoom?.() || !monopoly || !el.monopolyStatusPanel) {
      if (el.monopolyStatusPanel) {
        el.monopolyStatusPanel.innerHTML = "";
      }
      return;
    }

    const economyById = new Map(
      (Array.isArray(monopoly.playerEconomy) ? monopoly.playerEconomy : []).map((entry) => [
        entry.playerId,
        entry,
      ]),
    );

    const rows = (state.room?.players ?? []).map((player) => {
      const economy = economyById.get(player.playerId) ?? {};
      const classes = ["mono-player-card"];
      if (player.playerId === state.playerId) {
        classes.push("me");
      }
      if (player.playerId === monopoly.activePlayerId) {
        classes.push("active");
      }
      if (player.playerId === monopoly.pendingDecisionPlayerId) {
        classes.push("pending");
      }
      if (economy.isBankrupt || player.isBankrupt) {
        classes.push("bankrupt");
      }

      return `
        <article class="${classes.join(" ")}">
          <div class="mono-player-head">
            <strong>${escapeHtml(player.displayName)}</strong>
            <span class="mono-money">${money(economy.cash ?? player.cash ?? 0)}</span>
          </div>
          <div class="mono-player-grid">
            <span>Net: <b>${money(economy.netWorth ?? 0)}</b></span>
            <span>Assets: <b>${money(economy.assetValue ?? 0)}</b></span>
            <span>Property: <b>${intVal(economy.propertyCount)}</b></span>
            <span>Sets: <b>${intVal(economy.monopolySetCount)}</b></span>
            <span>Houses: <b>${intVal(economy.houses)}</b></span>
            <span>Hotels: <b>${intVal(economy.hotels)}</b></span>
            <span>Mortgaged: <b>${intVal(economy.mortgaged)}</b></span>
            <span>Jail: <b>${economy.inJail ? "Yes" : "No"}</b></span>
          </div>
        </article>
      `;
    });

    const debtLine = monopoly.pendingDebtAmount > 0
      ? `<div class="mono-global-info">Debt Pending: ${money(monopoly.pendingDebtAmount)} to ${escapeHtml(helpers.playerName(monopoly.pendingDebtToPlayerId))}</div>`
      : "";

    el.monopolyStatusPanel.innerHTML = `
      <div class="mono-status-head">
        <strong>Economy Status</strong>
        <span class="meta-chip">Houses ${intVal(monopoly.availableHouses)} | Hotels ${intVal(monopoly.availableHotels)}</span>
      </div>
      ${debtLine}
      <div class="mono-status-list">${rows.join("")}</div>
    `;
  }

  function intVal(value) {
    return Number.parseInt(String(value ?? 0), 10) || 0;
  }

  function money(value) {
    return root.monopolyHelpers?.money?.(value) ?? `$${intVal(value)}`;
  }

  function escapeHtml(input) {
    return root.utils.escapeHtml(String(input ?? ""));
  }

  root.monopolyStatusPanel = {
    render,
  };
})();
