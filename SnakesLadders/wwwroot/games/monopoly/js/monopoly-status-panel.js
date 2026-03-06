(() => {
  const root = window.SNL;
  const { state, el } = root;

  const uiState = {
    open: false,
  };

  let wired = false;

  function init() {
    if (wired) {
      return;
    }

    wired = true;
    el.monopolyStatusFab?.addEventListener("click", () => {
      uiState.open = !uiState.open;
      render();
    });

    el.monopolyStatusPopup?.addEventListener("click", (event) => {
      if (event.target.closest("[data-action='close-status']")) {
        uiState.open = false;
        render();
      }
    });
  }

  function render() {
    const helpers = root.monopolyHelpers;
    const started =
      state.room?.status === root.GAME_STATUS.STARTED &&
      helpers?.isMonopolyRoom?.(state.room);

    if (!started || !helpers || !el.monopolyStatusFab || !el.monopolyStatusPopup || !el.monopolyStatusPanel) {
      uiState.open = false;
      el.monopolyStatusFab?.classList.add("hidden");
      el.monopolyStatusPopup?.classList.add("hidden");
      if (el.monopolyStatusPanel) {
        el.monopolyStatusPanel.innerHTML = "";
      }
      return;
    }

    el.monopolyStatusFab.classList.remove("hidden");

    if (!uiState.open) {
      el.monopolyStatusPopup.classList.add("hidden");
      return;
    }

    const monopoly = helpers.getMonopolyState();
    if (!monopoly) {
      el.monopolyStatusPopup.classList.add("hidden");
      return;
    }

    const economyRows = Array.isArray(monopoly.playerEconomy)
      ? monopoly.playerEconomy
      : [];
    const economyById = new Map(economyRows.map((row) => [row.playerId, row]));

    const playerCards = (state.room?.players ?? []).map((player) => {
      const economy = economyById.get(player.playerId) ?? {};
      const classes = ["mono-status-card"];
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
          <div class="mono-status-card-head">
            <strong>${escapeHtml(player.displayName)}</strong>
            <span class="mono-status-money">${money(economy.cash ?? player.cash ?? 0)}</span>
          </div>
          <div class="mono-status-grid">
            <span>มูลค่าสุทธิ: <b>${money(economy.netWorth ?? 0)}</b></span>
            <span>มูลค่าทรัพย์สิน: <b>${money(economy.assetValue ?? 0)}</b></span>
            <span>จำนวนทรัพย์สิน: <b>${intVal(economy.propertyCount)}</b></span>
            <span>จำนวนชุดสีครบ: <b>${intVal(economy.monopolySetCount)}</b></span>
            <span>บ้าน: <b>${intVal(economy.houses)}</b></span>
            <span>โรงแรม: <b>${intVal(economy.hotels)}</b></span>
            <span>จำนอง: <b>${intVal(economy.mortgaged)}</b></span>
            <span>ติดคุก: <b>${economy.inJail ? "ใช่" : "ไม่"}</b></span>
          </div>
        </article>
      `;
    });

    const debtBanner = monopoly.pendingDebtAmount > 0
      ? `<div class="mono-status-debt">หนี้คงค้าง: ${money(monopoly.pendingDebtAmount)} ถึง ${escapeHtml(helpers.playerName(monopoly.pendingDebtToPlayerId))}</div>`
      : "";

    el.monopolyStatusPanel.innerHTML = `
      <div class="mono-status-pop-head">
        <strong>สเตตัสผู้เล่น</strong>
        <button type="button" class="btn mono-status-close" data-action="close-status">ปิด</button>
      </div>
      <div class="mono-status-overview">
        <span>บ้านคงเหลือ: <b>${intVal(monopoly.availableHouses)}</b></span>
        <span>โรงแรมคงเหลือ: <b>${intVal(monopoly.availableHotels)}</b></span>
      </div>
      ${debtBanner}
      <div class="mono-status-cards">${playerCards.join("")}</div>
    `;

    el.monopolyStatusPopup.classList.remove("hidden");
  }

  function reset() {
    uiState.open = false;
    el.monopolyStatusFab?.classList.add("hidden");
    el.monopolyStatusPopup?.classList.add("hidden");
    if (el.monopolyStatusPanel) {
      el.monopolyStatusPanel.innerHTML = "";
    }
  }

  function intVal(value) {
    return Number.parseInt(String(value ?? "0"), 10) || 0;
  }

  function money(value) {
    const formatter = root.monopolyHelpers?.money;
    if (typeof formatter === "function") {
      return formatter(value);
    }

    const amount = intVal(value);
    const abs = Math.abs(amount).toLocaleString("th-TH");
    return amount < 0 ? `-฿${abs}` : `฿${abs}`;
  }

  function escapeHtml(input) {
    return root.utils.escapeHtml(String(input ?? ""));
  }

  root.monopolyStatusPanel = {
    init,
    render,
    reset,
  };
})();
