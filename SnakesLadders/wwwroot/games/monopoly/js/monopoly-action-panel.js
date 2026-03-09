(() => {
  const root = window.SNL;
  const { state, el } = root;

  const panelState = {
    manageStep: "menu",
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
    countdownTimer = window.setInterval(updateCountdownLabel, 1000);
  }

  function reset() {
    panelState.manageStep = "menu";
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
    const helpers = root.monopolyHelpers;
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
    const moneySummary = state.lastTurn?.clientFx?.currentPlayerMoneySummary ?? null;
    const sections = [];

    if (eventLine) {
      const toneClass = eventLine.startsWith("โอกาส:")
        ? "mono-outcome-chance"
        : "mono-outcome-community";
      sections.push(`
        <div class="mono-outcome ${toneClass}">
          <div class="mono-outcome-label">เหตุการณ์ที่เกิดขึ้น</div>
          <div class="mono-outcome-text">${escapeHtml(eventLine)}</div>
        </div>
      `);
    }

    if (moneySummary) {
      sections.push(`
        <div class="mono-outcome mono-outcome-money ${moneySummary.incoming ? "mono-outcome-money-in" : "mono-outcome-money-out"}">
          <div class="mono-outcome-label">${escapeHtml(String(moneySummary.title ?? "สรุปธุรกรรม"))}</div>
          <div class="mono-outcome-text">
            ${moneySummary.incoming ? "รับ" : "จ่าย"} ${helpers.money(moneySummary.amount ?? 0)}
            ${moneySummary.incoming ? " | ตอนนี้มี " : " | เหลือ "}
            ${helpers.money(moneySummary.afterCash ?? 0)}
          </div>
        </div>
      `);
    }

    return sections.join("");
  }

  function shouldShowForCurrentPlayer(monopoly) {
    const helpers = root.monopolyHelpers;
    const phase = helpers.phase();

    if (
      phase === root.MONOPOLY_PHASE.RESOLVING ||
      phase === root.MONOPOLY_PHASE.FINISHED ||
      phase === root.MONOPOLY_PHASE.AWAIT_ROLL
    ) {
      return false;
    }

    if (state.animating) {
      return false;
    }

    return helpers.isMyDecisionTurn();
  }

  function renderByPhase(phase, monopoly) {
    switch (phase) {
      case root.MONOPOLY_PHASE.AWAIT_JAIL_DECISION:
        return renderJailDecision(monopoly);

      case root.MONOPOLY_PHASE.AWAIT_PURCHASE_DECISION:
        return renderPurchaseDecision(monopoly);

      case root.MONOPOLY_PHASE.AWAIT_MANAGE:
      case root.MONOPOLY_PHASE.AWAIT_END_TURN:
        if (parseIntVal(monopoly?.pendingDebtAmount) > 0) {
          return renderDebtResolution(monopoly);
        }
        return renderManageWizard(monopoly);

      default:
        return `<div class="mono-muted">รอการอัปเดตสถานะ</div>`;
    }
  }

  function renderJailDecision(monopoly) {
    const helpers = root.monopolyHelpers;
    const fine = parseIntVal(monopoly?.currentJailFine) || 50;
    const cash = currentPlayerCash(monopoly);
    const shortfall = Math.max(0, fine - cash);

    return `
      <div class="mono-info-card">
        <div class="mono-info-title">ออกจากคุก</div>
        <div class="mono-info-sub">ค่าประกันปัจจุบัน ${helpers.money(fine)}</div>
        <div class="mono-info-sub">เงินคงเหลือ ${helpers.money(cash)}</div>
      </div>
      ${
        shortfall > 0
          ? `<div class="mono-tip mono-tip-warn">เงินยังไม่พอค่าประกัน ขาด ${helpers.money(shortfall)} ถ้ากดจ่าย ระบบจะพาไปขายทรัพย์สินต่อทันที</div>`
          : ""
      }
      <div class="mono-tip mono-tip-reco">คำแนะนำ: ถ้าทอยแก้คุกไม่ออกดับเบิล ค่าประกันรอบถัดไปจะคูณ 2 จากรอบก่อน</div>
      ${renderActionButtons([
        { label: `จ่ายค่าประกันออกจากคุก (${helpers.money(fine)})`, action: "pay-jail", tone: "primary" },
        { label: "เสี่ยงทอยดับเบิลเพื่อออกจากคุก", action: "try-jail-roll", tone: "" },
      ])}
    `;
  }

  function renderPurchaseDecision(monopoly) {
    const helpers = root.monopolyHelpers;
    const cell = helpers.findCell(monopoly.pendingPurchaseCellId);
    const name = cellName(cell);
    const price = parseIntVal(
      monopoly?.pendingPurchasePrice ?? currentCityPrice(cell),
    );
    const cash = currentPlayerCash(monopoly);
    const shortfall = Math.max(0, price - cash);
    const canAfford = cash >= price;
    const ownerId = String(monopoly?.pendingPurchaseOwnerPlayerId ?? "").trim();
    const ownerName = ownerId ? helpers.playerName(ownerId) : "";
    const isTakeover = Boolean(ownerId);
    const recommendation = isTakeover
      ? canAfford
        ? "คุณจ่ายค่าผ่านทางเรียบร้อยแล้ว ถ้าซื้อต่อจะยึดสิทธิ์ความเป็นเจ้าของทันที"
        : `เงินยังไม่พอซื้อต่อ ขาดอีก ${helpers.money(shortfall)} เลือกข้ามการซื้อแล้วไปต่อได้`
      : canAfford
        ? "ถ้าอยากเก็บเงินไว้ก่อน กดข้ามการซื้อแล้วไปจัดการทรัพย์สินต่อได้"
        : `เงินไม่พอซื้อ ขาดอีก ${helpers.money(shortfall)} กดข้ามการซื้อแล้วไปต่อได้`;
    const actions = canAfford
      ? [
        { label: isTakeover ? "ซื้อทรัพย์สินนี้ต่อ" : "ซื้อทรัพย์สิน", action: "buy-property", tone: "primary" },
        { label: isTakeover ? "ไม่ซื้อ / จบช่วงนี้" : "ข้ามการซื้อ", action: "decline-property", tone: "danger" },
      ]
      : [
        { label: isTakeover ? "ยังไม่ซื้อ / จบช่วงนี้" : "ข้ามการซื้อ", action: "decline-property", tone: "danger" },
      ];

    return `
      <div class="mono-info-card">
        <div class="mono-info-title">${escapeHtml(name)}</div>
        <div class="mono-info-sub">${isTakeover ? `เจ้าของปัจจุบัน ${escapeHtml(ownerName)}` : "ทรัพย์สินของธนาคาร"}</div>
        <div class="mono-info-sub">${isTakeover ? "ราคาซื้อต่อ" : "ราคา"} ${helpers.money(price)}</div>
        <div class="mono-info-sub">เงินคงเหลือ ${helpers.money(cash)}</div>
      </div>
      ${
        canAfford
          ? ""
          : `<div class="mono-tip mono-tip-warn">เงินไม่พอสำหรับการ${isTakeover ? "ซื้อต่อ" : "ซื้อช่องนี้"} (ขาด ${helpers.money(shortfall)})</div>`
      }
      <div class="mono-tip mono-tip-reco">คำแนะนำ: ${escapeHtml(recommendation)}</div>
      ${renderActionButtons(actions)}
    `;
  }

  function renderManageWizard(monopoly) {
    switch (panelState.manageStep) {
      case "build":
        return renderBuildSellForm("build", "อัปเกรดเมือง / โรงแรม / แลนด์มาร์ก", "build-house", monopoly);
      case "sell":
        return renderBuildSellForm("sell", "ลดระดับเมือง / ขายโรงแรม / รื้อแลนด์มาร์ก", "sell-house", monopoly);
      case "mortgage":
        return renderMortgageForm(false, monopoly);
      case "unmortgage":
        return renderMortgageForm(true, monopoly);
      case "bankruptcy":
        return renderBankruptcyConfirm();
      default:
        return renderManageMenu(monopoly);
    }
  }

  function renderManageMenu(monopoly) {
    const helpers = root.monopolyHelpers;
    const available = evaluateManageActions(monopoly);
    const landingOpportunity = resolveLandingUpgradeOpportunity(monopoly);
    const buttons = [];

    if (landingOpportunity) {
      buttons.push({
        label: landingOpportunity.buttonLabel,
        action: "upgrade-landed-now",
        tone: "primary",
      });
    }
    if (available.canBuild) {
      buttons.push({ label: `อัปเกรดเมือง / โรงแรม / แลนด์มาร์ก (${available.buildCount})`, action: "step-build", tone: "" });
    }
    if (available.canSell) {
      buttons.push({ label: `ลดระดับเมือง / ขายโรงแรม / รื้อแลนด์มาร์ก (${available.sellCount})`, action: "step-sell", tone: "" });
    }
    if (available.canMortgage) {
      buttons.push({ label: `จำนองทรัพย์สิน (${available.mortgageCount})`, action: "step-mortgage", tone: "" });
    }
    if (available.canUnmortgage) {
      buttons.push({ label: `ไถ่ถอนจำนอง (${available.unmortgageCount})`, action: "step-unmortgage", tone: "" });
    }
    buttons.push({ label: "จบเทิร์น", action: "end-turn", tone: "primary" });
    if (available.canBankruptcy) {
      buttons.push({ label: "ประกาศล้มละลาย", action: "step-bankruptcy", tone: "danger" });
    }

    const recommendation = resolveManageRecommendation(available, landingOpportunity);

    return `
      ${landingOpportunity ? `
        <div class="mono-info-card mono-opportunity-card">
          <div class="mono-info-title">โอกาสอัปเกรดทันที: ${escapeHtml(landingOpportunity.cellName)}</div>
          <div class="mono-info-sub">${escapeHtml(landingOpportunity.detail)}</div>
          <div class="mono-info-sub">ค่าใช้จ่าย ${helpers.money(landingOpportunity.cost)}</div>
        </div>
      ` : ""}
      <div class="mono-step-badge">ขั้นตอนที่ 1: เลือกการจัดการทรัพย์สิน</div>
      ${renderActionButtons(buttons)}
      <div class="mono-tip mono-tip-warn">หากไม่ทำอะไรภายใน 45 วินาที ระบบจะจบเทิร์นให้อัตโนมัติ</div>
      ${recommendation ? `<div class="mono-tip mono-tip-reco">คำแนะนำ: ${escapeHtml(recommendation)}</div>` : ""}
    `;
  }

  function resolveManageRecommendation(available, landingOpportunity) {
    const monopoly = currentMonopolyState();
    const eligibleCount = Array.isArray(monopoly?.upgradeEligibleCellIds)
      ? monopoly.upgradeEligibleCellIds.filter((value) => parseIntVal(value) > 0).length
      : 0;
    if (landingOpportunity) {
      return `เพิ่งมาตกที่ ${landingOpportunity.cellName} และสามารถ${landingOpportunity.shortAction}ได้เลย ระบบเตรียมปุ่มลัดไว้ให้แล้ว`;
    }
    if (eligibleCount > 1) {
      return `รอบนี้คุณได้สิทธิ์อัปเกรด 1 เมืองจากทั้งหมด ${eligibleCount} เมืองที่เลือกได้`;
    }
    if (available.canBuild) {
      return "รอบนี้อัปเกรดได้ 1 ครั้งเท่านั้น เลือกเมืองที่อยากดันรายได้ก่อน";
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
      canBankruptcy: pendingDebt > 0 || cash < 0,
    };
  }

  function renderDebtResolution(monopoly) {
    const helpers = root.monopolyHelpers;
    const debtAmount = parseIntVal(monopoly?.pendingDebtAmount);
    const creditorId = String(monopoly?.pendingDebtToPlayerId ?? "").trim();
    const debtReason = String(monopoly?.pendingDebtReason ?? "").trim();
    const buildingCandidates = getSellCandidates(state.playerId);
    const assetCandidates = getDebtSaleCandidates(state.playerId, monopoly);
    const buyerLabel = creditorId
      ? `ขายให้ ${helpers.playerName(creditorId)}`
      : "ขายคืนธนาคาร";

    return `
      <div class="mono-step-badge">เคลียร์หนี้ก่อนเล่นต่อ</div>
      <div class="mono-info-card mono-opportunity-card">
        <div class="mono-info-title">หนี้คงค้าง ${helpers.money(debtAmount)}</div>
        <div class="mono-info-sub">${escapeHtml(debtReason || (creditorId ? "หนี้ค่าผ่านทาง" : "หนี้กับธนาคาร"))}</div>
        <div class="mono-info-sub">${escapeHtml(creditorId ? `เจ้าหนี้: ${helpers.playerName(creditorId)}` : "เจ้าหนี้: ธนาคาร")}</div>
      </div>
      ${
        buildingCandidates.length > 0
          ? `
            <div class="mono-list-title">รื้อสิ่งปลูกสร้างเพื่อหาเงินสด</div>
            ${renderDebtActionList(
              buildingCandidates.map((cell) => ({
                cellId: resolveCellNo(cell),
                title: cellName(cell),
                meta: describeStructureSell(cell),
                amountLabel: helpers.money(estimateStructureSellValue(cell)),
                action: "sell-structure",
              })),
            )}
          `
          : ""
      }
      ${
        assetCandidates.length > 0
          ? `
            <div class="mono-list-title">${escapeHtml(buyerLabel)}</div>
            ${renderDebtActionList(
              assetCandidates.map((cell) => ({
                cellId: resolveCellNo(cell),
                title: cellName(cell),
                meta: describeDebtSale(cell, monopoly),
                amountLabel: helpers.money(estimateDebtSaleValue(cell, monopoly)),
                action: "sell-property",
              })),
            )}
          `
          : `<div class="mono-muted">ตอนนี้ไม่มีอสังหาที่ขายโอนได้แล้ว</div>`
      }
      <div class="mono-tip mono-tip-warn">ถ้าปิดหนี้ไม่ได้ก่อนหมดเวลา ระบบจะบังคับล้มละลายให้อัตโนมัติ</div>
      ${renderActionButtons([{ label: "ประกาศล้มละลาย", action: "declare-bankruptcy", tone: "danger" }])}
    `;
  }

  function renderBuildSellForm(mode, title, formAction, monopoly) {
    const helpers = root.monopolyHelpers;
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
      <div class="mono-tip mono-tip-reco">
        ${
          mode === "build"
            ? "ระบบจะอัปเกรดให้ทีละขั้นตามสถานะปัจจุบันของช่องที่เลือก เช่น บ้าน -> โรงแรม -> แลนด์มาร์ก"
            : "ระบบจะลดระดับจากสูงสุดก่อน เช่น แลนด์มาร์ก -> โรงแรม -> บ้าน"
        }
      </div>
      <div class="mono-list-title">เลือกทรัพย์สิน</div>
      ${renderDebtActionList(
        properties.map((cell) => ({
          cellId: resolveCellNo(cell),
          title: cellName(cell),
          meta: mode === "build"
            ? describeBuildCandidate(cell)
            : describeStructureSell(cell),
          amountLabel: helpers.money(
            mode === "build"
              ? describeNextUpgradeStep(cell)?.cost ?? 0
              : estimateStructureSellValue(cell),
          ),
          action: mode === "build" ? "build-property" : "sell-structure",
        })),
      )}
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
      <div class="mono-list-title">เลือกทรัพย์สิน</div>
      ${renderDebtActionList(
        properties.map((cell) => ({
          cellId: resolveCellNo(cell),
          title: cellName(cell),
          meta: unmortgageMode
            ? describeUnmortgageCandidate(cell)
            : describeMortgageCandidate(cell),
          amountLabel: root.monopolyHelpers.money(
            unmortgageMode
              ? unmortgageCost(cell)
              : mortgageValue(cell),
          ),
          action: unmortgageMode ? "unmortgage-property" : "mortgage-property",
        })),
      )}
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
            ? "ถ้าไม่อยากเสี่ยง จ่ายค่าประกันเพื่อออกคุกได้ทันที แต่ถ้าทอยไม่ออก ค่าประกันรอบถัดไปจะเพิ่มเป็น 2 เท่า"
            : "เงินไม่พอค่าประกัน ควรเลือกทอยดับเบิลเพื่อออกจากคุก หรือเตรียมขายทรัพย์เพื่อจ่ายรอบถัดไป",
        );
        break;
      case root.MONOPOLY_PHASE.AWAIT_PURCHASE_DECISION:
        {
          const cell = root.monopolyHelpers.findCell(monopoly?.pendingPurchaseCellId);
          const price = parseIntVal(
            monopoly?.pendingPurchasePrice ?? currentCityPrice(cell),
          );
          const ownerId = String(monopoly?.pendingPurchaseOwnerPlayerId ?? "").trim();
          if (price > 0 && cash < price) {
            tips.push(
              ownerId
                ? `เงินยังไม่พอซื้อต่อ (ขาด ${root.monopolyHelpers.money(price - cash)}) ให้กดไม่ซื้อแล้วไปต่อ`
                : `เงินไม่พอซื้อช่องนี้ (ขาด ${root.monopolyHelpers.money(price - cash)}) ให้เลือกข้ามการซื้อแล้วไปต่อ`,
            );
          } else {
            tips.push(ownerId
              ? "ถ้าซื้อต่อหลังจ่ายค่าผ่านทางแล้ว ช่องนี้จะย้ายเจ้าของทันที"
              : "เช็กเงินคงเหลือหลังซื้อ เพื่อกันพลาดตอนเจอค่าผ่านทางหนักในเทิร์นถัดไป");
          }
        }
        break;
      case root.MONOPOLY_PHASE.AWAIT_MANAGE:
      case root.MONOPOLY_PHASE.AWAIT_END_TURN:
        {
          const landingOpportunity = resolveLandingUpgradeOpportunity(monopoly);
          if (landingOpportunity) {
            tips.push(`เพิ่งมาตกที่ ${landingOpportunity.cellName} และสามารถ${landingOpportunity.shortAction}ได้ทันที`);
          }
        }
        if (pendingDebt > 0) {
          tips.push(`ตอนนี้มีหนี้คงค้าง ${root.monopolyHelpers.money(pendingDebt)} ต้องรื้อสิ่งปลูกสร้างหรือขายอสังหาก่อนเล่นต่อ`);
          if (String(monopoly?.pendingDebtToPlayerId ?? "").trim()) {
            tips.push("หนี้รอบนี้ผูกกับเจ้าหนี้โดยตรง ระบบจะโชว์เฉพาะอสังหาที่โอนให้เขาได้");
          }
        }
        tips.push("แสดงเฉพาะปุ่มที่ทำได้จริงในเทิร์นนี้ เพื่อลดความสับสน");
        tips.push("รอบนี้อัปเกรดได้แค่ 1 ครั้ง และช่องที่เลือกต้องเป็นช่องที่ระบบเปิดสิทธิ์ให้ในเทิร์นนี้");
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

  function renderDebtActionList(items) {
    if (!items || items.length === 0) {
      return `<div class="mono-muted">ไม่มีรายการ</div>`;
    }

    return `
      <div class="mono-action-list">
        ${items.map((item) => `
          <button
            type="button"
            class="btn mono-list-btn"
            data-action="${escapeHtml(item.action)}"
            data-cell-id="${item.cellId}">
            <span class="mono-list-btn-main">
              <span class="mono-list-btn-title">${escapeHtml(item.title)}</span>
              <span class="mono-list-btn-meta">${escapeHtml(item.meta)}</span>
            </span>
            <span class="mono-list-btn-amount">${escapeHtml(item.amountLabel)}</span>
          </button>
        `).join("")}
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

    const cellId = parseIntVal(button.dataset.cellId);
    if (cellId > 0) {
      if (action === "build-property") {
        panelState.manageStep = "menu";
        await root.monopolyHelpers.submitAction(root.GAME_ACTION_TYPES.BUILD_HOUSE, { cellId });
        return;
      }
      if (action === "sell-structure") {
        panelState.manageStep = "menu";
        await root.monopolyHelpers.submitAction(root.GAME_ACTION_TYPES.SELL_HOUSE, { cellId });
        return;
      }
      if (action === "mortgage-property") {
        panelState.manageStep = "menu";
        await root.monopolyHelpers.submitAction(root.GAME_ACTION_TYPES.MORTGAGE, { cellId });
        return;
      }
      if (action === "unmortgage-property") {
        panelState.manageStep = "menu";
        await root.monopolyHelpers.submitAction(root.GAME_ACTION_TYPES.UNMORTGAGE, { cellId });
        return;
      }
      if (action === "sell-property") {
        panelState.manageStep = "menu";
        await root.monopolyHelpers.submitAction(root.GAME_ACTION_TYPES.SELL_PROPERTY, { cellId });
        return;
      }
    }

    await submitSimpleAction(action);
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
      case "end-turn":
        await submit(root.GAME_ACTION_TYPES.END_TURN, {});
        return;
      case "declare-bankruptcy":
        await submit(root.GAME_ACTION_TYPES.DECLARE_BANKRUPTCY, {});
        panelState.manageStep = "menu";
        return;
      case "upgrade-landed-now": {
        const landingOpportunity = resolveLandingUpgradeOpportunity(currentMonopolyState());
        if (!landingOpportunity?.cellId) {
          root.feedback.logEvent("ตอนนี้ยังไม่มีเมืองที่อัปเกรดได้ทันที", true);
          return;
        }

        await submit(root.GAME_ACTION_TYPES.BUILD_HOUSE, {
          cellId: landingOpportunity.cellId,
        });
        return;
      }
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
    const eligibleIds = Array.isArray(monopoly?.upgradeEligibleCellIds)
      ? monopoly.upgradeEligibleCellIds
        .map((value) => parseIntVal(value))
        .filter((value) => value > 0)
      : [];
    const eligibleSet = new Set(eligibleIds);
    if (eligibleSet.size === 0 || Boolean(monopoly?.upgradeUsedThisTurn)) {
      return [];
    }

    return ownedCellsOf(playerId).filter((cell) => {
      if (resolveType(cell) !== 1) {
        return false;
      }

      if (!eligibleSet.has(resolveCellNo(cell))) {
        return false;
      }

      if (Boolean(cell?.isMortgaged ?? cell?.IsMortgaged)) {
        return false;
      }

      const hasHotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
      const landmark = hasLandmark(cell);
      const houseCount = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
      if (landmark) {
        return false;
      }

      const houseCost = houseCostForCell(cell);
      if (hasHotel) {
        return cash >= landmarkCostForCell(cell);
      }

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

      if (hasLandmark(cell)) {
        return true;
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
      return houses <= 0 && !hasHotel && !hasLandmark(cell);
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

  function getDebtSaleCandidates(playerId, monopoly) {
    const creditorId = String(monopoly?.pendingDebtToPlayerId ?? "").trim();
    const debtAmount = parseIntVal(monopoly?.pendingDebtAmount);
    const creditorCash = parseIntVal(
      (state.room?.players ?? []).find((player) => player.playerId === creditorId)?.cash,
    );

    return ownedCellsOf(playerId).filter((cell) => {
      const type = resolveType(cell);
      if (type !== 1 && type !== 2 && type !== 3) {
        return false;
      }

      if (!creditorId) {
        return true;
      }

      if (hasLandmark(cell)) {
        return false;
      }

      const saleValue = estimateDebtSaleValue(cell, monopoly);
      const surplus = Math.max(0, saleValue - debtAmount);
      return surplus <= creditorCash;
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
    if (hasLandmark(cell)) {
      return 6;
    }

    const hasHotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
    if (hasHotel) {
      return 5;
    }
    const houses = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
    return Math.max(0, Math.min(4, houses));
  }

  function resolveLandingUpgradeOpportunity(monopoly) {
    const phase = root.monopolyHelpers.phase();
    if (
      phase !== root.MONOPOLY_PHASE.AWAIT_MANAGE &&
      phase !== root.MONOPOLY_PHASE.AWAIT_END_TURN
    ) {
      return null;
    }

    const eligibleIds = Array.isArray(monopoly?.upgradeEligibleCellIds)
      ? monopoly.upgradeEligibleCellIds
        .map((value) => parseIntVal(value))
        .filter((value) => value > 0)
      : [];
    if (Boolean(monopoly?.upgradeUsedThisTurn) || eligibleIds.length !== 1) {
      return null;
    }

    const cellId = eligibleIds[0];
    if (cellId <= 0) {
      return null;
    }

    const cell = root.monopolyHelpers.findCell(cellId);
    if (!cell || resolveType(cell) !== 1) {
      return null;
    }

    const ownerId = String(cell?.ownerPlayerId ?? cell?.OwnerPlayerId ?? "").trim();
    if (ownerId !== state.playerId) {
      return null;
    }

    const buildable = getBuildCandidates(state.playerId, monopoly, currentPlayerCash(monopoly))
      .some((entry) => resolveCellNo(entry) === cellId);
    if (!buildable) {
      return null;
    }

    const nextStep = describeNextUpgradeStep(cell);
    if (!nextStep) {
      return null;
    }

    return {
      cellId,
      cellName: cellName(cell),
      buttonLabel: nextStep.buttonLabel,
      shortAction: nextStep.shortAction,
      detail: nextStep.detail,
      cost: nextStep.cost,
    };
  }

  function describeNextUpgradeStep(cell) {
    if (hasLandmark(cell)) {
      return null;
    }

    const houses = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
    if (Boolean(cell?.hasHotel ?? cell?.HasHotel)) {
      return {
        buttonLabel: "สร้างแลนด์มาร์กที่นี่ทันที",
        shortAction: "สร้างแลนด์มาร์ก",
        detail: "ช่องนี้มีโรงแรมแล้ว สามารถอัปเป็นแลนด์มาร์กระดับสูงสุดได้ทันที",
        cost: landmarkCostForCell(cell),
      };
    }

    if (houses < 4) {
      return {
        buttonLabel: "อัปเกรดเมืองนี้ทันที",
        shortAction: `สร้างบ้านหลังที่ ${houses + 1}`,
        detail: `เพิ่มบ้านหลังที่ ${houses + 1} ให้เมืองนี้เพื่อดันค่าเช่า`,
        cost: houseCostForCell(cell),
      };
    }

    return {
      buttonLabel: "สร้างโรงแรมที่นี่ทันที",
      shortAction: "สร้างโรงแรม",
      detail: "ช่องนี้ครบ 4 บ้านแล้ว สามารถอัปเกรดเป็นโรงแรมได้ทันที",
      cost: houseCostForCell(cell),
    };
  }

  function hasLandmark(cell) {
    return Boolean(cell?.hasLandmark ?? cell?.HasLandmark);
  }

  function houseCostForCell(cell) {
    return parseIntVal(cell?.houseCost ?? cell?.HouseCost);
  }

  function landmarkCostForCell(cell) {
    return Math.max(0, houseCostForCell(cell) * 2);
  }

  function estimateStructureSellValue(cell) {
    const houseCost = houseCostForCell(cell);
    if (hasLandmark(cell)) {
      return Math.max(1, Math.floor(landmarkCostForCell(cell) / 2));
    }

    if (Boolean(cell?.hasHotel ?? cell?.HasHotel)) {
      return Math.max(1, Math.floor(houseCost / 2));
    }

    return Math.max(1, Math.floor(houseCost / 2));
  }

  function estimateDebtSaleValue(cell, monopoly) {
    const creditorId = String(monopoly?.pendingDebtToPlayerId ?? "").trim();
    const price = currentCityPrice(cell);
    const houses = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
    const mortgaged = Boolean(cell?.isMortgaged ?? cell?.IsMortgaged);

    if (!creditorId) {
      const baseValue = mortgaged
        ? Math.floor(price / 2)
        : Math.max(1, Math.floor(price * 0.5));
      const buildingRefund = hasLandmark(cell)
        ? houseCostForCell(cell) * 4
        : Boolean(cell?.hasHotel ?? cell?.HasHotel)
          ? Math.ceil(houseCostForCell(cell) * 2.5)
          : Math.ceil(houses * houseCostForCell(cell) * 0.5);
      return Math.max(1, baseValue + buildingRefund);
    }

    const baseValue = mortgaged ? Math.floor(price / 2) : price;
    const buildingValue = hasLandmark(cell)
      ? houseCostForCell(cell) * 7
      : Boolean(cell?.hasHotel ?? cell?.HasHotel)
        ? houseCostForCell(cell) * 5
        : houses * houseCostForCell(cell);
    return Math.max(1, baseValue + buildingValue);
  }

  function describeStructureSell(cell) {
    if (hasLandmark(cell)) {
      return "รื้อแลนด์มาร์กออกก่อน เพื่อรับเงินสดเพิ่ม";
    }

    if (Boolean(cell?.hasHotel ?? cell?.HasHotel)) {
      return "ขายโรงแรมลงเป็น 4 บ้าน";
    }

    const houses = parseIntVal(cell?.houseCount ?? cell?.HouseCount);
    return houses > 0
      ? `ขายบ้านออก 1 หลัง (คงเหลือ ${Math.max(0, houses - 1)} หลัง)`
      : "ขายสิ่งปลูกสร้าง";
  }

  function describeDebtSale(cell, monopoly) {
    const creditorId = String(monopoly?.pendingDebtToPlayerId ?? "").trim();
    const level = hasLandmark(cell)
      ? "มีแลนด์มาร์ก"
      : Boolean(cell?.hasHotel ?? cell?.HasHotel)
        ? "มีโรงแรม"
        : parseIntVal(cell?.houseCount ?? cell?.HouseCount) > 0
          ? `บ้าน ${parseIntVal(cell?.houseCount ?? cell?.HouseCount)}`
          : "ไม่มีสิ่งปลูกสร้าง";
    return creditorId
      ? `${level} | โอนให้ ${root.monopolyHelpers.playerName(creditorId)}`
      : `${level} | ปล่อยกลับธนาคาร`;
  }

  function describeBuildCandidate(cell) {
    const next = describeNextUpgradeStep(cell);
    if (!next) {
      return "ตอนนี้ยังอัปเกรดต่อไม่ได้";
    }

    const group = normalizeGroup(cell?.colorGroup ?? cell?.ColorGroup)
      .replace(/-/g, " ")
      .trim();
    const groupLabel = group ? ` | ชุดสี ${group}` : "";
    return `${next.detail}${groupLabel}`;
  }

  function describeMortgageCandidate(cell) {
    return "รับเงินสดทันที แต่ช่องนี้จะเก็บค่าผ่านทางไม่ได้จนกว่าจะไถ่ถอน";
  }

  function describeUnmortgageCandidate(cell) {
    return "จ่ายคืนธนาคารแล้ว ช่องนี้จะกลับมาเก็บค่าผ่านทางได้ตามปกติ";
  }

  function mortgageValue(cell) {
    const price = currentCityPrice(cell);
    return Math.max(0, Math.floor(price / 2));
  }

  function unmortgageCost(cell) {
    const price = currentCityPrice(cell);
    const mortgage = Math.floor(price / 2);
    return Math.ceil(mortgage * 1.1);
  }

  function currentCityPrice(cell) {
    return parseIntVal(
      root.monopolyHelpers?.scaleCityPriceForCell?.(cell) ?? cell?.price ?? cell?.Price,
    );
  }

  function currentMonopolyState() {
    return root.monopolyHelpers?.getMonopolyState?.() ?? null;
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
