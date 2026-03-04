(() => {
  const root = window.SNL;
  const { state, el } = root;

  let wired = false;

  function wire() {
    if (wired || !el.monopolyActionPanel) {
      return;
    }

    wired = true;
    el.monopolyActionPanel.addEventListener("click", onActionClick);
    el.monopolyActionPanel.addEventListener("submit", onActionSubmit);
  }

  function reset() {
    if (el.monopolyActionPanel) {
      el.monopolyActionPanel.innerHTML = "";
    }
  }

  function render() {
    const helpers = root.monopolyHelpers;
    const monopoly = helpers?.getMonopolyState?.();
    const inMonopolyRoom = Boolean(
      state.room &&
      state.room.status === root.GAME_STATUS.STARTED &&
      helpers?.isMonopolyRoom?.(),
    );

    if (!inMonopolyRoom || !monopoly || !el.monopolyHud || !el.monopolyActionPanel) {
      el.monopolyHud?.classList.add("hidden");
      reset();
      return;
    }

    el.monopolyHud.classList.remove("hidden");

    const phase = helpers.phase();
    const phaseLabel = helpers.phaseLabel(phase);
    const activePlayerName = helpers.playerName(monopoly.activePlayerId);
    const pendingPlayerName = helpers.playerName(monopoly.pendingDecisionPlayerId);
    const mine = helpers.isMyDecisionTurn();

    const head = `
      <div class="mono-panel-head">
        <strong>Monopoly Action</strong>
        <span class="meta-chip">Phase: ${phaseLabel}</span>
      </div>
      <div class="mono-phase-meta">Active: <strong>${escapeHtml(activePlayerName)}</strong> | Pending: <strong>${escapeHtml(pendingPlayerName)}</strong></div>
    `;

    const body = resolvePhaseBody(phase, monopoly, mine);
    el.monopolyActionPanel.innerHTML = `${head}<div class="mono-panel-body">${body}</div>`;
  }

  function resolvePhaseBody(phase, monopoly, mine) {
    switch (phase) {
      case root.MONOPOLY_PHASE.AWAIT_ROLL:
        return mine
          ? renderBigActionButtons([
            {
              label: "Roll 2 Dice",
              action: "roll",
              tone: "primary",
            },
          ])
          : "<div class='mono-muted'>รอผู้เล่นปัจจุบันทอยเต๋า</div>";

      case root.MONOPOLY_PHASE.AWAIT_JAIL_DECISION:
        return mine
          ? renderBigActionButtons([
            { label: "Pay Jail Fine ($50)", action: "pay-jail", tone: "primary" },
            { label: "Try Jail Roll", action: "try-jail-roll", tone: "" },
          ])
          : "<div class='mono-muted'>รอผู้เล่นในคุกตัดสินใจ</div>";

      case root.MONOPOLY_PHASE.AWAIT_PURCHASE_DECISION:
        return renderPurchaseDecision(monopoly, mine);

      case root.MONOPOLY_PHASE.AUCTION_IN_PROGRESS:
        return renderAuctionPanel(monopoly, mine);

      case root.MONOPOLY_PHASE.AWAIT_TRADE_RESPONSE:
        return renderTradeResponse(monopoly, mine);

      case root.MONOPOLY_PHASE.AWAIT_MANAGE:
      case root.MONOPOLY_PHASE.AWAIT_END_TURN:
        return mine
          ? renderManagePanel()
          : "<div class='mono-muted'>รอผู้เล่นปัจจุบันจัดการทรัพย์สิน</div>";

      case root.MONOPOLY_PHASE.RESOLVING:
        return "<div class='mono-muted'>กำลังคำนวณผลของแอคชั่น...</div>";

      default:
        return "<div class='mono-muted'>รอการอัปเดตสถานะ</div>";
    }
  }

  function renderPurchaseDecision(monopoly, mine) {
    const helpers = root.monopolyHelpers;
    const pendingCell = helpers.findCell(monopoly.pendingPurchaseCellId);
    const cellName = pendingCell?.name ?? pendingCell?.Name ?? "ทรัพย์สิน";
    const price = Number.parseInt(String(pendingCell?.price ?? pendingCell?.Price ?? 0), 10) || 0;

    if (!mine) {
      return `<div class='mono-muted'>รอผู้เล่นตัดสินใจซื้อ ${escapeHtml(cellName)}</div>`;
    }

    return `
      <div class="mono-info-block">
        <div class="mono-title">${escapeHtml(cellName)}</div>
        <div class="mono-sub">ราคา ${helpers.money(price)}</div>
      </div>
      ${renderBigActionButtons([
        { label: "Buy Property", action: "buy-property", tone: "primary" },
        { label: "Decline & Start Auction", action: "decline-property", tone: "danger" },
      ])}
    `;
  }

  function renderAuctionPanel(monopoly, mine) {
    const auction = monopoly.activeAuction;
    if (!auction) {
      return "<div class='mono-muted'>ยังไม่มีข้อมูลประมูล</div>";
    }

    const helpers = root.monopolyHelpers;
    const bidderName = helpers.playerName(auction.currentBidderPlayerId);
    const info = `
      <div class="mono-info-block">
        <div class="mono-title">Auction: ${escapeHtml(auction.cellName)}</div>
        <div class="mono-sub">Current Bid: <strong>${helpers.money(auction.currentBidAmount)}</strong> by ${escapeHtml(bidderName)}</div>
      </div>
    `;

    if (!mine) {
      return `${info}<div class='mono-muted'>รอผู้เล่นที่ถึงคิวบิด/ผ่าน</div>`;
    }

    return `
      ${info}
      <form class="mono-inline-form" data-action="bid-auction">
        <label>Bid Amount</label>
        <input name="bidAmount" type="number" min="1" step="1" placeholder="เช่น 120">
        <button type="submit" class="btn btn-primary mono-action-btn">Submit Bid</button>
      </form>
      ${renderBigActionButtons([
        { label: "Pass Auction", action: "pass-auction", tone: "" },
      ])}
    `;
  }

  function renderTradeResponse(monopoly, mine) {
    const trade = monopoly.activeTradeOffer;
    if (!trade) {
      return "<div class='mono-muted'>ยังไม่มีข้อเสนอเทรด</div>";
    }

    const helpers = root.monopolyHelpers;
    const giveCells = Array.isArray(trade.giveCells) ? trade.giveCells.join(", ") : "-";
    const receiveCells = Array.isArray(trade.receiveCells)
      ? trade.receiveCells.join(", ")
      : "-";

    const info = `
      <div class="mono-info-block">
        <div class="mono-title">Trade Offer</div>
        <div class="mono-sub">From: ${escapeHtml(helpers.playerName(trade.fromPlayerId))} → ${escapeHtml(helpers.playerName(trade.toPlayerId))}</div>
        <div class="mono-sub">Cash Give: ${helpers.money(trade.cashGive)} | Cash Receive: ${helpers.money(trade.cashReceive)}</div>
        <div class="mono-sub">Give Cells: ${escapeHtml(giveCells || "-")}</div>
        <div class="mono-sub">Receive Cells: ${escapeHtml(receiveCells || "-")}</div>
      </div>
    `;

    if (!mine) {
      return `${info}<div class='mono-muted'>รอผู้เล่นเป้าหมายตอบรับหรือปฏิเสธ</div>`;
    }

    return `${info}${renderBigActionButtons([
      { label: "Accept Trade", action: "accept-trade", tone: "primary" },
      { label: "Reject Trade", action: "reject-trade", tone: "danger" },
    ])}`;
  }

  function renderManagePanel() {
    const helpers = root.monopolyHelpers;
    const ownedCells = helpers
      .resolveCells()
      .filter((cell) => ownerId(cell) === state.playerId);

    const buildable = ownedCells.filter((cell) => resolveType(cell) === 1);
    const mortgaged = ownedCells.filter((cell) => Boolean(cell?.isMortgaged ?? cell?.IsMortgaged));
    const nonMortgaged = ownedCells.filter((cell) => !Boolean(cell?.isMortgaged ?? cell?.IsMortgaged));

    return `
      <div class="mono-manage-grid">
        ${renderCellSelectForm("build-house", "Build House / Hotel", buildable)}
        ${renderCellSelectForm("sell-house", "Sell House / Hotel", buildable)}
        ${renderCellSelectForm("mortgage", "Mortgage", nonMortgaged)}
        ${renderCellSelectForm("unmortgage", "Unmortgage", mortgaged)}
      </div>
      ${renderTradeOfferForm()}
      ${renderBigActionButtons([
        { label: "Declare Bankruptcy", action: "declare-bankruptcy", tone: "danger" },
        { label: "End Turn", action: "end-turn", tone: "primary" },
      ])}
    `;
  }

  function renderTradeOfferForm() {
    const players = (state.room?.players ?? []).filter(
      (player) => player.playerId !== state.playerId && !player.isBankrupt,
    );

    const options = players
      .map(
        (player) =>
          `<option value="${escapeHtml(player.playerId)}">${escapeHtml(player.displayName)}</option>`,
      )
      .join("");

    return `
      <form class="mono-trade-form" data-action="offer-trade">
        <div class="mono-form-head">Offer Trade</div>
        <label>Target Player</label>
        <select name="targetPlayerId">${options}</select>
        <label>Cash Give</label>
        <input name="cashGive" type="number" min="0" step="1" value="0">
        <label>Cash Receive</label>
        <input name="cashReceive" type="number" min="0" step="1" value="0">
        <label>Give Cells (comma)</label>
        <input name="giveCells" type="text" placeholder="เช่น 2,4,12">
        <label>Receive Cells (comma)</label>
        <input name="receiveCells" type="text" placeholder="เช่น 27,29">
        <button type="submit" class="btn mono-action-btn">Send Trade Offer</button>
      </form>
    `;
  }

  function renderCellSelectForm(action, title, cells) {
    const options = cells
      .map((cell) => {
        const cellNo = resolveCellNo(cell);
        const name = String(cell?.name ?? cell?.Name ?? `Cell ${cellNo}`);
        return `<option value="${cellNo}">${cellNo} - ${escapeHtml(name)}</option>`;
      })
      .join("");

    return `
      <form class="mono-inline-form" data-action="${action}">
        <label>${escapeHtml(title)}</label>
        <select name="cellId">${options}</select>
        <button type="submit" class="btn mono-action-btn">Apply</button>
      </form>
    `;
  }

  function renderBigActionButtons(buttons) {
    const rows = buttons
      .map((button) => {
        const toneClass = button.tone ? `mono-btn-${button.tone}` : "";
        return `<button type="button" class="btn mono-action-btn ${toneClass}" data-action="${button.action}">${escapeHtml(button.label)}</button>`;
      })
      .join("");

    return `<div class="mono-action-list">${rows}</div>`;
  }

  async function onActionClick(event) {
    const button = event.target.closest("[data-action]");
    if (!button) {
      return;
    }

    const action = String(button.dataset.action ?? "").trim();
    if (!action) {
      return;
    }

    await submitMappedAction(action, {});
  }

  async function onActionSubmit(event) {
    const form = event.target.closest("form[data-action]");
    if (!form) {
      return;
    }

    event.preventDefault();
    const action = String(form.dataset.action ?? "").trim();
    const formData = new FormData(form);
    const payload = Object.fromEntries(formData.entries());

    await submitMappedAction(action, payload);
  }

  async function submitMappedAction(action, payload) {
    const helpers = root.monopolyHelpers;
    if (!helpers || !helpers.isMonopolyRoom()) {
      return;
    }

    switch (action) {
      case "roll":
        await helpers.submitAction(root.GAME_ACTION_TYPES.ROLL_DICE, {});
        return;
      case "pay-jail":
        await helpers.submitAction(root.GAME_ACTION_TYPES.PAY_JAIL_FINE, {});
        return;
      case "try-jail-roll":
        await helpers.submitAction(root.GAME_ACTION_TYPES.TRY_JAIL_ROLL, {});
        return;
      case "buy-property":
        await helpers.submitAction(root.GAME_ACTION_TYPES.BUY_PROPERTY, {});
        return;
      case "decline-property":
        await helpers.submitAction(root.GAME_ACTION_TYPES.DECLINE_PURCHASE, {});
        return;
      case "pass-auction":
        await helpers.submitAction(root.GAME_ACTION_TYPES.PASS_AUCTION, {});
        return;
      case "accept-trade":
        await helpers.submitAction(root.GAME_ACTION_TYPES.ACCEPT_TRADE, {});
        return;
      case "reject-trade":
        await helpers.submitAction(root.GAME_ACTION_TYPES.REJECT_TRADE, {});
        return;
      case "declare-bankruptcy":
        await helpers.submitAction(root.GAME_ACTION_TYPES.DECLARE_BANKRUPTCY, {});
        return;
      case "end-turn":
        await helpers.submitAction(root.GAME_ACTION_TYPES.END_TURN, {});
        return;
      case "bid-auction": {
        const bid = Number.parseInt(String(payload.bidAmount ?? ""), 10);
        if (!Number.isFinite(bid) || bid <= 0) {
          root.feedback.logEvent("กรุณาใส่ราคา Bid ที่ถูกต้อง", true);
          return;
        }

        await helpers.submitAction(root.GAME_ACTION_TYPES.BID_AUCTION, {
          bidAmount: bid,
        });
        return;
      }
      case "build-house":
      case "sell-house":
      case "mortgage":
      case "unmortgage": {
        const cellId = Number.parseInt(String(payload.cellId ?? ""), 10);
        if (!Number.isFinite(cellId) || cellId <= 0) {
          root.feedback.logEvent("กรุณาเลือกช่องทรัพย์สินก่อน", true);
          return;
        }

        const actionMap = {
          "build-house": root.GAME_ACTION_TYPES.BUILD_HOUSE,
          "sell-house": root.GAME_ACTION_TYPES.SELL_HOUSE,
          mortgage: root.GAME_ACTION_TYPES.MORTGAGE,
          unmortgage: root.GAME_ACTION_TYPES.UNMORTGAGE,
        };

        await helpers.submitAction(actionMap[action], {
          cellId,
        });
        return;
      }
      case "offer-trade": {
        const targetPlayerId = String(payload.targetPlayerId ?? "").trim();
        if (!targetPlayerId) {
          root.feedback.logEvent("กรุณาเลือกผู้เล่นเป้าหมาย", true);
          return;
        }

        const cashGive = Number.parseInt(String(payload.cashGive ?? "0"), 10) || 0;
        const cashReceive = Number.parseInt(String(payload.cashReceive ?? "0"), 10) || 0;
        const giveCells = helpers.parseCellList(payload.giveCells);
        const receiveCells = helpers.parseCellList(payload.receiveCells);

        await helpers.submitAction(root.GAME_ACTION_TYPES.OFFER_TRADE, {
          targetPlayerId,
          tradeOffer: {
            cashGive: Math.max(0, cashGive),
            cashReceive: Math.max(0, cashReceive),
            giveCells,
            receiveCells,
          },
        });
        return;
      }
      default:
        return;
    }
  }

  function ownerId(cell) {
    return String(cell?.ownerPlayerId ?? cell?.OwnerPlayerId ?? "").trim();
  }

  function resolveType(cell) {
    return Number.parseInt(String(cell?.type ?? cell?.Type ?? 0), 10) || 0;
  }

  function resolveCellNo(cell) {
    return Number.parseInt(String(cell?.cell ?? cell?.Cell ?? 0), 10) || 0;
  }

  function escapeHtml(input) {
    return root.utils.escapeHtml(String(input ?? ""));
  }

  root.monopolyActionPanel = {
    wire,
    reset,
    render,
  };
})();
