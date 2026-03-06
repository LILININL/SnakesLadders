(() => {
  const root = window.SNL;
  const { state, el } = root;

  const panelState = {
    manageStep: "menu",
    tradeTargetPlayerId: "",
    lastPhase: -1,
    lastTurnCounter: -1,
  };
  const COLOR_GROUP_SET_SIZE = {
    brown: 2,
    "light blue": 3,
    pink: 3,
    orange: 3,
    red: 3,
    yellow: 3,
    green: 3,
    "dark blue": 2,
  };

  let wired = false;
  let countdownTimer = 0;

  function wire() {
    if (wired || !el.monopolyActionPanel) {
      return;
    }

    wired = true;
    el.monopolyActionPanel.addEventListener("click", onPanelClick);
    el.monopolyActionPanel.addEventListener("submit", onPanelSubmit);
    el.monopolyActionPanel.addEventListener("change", onPanelChange);
    countdownTimer = window.setInterval(updateCountdownLabel, 1000);
  }

  function reset() {
    panelState.manageStep = "menu";
    panelState.tradeTargetPlayerId = "";
    panelState.lastPhase = -1;
    panelState.lastTurnCounter = -1;
    hidePopup();
    if (el.monopolyActionPanel) {
      el.monopolyActionPanel.innerHTML = "";
    }
  }

  function render() {
    const helpers = root.monopolyHelpers;
    if (!helpers?.isMonopolyRoom?.(state.room) ||
        state.room?.status !== root.GAME_STATUS.STARTED ||
        !el.monopolyActionPopup ||
        !el.monopolyActionPanel) {
      hidePopup();
      return;
    }

    const monopoly = helpers.getMonopolyState();
    if (!monopoly) {
      hidePopup();
      return;
    }

    const phase = helpers.phase();
    const turnCounter = Number.parseInt(String(state.room?.turnCounter ?? -1), 10) || 0;

    if (phase !== panelState.lastPhase || turnCounter !== panelState.lastTurnCounter) {
      if (phase === root.MONOPOLY_PHASE.AWAIT_MANAGE ||
          phase === root.MONOPOLY_PHASE.AWAIT_END_TURN) {
        panelState.manageStep = "menu";
      }
      panelState.lastPhase = phase;
      panelState.lastTurnCounter = turnCounter;
    }

    if (!shouldShowForCurrentPlayer(monopoly)) {
      hidePopup();
      return;
    }

    const title = phaseTitle(phase);
    const phaseBody = renderByPhase(phase, monopoly);
    const phaseOutcomeNotice = renderPhaseOutcomeNotice(phase);
    const phaseAdvice = renderPhaseAdvice(phase, monopoly);
    const remainSec = resolveDeadlineSec(state.room?.turnDeadlineUtc);

    el.monopolyActionPanel.innerHTML = `
      <div class="mono-pop-head">
        <div class="mono-pop-title">${escapeHtml(title)}</div>
        <div class="mono-pop-meta">${escapeHtml(renderTurnMeta(monopoly))}</div>
        ${remainSec > 0 ? `<div class="mono-pop-timer">เหลือเวลา ${remainSec} วินาที</div>` : ""}
      </div>
      ${phaseOutcomeNotice}
      <div class="mono-pop-body">${phaseBody}</div>
      ${phaseAdvice}
    `;

    el.monopolyActionPopup.classList.remove("hidden");
  }

  function renderPhaseOutcomeNotice(phase) {
    if (
      phase !== root.MONOPOLY_PHASE.AWAIT_PURCHASE_DECISION &&
      phase !== root.MONOPOLY_PHASE.AWAIT_MANAGE &&
      phase !== root.MONOPOLY_PHASE.AWAIT_END_TURN
    ) {
      return "";
    }

    const lastTurn = state.lastTurn;
    if (!lastTurn || String(lastTurn.playerId ?? "") !== String(state.playerId ?? "")) {
      return "";
    }

    const logs = Array.isArray(lastTurn.actionLogs)
      ? lastTurn.actionLogs
        .map((line) => String(line ?? "").trim())
        .filter(Boolean)
      : [];
    if (logs.length === 0) {
      return "";
    }

    const eventLine = logs.find(
      (line) => line.startsWith("โอกาส:") || line.startsWith("การ์ดชุมชน:"),
    );
    if (!eventLine) {
      return "";
    }

    const toneClass = eventLine.startsWith("โอกาส:")
      ? "mono-outcome-chance"
      : "mono-outcome-community";

    return `
      <div class="mono-outcome ${toneClass}">
        <div class="mono-outcome-label">เหตุการณ์ที่เกิดขึ้น</div>
        <div class="mono-outcome-text">${escapeHtml(eventLine)}</div>
      </div>
    `;
  }

  function shouldShowForCurrentPlayer(monopoly) {
    const helpers = root.monopolyHelpers;
    const phase = helpers.phase();

    if (phase === root.MONOPOLY_PHASE.RESOLVING || phase === root.MONOPOLY_PHASE.FINISHED) {
      return false;
    }

    if (state.animating) {
      return false;
    }

    return helpers.isMyDecisionTurn();
  }

  function renderByPhase(phase, monopoly) {
    switch (phase) {
      case root.MONOPOLY_PHASE.AWAIT_ROLL:
        return renderActionButtons([
          { label: "ทอยลูกเต๋า 2 ลูก", action: "roll", tone: "primary" },
        ]);

      case root.MONOPOLY_PHASE.AWAIT_JAIL_DECISION:
        return renderActionButtons(
          [
            currentPlayerCash(monopoly) >= 50
              ? { label: "จ่ายค่าปรับออกจากคุก (฿50)", action: "pay-jail", tone: "primary" }
              : null,
            { label: "เสี่ยงทอยดับเบิลเพื่อออกจากคุก", action: "try-jail-roll", tone: "" },
          ].filter(Boolean),
        );

      case root.MONOPOLY_PHASE.AWAIT_PURCHASE_DECISION:
        return renderPurchaseDecision(monopoly);

      case root.MONOPOLY_PHASE.AUCTION_IN_PROGRESS:
        return renderAuction(monopoly);

      case root.MONOPOLY_PHASE.AWAIT_TRADE_RESPONSE:
        return renderTradeResponse(monopoly);

      case root.MONOPOLY_PHASE.AWAIT_MANAGE:
      case root.MONOPOLY_PHASE.AWAIT_END_TURN:
        return renderManageWizard(monopoly);

      default:
        return `<div class="mono-muted">รอการอัปเดตสถานะ</div>`;
    }
  }

  function renderPurchaseDecision(monopoly) {
    const helpers = root.monopolyHelpers;
    const cell = helpers.findCell(monopoly.pendingPurchaseCellId);
    const name = cellName(cell);
    const price = parseIntVal(cell?.price ?? cell?.Price);
    const cash = currentPlayerCash(monopoly);
    const shortfall = Math.max(0, price - cash);
    const canAfford = cash >= price;
    const recommendation = canAfford
      ? "ถ้าต้องการเก็บเงินไว้จ่ายค่าเช่ารอบถัดไป ให้เลือกไม่ซื้อแล้วเปิดประมูล"
      : `เงินไม่พอซื้อ ขาดอีก ${helpers.money(shortfall)} แนะนำให้เลือกไม่ซื้อเพื่อเปิดประมูล`;
    const actions = canAfford
      ? [
        { label: "ซื้อทรัพย์สิน", action: "buy-property", tone: "primary" },
        { label: "ไม่ซื้อ (เปิดประมูล)", action: "decline-property", tone: "danger" },
      ]
      : [
        { label: "ไม่ซื้อ (เปิดประมูล)", action: "decline-property", tone: "danger" },
      ];

    return `
      <div class="mono-info-card">
        <div class="mono-info-title">${escapeHtml(name)}</div>
        <div class="mono-info-sub">ราคา ${helpers.money(price)}</div>
        <div class="mono-info-sub">เงินคงเหลือ ${helpers.money(cash)}</div>
      </div>
      ${
        canAfford
          ? ""
          : `<div class="mono-tip mono-tip-warn">เงินไม่พอสำหรับการซื้อช่องนี้ (ขาด ${helpers.money(shortfall)})</div>`
      }
      <div class="mono-tip mono-tip-reco">คำแนะนำ: ${escapeHtml(recommendation)}</div>
      ${renderActionButtons(actions)}
    `;
  }

  function renderAuction(monopoly) {
    const helpers = root.monopolyHelpers;
    const auction = monopoly.activeAuction;
    if (!auction) {
      return `<div class="mono-muted">ยังไม่มีข้อมูลประมูล</div>`;
    }

    const bidderName = helpers.playerName(auction.currentBidderPlayerId);
    const minBid = Math.max(1, parseIntVal(auction.currentBidAmount) + 1);

    return `
      <div class="mono-info-card">
        <div class="mono-info-title">ประมูล: ${escapeHtml(auction.cellName ?? `ช่อง ${auction.cellId}`)}</div>
        <div class="mono-info-sub">ราคาปัจจุบัน ${helpers.money(auction.currentBidAmount)} โดย ${escapeHtml(bidderName)}</div>
      </div>
      <form class="mono-form" data-form="bid-auction">
        <label>ราคาที่ต้องการบิด</label>
        <input name="bidAmount" type="number" min="${minBid}" step="1" placeholder="อย่างน้อย ${minBid}" required>
        <button type="submit" class="btn mono-pop-btn mono-pop-btn-primary">ยืนยันราคาบิด</button>
      </form>
      ${renderActionButtons([
        { label: "ผ่าน", action: "pass-auction", tone: "" },
      ])}
    `;
  }

  function renderTradeResponse(monopoly) {
    const helpers = root.monopolyHelpers;
    const offer = monopoly.activeTradeOffer;
    if (!offer) {
      return `<div class="mono-muted">ไม่มีข้อเสนอเทรด</div>`;
    }

    const giveCells = Array.isArray(offer.giveCells) ? offer.giveCells.join(", ") : "-";
    const receiveCells = Array.isArray(offer.receiveCells)
      ? offer.receiveCells.join(", ")
      : "-";

    return `
      <div class="mono-info-card">
        <div class="mono-info-title">ข้อเสนอเทรดจาก ${escapeHtml(helpers.playerName(offer.fromPlayerId))}</div>
        <div class="mono-info-sub">เงินที่เขาให้: ${helpers.money(offer.cashGive)} | เงินที่เขาขอ: ${helpers.money(offer.cashReceive)}</div>
        <div class="mono-info-sub">ทรัพย์สินที่เขาให้: ${escapeHtml(giveCells || "-")}</div>
        <div class="mono-info-sub">ทรัพย์สินที่เขาขอ: ${escapeHtml(receiveCells || "-")}</div>
      </div>
      ${renderActionButtons([
        { label: "ตอบรับเทรด", action: "accept-trade", tone: "primary" },
        { label: "ปฏิเสธเทรด", action: "reject-trade", tone: "danger" },
      ])}
    `;
  }

  function renderManageWizard(monopoly) {
    switch (panelState.manageStep) {
      case "build":
        return renderBuildSellForm("build", "สร้างบ้าน/โรงแรม", "build-house", monopoly);
      case "sell":
        return renderBuildSellForm("sell", "ขายบ้าน/โรงแรม", "sell-house", monopoly);
      case "mortgage":
        return renderMortgageForm(false, monopoly);
      case "unmortgage":
        return renderMortgageForm(true, monopoly);
      case "trade":
        return renderTradeWizard(monopoly);
      case "bankruptcy":
        return renderBankruptcyConfirm();
      default:
        return renderManageMenu(monopoly);
    }
  }

  function renderManageMenu(monopoly) {
    const available = evaluateManageActions(monopoly);
    const buttons = [];

    if (available.canBuild) {
      buttons.push({ label: `สร้างบ้าน / โรงแรม (${available.buildCount})`, action: "step-build", tone: "" });
    }
    if (available.canSell) {
      buttons.push({ label: `ขายบ้าน / โรงแรม (${available.sellCount})`, action: "step-sell", tone: "" });
    }
    if (available.canMortgage) {
      buttons.push({ label: `จำนองทรัพย์สิน (${available.mortgageCount})`, action: "step-mortgage", tone: "" });
    }
    if (available.canUnmortgage) {
      buttons.push({ label: `ไถ่ถอนจำนอง (${available.unmortgageCount})`, action: "step-unmortgage", tone: "" });
    }
    if (available.canTrade) {
      buttons.push({ label: "เสนอเทรด", action: "step-trade", tone: "" });
    }
    buttons.push({ label: "จบเทิร์น", action: "end-turn", tone: "primary" });
    if (available.canBankruptcy) {
      buttons.push({ label: "ประกาศล้มละลาย", action: "step-bankruptcy", tone: "danger" });
    }

    const recommendation = resolveManageRecommendation(available);

    return `
      <div class="mono-step-badge">ขั้นตอนที่ 1: เลือกการจัดการทรัพย์สิน</div>
      ${renderActionButtons(buttons)}
      <div class="mono-tip mono-tip-warn">หากไม่ทำอะไรภายใน 45 วินาที ระบบจะจบเทิร์นให้อัตโนมัติ</div>
      ${recommendation ? `<div class="mono-tip mono-tip-reco">คำแนะนำ: ${escapeHtml(recommendation)}</div>` : ""}
    `;
  }

  function resolveManageRecommendation(available) {
    if (available.canBuild) {
      return "หากมีเงินเหลือ ลองเริ่มจากการสร้างบ้านเพื่อเพิ่มค่าเช่า";
    }
    if (available.canUnmortgage) {
      return "ไถ่ถอนทรัพย์ที่จำนองก่อน เพื่อกลับมาเก็บค่าเช่าได้";
    }
    if (available.canMortgage) {
      return "ถ้าเงินตึงมือ ใช้จำนองเพื่อเพิ่มสภาพคล่องก่อนจบเทิร์น";
    }
    if (available.canSell) {
      return "ขายสิ่งปลูกสร้างบางส่วนเพื่อรักษาเงินสดสำรอง";
    }
    if (available.canTrade) {
      return "ลองเสนอเทรดเพื่อปิดชุดสีให้ครบเร็วขึ้น";
    }
    if (available.canBankruptcy) {
      return "หนี้สูงและจัดการต่อไม่ไหว ค่อยใช้ล้มละลายเป็นทางเลือกสุดท้าย";
    }
    return "";
  }

  function evaluateManageActions(monopoly) {
    const cash = currentPlayerCash(monopoly);
    const buildCandidates = getBuildCandidates(state.playerId, monopoly, cash);
    const sellCandidates = getSellCandidates(state.playerId);
    const mortgageCandidates = getMortgageCandidates(state.playerId);
    const unmortgageCandidates = getUnmortgageCandidates(state.playerId, cash);
    const pendingDebt = parseIntVal(monopoly?.pendingDebtAmount);

    return {
      buildCount: buildCandidates.length,
      sellCount: sellCandidates.length,
      mortgageCount: mortgageCandidates.length,
      unmortgageCount: unmortgageCandidates.length,
      canBuild: buildCandidates.length > 0,
      canSell: sellCandidates.length > 0,
      canMortgage: mortgageCandidates.length > 0,
      canUnmortgage: unmortgageCandidates.length > 0,
      canTrade: getTradeTargets().length > 0 && !monopoly?.activeTradeOffer,
      canBankruptcy: pendingDebt > 0 || cash < 0,
    };
  }

  function renderBuildSellForm(mode, title, formAction, monopoly) {
    const cash = currentPlayerCash(monopoly);
    const properties =
      mode === "build"
        ? getBuildCandidates(state.playerId, monopoly, cash)
        : getSellCandidates(state.playerId);

    if (properties.length === 0) {
      return `
        <div class="mono-step-badge">ขั้นตอนที่ 2: ${escapeHtml(title)}</div>
        <div class="mono-muted">ตอนนี้ยังไม่มีทรัพย์สินที่ทำรายการนี้ได้</div>
        ${renderActionButtons([{ label: "ย้อนกลับ", action: "back-menu", tone: "" }])}
      `;
    }

    return `
      <div class="mono-step-badge">ขั้นตอนที่ 2: ${escapeHtml(title)}</div>
      <form class="mono-form" data-form="${formAction}">
        <label>เลือกทรัพย์สิน</label>
        <select name="cellId" required>
          ${renderCellOptions(properties)}
        </select>
        <button type="submit" class="btn mono-pop-btn mono-pop-btn-primary">ยืนยัน</button>
      </form>
      ${renderActionButtons([{ label: "ย้อนกลับ", action: "back-menu", tone: "" }])}
    `;
  }

  function renderMortgageForm(unmortgageMode, monopoly) {
    const cash = currentPlayerCash(monopoly);
    const properties = unmortgageMode
      ? getUnmortgageCandidates(state.playerId, cash)
      : getMortgageCandidates(state.playerId);

    const formAction = unmortgageMode ? "unmortgage" : "mortgage";
    const title = unmortgageMode ? "ไถ่ถอนจำนอง" : "จำนองทรัพย์สิน";

    if (properties.length === 0) {
      return `
        <div class="mono-step-badge">ขั้นตอนที่ 2: ${title}</div>
        <div class="mono-muted">ไม่มีทรัพย์สินที่ทำรายการนี้ได้</div>
        ${renderActionButtons([{ label: "ย้อนกลับ", action: "back-menu", tone: "" }])}
      `;
    }

    return `
      <div class="mono-step-badge">ขั้นตอนที่ 2: ${title}</div>
      <form class="mono-form" data-form="${formAction}">
        <label>เลือกทรัพย์สิน</label>
        <select name="cellId" required>
          ${renderCellOptions(properties)}
        </select>
        <button type="submit" class="btn mono-pop-btn mono-pop-btn-primary">ยืนยัน</button>
      </form>
      ${renderActionButtons([{ label: "ย้อนกลับ", action: "back-menu", tone: "" }])}
    `;
  }

  function renderTradeWizard(monopoly) {
    const players = getTradeTargets();

    if (players.length === 0 || monopoly?.activeTradeOffer) {
      return `
        <div class="mono-step-badge">ขั้นตอนที่ 2: เสนอเทรด</div>
        <div class="mono-muted">ยังไม่สามารถเริ่มเทรดได้ในตอนนี้</div>
        ${renderActionButtons([{ label: "ย้อนกลับ", action: "back-menu", tone: "" }])}
      `;
    }

    const selectedTargetId = resolveSelectedTradeTarget(players);
    const myCells = getTradeableAssets(state.playerId);
    const targetCells = getTradeableAssets(selectedTargetId);

    return `
      <div class="mono-step-badge">ขั้นตอนที่ 2: เสนอเทรด</div>
      <form class="mono-form" data-form="offer-trade">
        <label>ผู้เล่นเป้าหมาย</label>
        <select name="targetPlayerId" data-field="trade-target">
          ${players
            .map(
              (player) =>
                `<option value="${escapeHtml(player.playerId)}" ${player.playerId === selectedTargetId ? "selected" : ""}>${escapeHtml(player.displayName)}</option>`,
            )
            .join("")}
        </select>

        <div class="mono-form-grid">
          <label>เงินที่คุณให้
            <input name="cashGive" type="number" min="0" step="1" value="0">
          </label>
          <label>เงินที่คุณขอ
            <input name="cashReceive" type="number" min="0" step="1" value="0">
          </label>
        </div>

        <div class="mono-checklist-wrap">
          <div class="mono-checklist-title">ทรัพย์สินที่คุณจะให้</div>
          ${renderCellChecklist("giveCells", myCells)}
        </div>

        <div class="mono-checklist-wrap">
          <div class="mono-checklist-title">ทรัพย์สินที่คุณขอ</div>
          ${renderCellChecklist("receiveCells", targetCells)}
        </div>

        <button type="submit" class="btn mono-pop-btn mono-pop-btn-primary">ส่งข้อเสนอเทรด</button>
      </form>
      ${renderActionButtons([{ label: "ย้อนกลับ", action: "back-menu", tone: "" }])}
    `;
  }

  function renderPhaseAdvice(phase, monopoly) {
    const tips = [];
    const cash = currentPlayerCash(monopoly);
    const pendingDebt = parseIntVal(monopoly?.pendingDebtAmount);

    switch (phase) {
      case root.MONOPOLY_PHASE.AWAIT_ROLL:
        tips.push("ทอยแล้วรอดูการเดินตัวละครก่อน ระบบจะเปิดแอคชั่นถัดไปอัตโนมัติ");
        break;
      case root.MONOPOLY_PHASE.AWAIT_JAIL_DECISION:
        tips.push(
          cash >= 50
            ? "ถ้าไม่อยากเสี่ยง จ่ายค่าปรับเพื่อออกคุกได้ทันที"
            : "เงินไม่พอค่าปรับ ควรเลือกทอยดับเบิลเพื่อออกจากคุก",
        );
        break;
      case root.MONOPOLY_PHASE.AWAIT_PURCHASE_DECISION:
        {
          const cell = root.monopolyHelpers.findCell(monopoly?.pendingPurchaseCellId);
          const price = parseIntVal(cell?.price ?? cell?.Price);
          if (price > 0 && cash < price) {
            tips.push(
              `เงินไม่พอซื้อช่องนี้ (ขาด ${root.monopolyHelpers.money(price - cash)}) ให้เลือกไม่ซื้อเพื่อเข้าสู่ประมูล`,
            );
          } else {
            tips.push("เช็กเงินคงเหลือหลังซื้อ เพื่อกันพลาดตอนเจอค่าเช่าหนัก");
          }
        }
        break;
      case root.MONOPOLY_PHASE.AUCTION_IN_PROGRESS:
        tips.push("บิดเฉพาะราคาที่รับไหว การประมูลจบรอบนี้ทันทีเมื่อทุกคนผ่าน");
        break;
      case root.MONOPOLY_PHASE.AWAIT_TRADE_RESPONSE:
        tips.push("เทียบมูลค่าทรัพย์สินและเงินสดก่อนตอบรับข้อเสนอเทรด");
        break;
      case root.MONOPOLY_PHASE.AWAIT_MANAGE:
      case root.MONOPOLY_PHASE.AWAIT_END_TURN:
        if (pendingDebt > 0) {
          tips.push(`ตอนนี้มีหนี้คงค้าง ${root.monopolyHelpers.money(pendingDebt)} ควรจัดการเงินสดก่อน`);
        }
        tips.push("แสดงเฉพาะปุ่มที่ทำได้จริงในเทิร์นนี้ เพื่อลดความสับสน");
        break;
      default:
        break;
    }

    if (tips.length === 0) {
      return "";
    }

    return `
      <div class="mono-advice">
        <div class="mono-advice-title">คำแนะนำ</div>
        <ul class="mono-advice-list">
          ${tips.map((tip) => `<li>${escapeHtml(tip)}</li>`).join("")}
        </ul>
      </div>
    `;
  }

  function renderBankruptcyConfirm() {
    return `
      <div class="mono-step-badge">ขั้นตอนที่ 2: ยืนยันการล้มละลาย</div>
      <div class="mono-muted">การกระทำนี้ย้อนกลับไม่ได้ ระบบจะโอนทรัพย์สินตามกติกา</div>
      ${renderActionButtons([
        { label: "ยืนยันล้มละลาย", action: "declare-bankruptcy", tone: "danger" },
        { label: "ย้อนกลับ", action: "back-menu", tone: "" },
      ])}
    `;
  }

  function renderCellChecklist(name, cells) {
    if (!cells || cells.length === 0) {
      return `<div class="mono-muted">ไม่มีทรัพย์สิน</div>`;
    }

    return `
      <div class="mono-checklist">
        ${cells
          .map((cell) => {
            const id = resolveCellNo(cell);
            return `
              <label class="mono-check-item">
                <input type="checkbox" name="${name}" value="${id}">
                <span>${escapeHtml(`${id} - ${cellName(cell)}`)}</span>
              </label>
            `;
          })
          .join("")}
      </div>
    `;
  }

  function renderCellOptions(cells) {
    return cells
      .map((cell) => {
        const cellNo = resolveCellNo(cell);
        const mortgaged = Boolean(cell?.isMortgaged ?? cell?.IsMortgaged);
        const houses = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
        const hotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
        const suffix = [
          mortgaged ? "จำนอง" : "",
          hotel ? "โรงแรม" : houses > 0 ? `บ้าน ${houses}` : "",
        ]
          .filter(Boolean)
          .join(" | ");
        return `<option value="${cellNo}">${escapeHtml(`${cellNo} - ${cellName(cell)}${suffix ? ` (${suffix})` : ""}`)}</option>`;
      })
      .join("");
  }

  function renderActionButtons(buttons) {
    return `
      <div class="mono-pop-actions">
        ${buttons
          .map((button) => {
            const toneClass = button.tone ? `mono-pop-btn-${button.tone}` : "";
            return `<button type="button" class="btn mono-pop-btn ${toneClass}" data-action="${button.action}">${escapeHtml(button.label)}</button>`;
          })
          .join("")}
      </div>
    `;
  }

  async function onPanelClick(event) {
    const button = event.target.closest("[data-action]");
    if (!button) {
      return;
    }

    const action = String(button.dataset.action ?? "").trim();
    if (!action) {
      return;
    }

    if (action.startsWith("step-")) {
      panelState.manageStep = action.replace("step-", "");
      render();
      return;
    }

    if (action === "back-menu") {
      panelState.manageStep = "menu";
      render();
      return;
    }

    await submitSimpleAction(action);
  }

  async function onPanelSubmit(event) {
    const form = event.target.closest("form[data-form]");
    if (!form) {
      return;
    }

    event.preventDefault();
    const formType = String(form.dataset.form ?? "").trim();
    const formData = new FormData(form);

    switch (formType) {
      case "bid-auction": {
        const bidAmount = parseIntVal(formData.get("bidAmount"));
        if (bidAmount <= 0) {
          root.feedback.logEvent("กรุณาใส่ราคาบิดที่ถูกต้อง", true);
          return;
        }

        await root.monopolyHelpers.submitAction(root.GAME_ACTION_TYPES.BID_AUCTION, {
          bidAmount,
        });
        return;
      }

      case "build-house":
      case "sell-house":
      case "mortgage":
      case "unmortgage": {
        const cellId = parseIntVal(formData.get("cellId"));
        if (cellId <= 0) {
          root.feedback.logEvent("กรุณาเลือกทรัพย์สิน", true);
          return;
        }

        const map = {
          "build-house": root.GAME_ACTION_TYPES.BUILD_HOUSE,
          "sell-house": root.GAME_ACTION_TYPES.SELL_HOUSE,
          mortgage: root.GAME_ACTION_TYPES.MORTGAGE,
          unmortgage: root.GAME_ACTION_TYPES.UNMORTGAGE,
        };

        await root.monopolyHelpers.submitAction(map[formType], { cellId });
        panelState.manageStep = "menu";
        return;
      }

      case "offer-trade": {
        const targetPlayerId = String(formData.get("targetPlayerId") ?? "").trim();
        if (!targetPlayerId) {
          root.feedback.logEvent("กรุณาเลือกผู้เล่นเป้าหมาย", true);
          return;
        }

        const cashGive = Math.max(0, parseIntVal(formData.get("cashGive")));
        const cashReceive = Math.max(0, parseIntVal(formData.get("cashReceive")));
        const giveCells = formData
          .getAll("giveCells")
          .map((value) => parseIntVal(value))
          .filter((value) => value > 0);
        const receiveCells = formData
          .getAll("receiveCells")
          .map((value) => parseIntVal(value))
          .filter((value) => value > 0);

        await root.monopolyHelpers.submitAction(root.GAME_ACTION_TYPES.OFFER_TRADE, {
          targetPlayerId,
          tradeOffer: {
            cashGive,
            cashReceive,
            giveCells,
            receiveCells,
          },
        });
        panelState.manageStep = "menu";
        return;
      }

      default:
        return;
    }
  }

  function onPanelChange(event) {
    const select = event.target.closest("select[data-field='trade-target']");
    if (!select) {
      return;
    }

    panelState.tradeTargetPlayerId = String(select.value ?? "").trim();
    render();
  }

  async function submitSimpleAction(action) {
    const submit = root.monopolyHelpers.submitAction;

    switch (action) {
      case "roll":
        await submit(root.GAME_ACTION_TYPES.ROLL_DICE, {});
        return;
      case "pay-jail":
        await submit(root.GAME_ACTION_TYPES.PAY_JAIL_FINE, {});
        return;
      case "try-jail-roll":
        await submit(root.GAME_ACTION_TYPES.TRY_JAIL_ROLL, {});
        return;
      case "buy-property":
        await submit(root.GAME_ACTION_TYPES.BUY_PROPERTY, {});
        return;
      case "decline-property":
        await submit(root.GAME_ACTION_TYPES.DECLINE_PURCHASE, {});
        return;
      case "pass-auction":
        await submit(root.GAME_ACTION_TYPES.PASS_AUCTION, {});
        return;
      case "accept-trade":
        await submit(root.GAME_ACTION_TYPES.ACCEPT_TRADE, {});
        return;
      case "reject-trade":
        await submit(root.GAME_ACTION_TYPES.REJECT_TRADE, {});
        return;
      case "end-turn":
        await submit(root.GAME_ACTION_TYPES.END_TURN, {});
        return;
      case "declare-bankruptcy":
        await submit(root.GAME_ACTION_TYPES.DECLARE_BANKRUPTCY, {});
        panelState.manageStep = "menu";
        return;
      default:
        return;
    }
  }

  function ownedCellsOf(playerId) {
    return (root.monopolyHelpers.resolveCells() ?? []).filter((cell) => {
      const owner = String(cell?.ownerPlayerId ?? cell?.OwnerPlayerId ?? "").trim();
      return owner === playerId;
    });
  }

  function getTradeableAssets(playerId) {
    return ownedCellsOf(playerId).filter((cell) => {
      const type = resolveType(cell);
      const houses = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
      const hasHotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
      return (type === 1 || type === 2 || type === 3) && houses <= 0 && !hasHotel;
    });
  }

  function getTradeTargets() {
    return (state.room?.players ?? []).filter(
      (player) => player.playerId !== state.playerId && !player.isBankrupt,
    );
  }

  function currentPlayerCash(monopoly) {
    const fromEconomy = (monopoly?.playerEconomy ?? []).find(
      (entry) => entry?.playerId === state.playerId,
    );
    if (fromEconomy) {
      return parseIntVal(fromEconomy.cash);
    }

    const player = (state.room?.players ?? []).find(
      (entry) => entry.playerId === state.playerId,
    );
    return parseIntVal(player?.cash);
  }

  function getBuildCandidates(playerId, monopoly, cash) {
    return ownedCellsOf(playerId).filter((cell) => {
      if (resolveType(cell) !== 1) {
        return false;
      }

      if (Boolean(cell?.isMortgaged ?? cell?.IsMortgaged)) {
        return false;
      }

      const group = String(cell?.colorGroup ?? cell?.ColorGroup ?? "").trim();
      if (!ownsFullColorSet(playerId, group) || hasMortgagedInColorSet(playerId, group)) {
        return false;
      }

      if (!canBuildEvenly(playerId, cell, group)) {
        return false;
      }

      const hasHotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
      const houseCount = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
      if (hasHotel) {
        return false;
      }

      const houseCost = parseIntVal(cell?.houseCost ?? cell?.HouseCost);
      if (cash < houseCost) {
        return false;
      }

      if (houseCount < 4) {
        return parseIntVal(monopoly?.availableHouses) > 0;
      }

      return parseIntVal(monopoly?.availableHotels) > 0;
    });
  }

  function getSellCandidates(playerId) {
    return ownedCellsOf(playerId).filter((cell) => {
      if (resolveType(cell) !== 1) {
        return false;
      }

      const group = String(cell?.colorGroup ?? cell?.ColorGroup ?? "").trim();
      if (!canSellEvenly(playerId, cell, group)) {
        return false;
      }

      const hasHotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
      if (hasHotel) {
        return parseIntVal(currentMonopolyState()?.availableHouses) >= 4;
      }

      return parseIntVal(cell?.houseCount ?? cell?.HouseCount) > 0;
    });
  }

  function getMortgageCandidates(playerId) {
    return ownedCellsOf(playerId).filter((cell) => {
      const type = resolveType(cell);
      if (type !== 1 && type !== 2 && type !== 3) {
        return false;
      }

      if (Boolean(cell?.isMortgaged ?? cell?.IsMortgaged)) {
        return false;
      }

      const houses = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
      const hasHotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
      return houses <= 0 && !hasHotel;
    });
  }

  function getUnmortgageCandidates(playerId, cash) {
    return ownedCellsOf(playerId).filter((cell) => {
      const type = resolveType(cell);
      if (type !== 1 && type !== 2 && type !== 3) {
        return false;
      }

      const mortgaged = Boolean(cell?.isMortgaged ?? cell?.IsMortgaged);
      if (!mortgaged) {
        return false;
      }

      return cash >= unmortgageCost(cell);
    });
  }

  function ownsFullColorSet(playerId, group) {
    const key = normalizeGroup(group);
    if (!key) {
      return false;
    }

    const requiredCount = COLOR_GROUP_SET_SIZE[key] ?? 0;
    if (requiredCount <= 0) {
      return false;
    }

    const ownedCount = ownedColorGroupCells(playerId, group).length;
    return ownedCount >= requiredCount;
  }

  function hasMortgagedInColorSet(playerId, group) {
    return ownedColorGroupCells(playerId, group).some(
      (cell) => Boolean(cell?.isMortgaged ?? cell?.IsMortgaged),
    );
  }

  function canBuildEvenly(playerId, targetCell, group) {
    const groupCells = ownedColorGroupCells(playerId, group);
    if (groupCells.length === 0) {
      return false;
    }

    const targetLevel = buildingLevel(targetCell);
    const minLevel = Math.min(...groupCells.map((cell) => buildingLevel(cell)));
    return targetLevel === minLevel;
  }

  function canSellEvenly(playerId, targetCell, group) {
    const groupCells = ownedColorGroupCells(playerId, group);
    if (groupCells.length === 0) {
      return false;
    }

    const targetLevel = buildingLevel(targetCell);
    const maxLevel = Math.max(...groupCells.map((cell) => buildingLevel(cell)));
    return targetLevel === maxLevel;
  }

  function ownedColorGroupCells(playerId, group) {
    const normalized = normalizeGroup(group);
    if (!normalized) {
      return [];
    }

    return ownedCellsOf(playerId).filter((cell) => (
      resolveType(cell) === 1 &&
      normalizeGroup(cell?.colorGroup ?? cell?.ColorGroup) === normalized
    ));
  }

  function normalizeGroup(group) {
    return String(group ?? "").trim().toLowerCase();
  }

  function buildingLevel(cell) {
    const hasHotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
    if (hasHotel) {
      return 5;
    }
    const houses = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
    return Math.max(0, Math.min(4, houses));
  }

  function unmortgageCost(cell) {
    const price = parseIntVal(cell?.price ?? cell?.Price);
    const mortgage = Math.floor(price / 2);
    return Math.ceil(mortgage * 1.1);
  }

  function currentMonopolyState() {
    return root.monopolyHelpers?.getMonopolyState?.() ?? null;
  }

  function resolveSelectedTradeTarget(players) {
    if (!players || players.length === 0) {
      return "";
    }

    const found = players.find((player) => player.playerId === panelState.tradeTargetPlayerId);
    if (found) {
      return found.playerId;
    }

    panelState.tradeTargetPlayerId = players[0].playerId;
    return panelState.tradeTargetPlayerId;
  }

  function renderTurnMeta(monopoly) {
    const helpers = root.monopolyHelpers;
    const active = helpers.playerName(monopoly.activePlayerId);
    const pending = helpers.playerName(monopoly.pendingDecisionPlayerId);
    return `ตาปัจจุบัน: ${active} | สิทธิ์ตัดสินใจ: ${pending}`;
  }

  function phaseTitle(phase) {
    switch (phase) {
      case root.MONOPOLY_PHASE.AWAIT_ROLL:
        return "ทอยลูกเต๋า";
      case root.MONOPOLY_PHASE.AWAIT_JAIL_DECISION:
        return "ตัดสินใจตอนติดคุก";
      case root.MONOPOLY_PHASE.AWAIT_PURCHASE_DECISION:
        return "ตัดสินใจซื้อทรัพย์สิน";
      case root.MONOPOLY_PHASE.AUCTION_IN_PROGRESS:
        return "ประมูลทรัพย์สิน";
      case root.MONOPOLY_PHASE.AWAIT_TRADE_RESPONSE:
        return "ตอบข้อเสนอเทรด";
      case root.MONOPOLY_PHASE.AWAIT_MANAGE:
      case root.MONOPOLY_PHASE.AWAIT_END_TURN:
        return "จัดการทรัพย์สิน";
      default:
        return "แอคชั่นเกมเศรษฐี";
    }
  }

  function hidePopup() {
    el.monopolyActionPopup?.classList.add("hidden");
  }

  function updateCountdownLabel() {
    const timerEl = el.monopolyActionPanel?.querySelector(".mono-pop-timer");
    if (!timerEl) {
      return;
    }

    const sec = resolveDeadlineSec(state.room?.turnDeadlineUtc);
    if (sec <= 0) {
      timerEl.textContent = "กำลังหมดเวลา...";
      return;
    }

    timerEl.textContent = `เหลือเวลา ${sec} วินาที`;
  }

  function resolveDeadlineSec(deadlineUtc) {
    if (!deadlineUtc) {
      return 0;
    }

    const remainMs = new Date(deadlineUtc).getTime() - Date.now();
    if (!Number.isFinite(remainMs) || remainMs <= 0) {
      return 0;
    }

    return Math.max(1, Math.ceil(remainMs / 1000));
  }

  function resolveType(cell) {
    return parseIntVal(cell?.type ?? cell?.Type);
  }

  function resolveCellNo(cell) {
    return parseIntVal(cell?.cell ?? cell?.Cell);
  }

  function cellName(cell) {
    return String(cell?.name ?? cell?.Name ?? "ทรัพย์สิน");
  }

  function parseIntVal(value) {
    return Number.parseInt(String(value ?? "0"), 10) || 0;
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
