(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml, normalizeAvatarId, boardItemMeta, syncAvatarHost, preloadImage } =
    root.utils;
  let diceChain = Promise.resolve();
  let turnStartChain = Promise.resolve();
  const ITEM_ACTIVATION = {
    0: {
      toneClass: "fx-rocket",
      stageClass: "item-fx-stage-zoom",
      label: "Rocket Boots!",
    },
    1: {
      toneClass: "fx-magnet",
      stageClass: "item-fx-stage-pulse",
      label: "Magnet Dice!",
    },
    2: {
      toneClass: "fx-repellent",
      stageClass: "item-fx-stage-pulse",
      label: "Snake Repellent!",
    },
    3: {
      toneClass: "fx-ladder",
      stageClass: "item-fx-stage-zoom",
      label: "Ladder Hack!",
    },
    4: {
      toneClass: "fx-banana",
      stageClass: "item-fx-stage-shake",
      label: "Banana Peel!",
    },
    5: {
      toneClass: "fx-swap",
      stageClass: "item-fx-stage-warp",
      label: "Swap Glove!",
    },
    6: {
      toneClass: "fx-anchor",
      stageClass: "item-fx-stage-pulse",
      label: "Anchor!",
    },
    7: {
      toneClass: "fx-chaos",
      stageClass: "item-fx-stage-chaos",
      label: "Chaos Button!",
    },
    8: {
      toneClass: "fx-snake-row",
      stageClass: "item-fx-stage-shake",
      label: "Snake Row!",
    },
    9: {
      toneClass: "fx-bridge",
      stageClass: "item-fx-stage-zoom",
      label: "Bridge to Leader!",
    },
    10: {
      toneClass: "fx-global-snake",
      stageClass: "item-fx-stage-chaos",
      label: "Global Snake Round!",
    },
  };
  const ITEM_FX_STAGE_CLASSES = [
    "item-fx-stage-zoom",
    "item-fx-stage-pulse",
    "item-fx-stage-shake",
    "item-fx-stage-warp",
    "item-fx-stage-chaos",
  ];
  const DICE_POSE_BY_VALUE = {
    1: { x: -16, y: 14 },
    2: { x: -16, y: 194 },
    3: { x: -16, y: 104 },
    4: { x: -16, y: -76 },
    5: { x: 74, y: 14 },
    6: { x: -106, y: 14 },
  };

  function showDice(playerId, diceOne = 0, diceTwo = 0, diceValue = 0, meta = null) {
    diceChain = diceChain
      .then(() => animateDice(playerId, diceOne, diceTwo, diceValue, meta))
      .catch(() => {});
    return diceChain;
  }

  function showWinner(turn, room) {
    if (!el.winnerOverlay || !el.winnerNameText || !el.winnerMetaText) {
      return Promise.resolve();
    }

    const gameResult = room?.gameResult ?? turn?.clientFx?.gameResult ?? null;
    const topPlacement = Array.isArray(gameResult?.placements)
      ? gameResult.placements.find((entry) => Number(entry?.rank) === 1) ?? null
      : null;
    const winnerId = turn?.winnerPlayerId ?? room?.winnerPlayerId;
    const winner = (room?.players ?? []).find((x) => x.playerId === winnerId);
    const winnerName = resolvePlayerName(winnerId, room);
    const winnerAvatarId = normalizeAvatarId(winner?.avatarId, 1);
    el.winnerNameText.textContent = winnerName;
    el.winnerMetaText.textContent =
      String(topPlacement?.outcomeReason ?? "").trim() ||
      winnerMetaText(turn?.finishReason);
    renderOverlayAvatar(el.winnerAvatar, winnerAvatarId, "winner");
    renderWinnerPlacements(gameResult, room);

    el.winnerOverlay.classList.remove("hidden");
    el.winnerOverlay.classList.add("show");
    return wait(gameResult?.placements?.length ? 5200 : 3000)
      .then(() => {
        el.winnerOverlay.classList.remove("show");
        return wait(240);
      })
      .then(() => {
        el.winnerOverlay.classList.add("hidden");
      });
  }

  function showTurnStart(room, playerId = null, badge = "ถึงเทิร์นของ") {
    turnStartChain = turnStartChain
      .then(() =>
        animateTurnStart(
          room,
          playerId ?? room?.currentTurnPlayerId ?? "",
          badge,
        ),
      )
      .catch(() => {});
    return turnStartChain;
  }

  function reset() {
    if (el.diceResultFx) {
      el.diceResultFx.className = "dice-result-fx hidden";
      el.diceResultFx.style.removeProperty("--dice-roll-duration");
      el.diceResultFx.textContent = "";
    }

    if (el.boardEventOverlay) {
      el.boardEventOverlay.className = "board-event-overlay hidden";
    }
    if (el.boardEventCard) {
      el.boardEventCard.innerHTML = "";
    }
    hideFinalDuelVotePrompt();
    if (el.moneyFlowOverlay) {
      el.moneyFlowOverlay.className = "money-flow-overlay hidden";
    }
    if (el.moneyFlowCard) {
      el.moneyFlowCard.innerHTML = "";
    }

    if (el.winnerOverlay) {
      el.winnerOverlay.className = "winner-overlay hidden";
    }
    if (el.winnerPodium) {
      el.winnerPodium.classList.add("hidden");
      el.winnerPodium.innerHTML = "";
    }
    if (el.winnerPlacementList) {
      el.winnerPlacementList.classList.add("hidden");
      el.winnerPlacementList.innerHTML = "";
    }

    if (el.firstTurnOverlay) {
      el.firstTurnOverlay.className = "first-turn-overlay hidden";
    }

    if (el.jumpHintBadge) {
      el.jumpHintBadge.className = "jump-hint-badge hidden";
      el.jumpHintBadge.textContent = "";
    }

    if (el.boardStage) {
      for (const className of ITEM_FX_STAGE_CLASSES) {
        el.boardStage.classList.remove(className);
      }

      for (const fxEl of el.boardStage.querySelectorAll(".item-cast-fx")) {
        fxEl.remove();
      }
    }
  }

  async function animateDice(playerId, diceOne, diceTwo, diceValue, meta = null) {
    if (!el.diceResultFx) {
      return;
    }

    const parsedDiceOne = Number.parseInt(String(diceOne ?? 0), 10) || 0;
    const parsedDiceTwo = Number.parseInt(String(diceTwo ?? 0), 10) || 0;
    const hasTwoDice = parsedDiceOne > 0 && parsedDiceTwo > 0;
    const resolvedDiceOne = normalizeDiceValue(parsedDiceOne);
    const resolvedDiceTwo = normalizeDiceValue(parsedDiceTwo);
    const resolvedDiceValue = normalizeDiceValue(
      Number.parseInt(String(diceValue ?? 0), 10) ||
      (hasTwoDice ? resolvedDiceOne + resolvedDiceTwo : resolvedDiceOne || resolvedDiceTwo || 1),
    );
    const rollPower = resolveRollPower(playerId);
    const durationScale = clamp(
      Number(meta?.durationScale ?? 1) || 1,
      0.15,
      1,
    );
    const rollDurationMs = Math.max(
      220,
      Math.round((1240 + Math.round(rollPower * 260)) * durationScale),
    );
    if (hasTwoDice) {
      const startOne = randomDiceFace(resolvedDiceOne);
      const startTwo = randomDiceFace(resolvedDiceTwo);
      const resolvedTotal =
        Number.parseInt(String(diceValue ?? 0), 10) ||
        resolvedDiceOne + resolvedDiceTwo;
      const status = resolveDiceStatus(meta, resolvedDiceOne, resolvedDiceTwo);
      el.diceResultFx.innerHTML = `
        <span class="dice-fx-pair" aria-hidden="true">
          ${buildDiceCubeMarkup("dice-fx-cube dice-fx-cube-a")}
          <span class="dice-fx-join">+</span>
          ${buildDiceCubeMarkup("dice-fx-cube dice-fx-cube-b")}
        </span>
        <span class="dice-fx-total-label">ทอยได้</span>
        <span class="dice-fx-total-value pending">?</span>
        <span class="dice-fx-double-status pending ${status.toneClass}">${escapeHtml(status.text)}</span>
      `;
      el.diceResultFx.style.setProperty("--dice-roll-duration", `${rollDurationMs}ms`);
      const cubeAEl = el.diceResultFx.querySelector(".dice-fx-cube-a");
      const cubeBEl = el.diceResultFx.querySelector(".dice-fx-cube-b");
      const totalEl = el.diceResultFx.querySelector(".dice-fx-total-value");
      const statusEl = el.diceResultFx.querySelector(".dice-fx-double-status");
      setFxCubeValue(cubeAEl, startOne);
      setFxCubeValue(cubeBEl, startTwo);
      el.diceResultFx.className = "dice-result-fx rolling pair-mode";
      await wait(Math.max(80, Math.round(140 * durationScale)) + rollDurationMs);
      setFxCubeValue(cubeAEl, resolvedDiceOne);
      setFxCubeValue(cubeBEl, resolvedDiceTwo);
      if (totalEl) {
        totalEl.textContent = String(resolvedTotal);
        totalEl.classList.remove("pending");
        totalEl.classList.add("revealed");
      }
      if (statusEl) {
        statusEl.classList.remove("pending");
        statusEl.classList.add("revealed");
      }
      el.diceResultFx.className = "dice-result-fx reveal pair-mode";
      await wait(Math.max(60, Math.round(120 * durationScale)));
      el.diceResultFx.className = "dice-result-fx show pair-mode";
      await wait(Math.max(180, Math.round(980 * durationScale)));
      el.diceResultFx.className = "dice-result-fx hide pair-mode";
      await wait(220);
      el.diceResultFx.className = "dice-result-fx hidden";
      el.diceResultFx.style.removeProperty("--dice-roll-duration");
      el.diceResultFx.textContent = "";
      return;
    }

    const startValue = randomDiceFace(resolvedDiceValue);
    const name = resolvePlayerName(playerId, state.room);
    el.diceResultFx.innerHTML = `
      <span class="dice-fx-name">${escapeHtml(name)}</span>
      <span class="dice-fx-value pending">?</span>
      <span class="dice-fx-value-caption pending">แต้ม</span>
      ${buildDiceCubeMarkup("dice-fx-cube")}
    `;
    el.diceResultFx.style.setProperty("--dice-roll-duration", `${rollDurationMs}ms`);
    const cubeEl = el.diceResultFx.querySelector(".dice-fx-cube");
    const valueEl = el.diceResultFx.querySelector(".dice-fx-value");
    const captionEl = el.diceResultFx.querySelector(".dice-fx-value-caption");
    setFxCubeValue(cubeEl, startValue);
    el.diceResultFx.className = "dice-result-fx rolling";
    await wait(Math.max(80, Math.round(140 * durationScale)) + rollDurationMs);
    setFxCubeValue(cubeEl, resolvedDiceValue);
    if (valueEl) {
      valueEl.textContent = String(resolvedDiceValue);
      valueEl.classList.remove("pending");
      valueEl.classList.add("revealed");
    }
    if (captionEl) {
      captionEl.classList.remove("pending");
    }
    el.diceResultFx.className = "dice-result-fx reveal";
    await wait(Math.max(60, Math.round(140 * durationScale)));
    el.diceResultFx.className = "dice-result-fx show";
    await wait(Math.max(180, Math.round(840 * durationScale)));
    el.diceResultFx.className = "dice-result-fx hide";
    await wait(220);
    el.diceResultFx.className = "dice-result-fx hidden";
    el.diceResultFx.style.removeProperty("--dice-roll-duration");
    el.diceResultFx.textContent = "";
  }

  function resolveDiceStatus(meta, diceOne, diceTwo) {
    const isDouble =
      typeof meta?.isDouble === "boolean"
        ? meta.isDouble
        : diceOne > 0 && diceOne === diceTwo;
    const isJailRoll = Boolean(meta?.isJailRoll);

    if (isJailRoll) {
      return isDouble
        ? {
            text: "ดับเบิ้ล ออกจากคุก",
            toneClass: "is-double",
          }
        : {
            text: "ไม่ดับเบิ้ล ยังอยู่ในคุก",
            toneClass: "not-double",
          };
    }

    return isDouble
      ? {
          text: "ดับเบิ้ล ได้เล่นต่อ",
          toneClass: "is-double",
        }
      : {
          text: "ไม่ดับเบิ้ล",
          toneClass: "not-double",
        };
  }

  function buildDiceCubeMarkup(className = "dice-fx-cube") {
    return `
      <span class="${className}" aria-hidden="true">
        <span class="dice-fx-face face-1">
          <span class="dice-pip pip-mm"></span>
        </span>
        <span class="dice-fx-face face-2">
          <span class="dice-pip pip-tl"></span>
          <span class="dice-pip pip-br"></span>
        </span>
        <span class="dice-fx-face face-3">
          <span class="dice-pip pip-tl"></span>
          <span class="dice-pip pip-mm"></span>
          <span class="dice-pip pip-br"></span>
        </span>
        <span class="dice-fx-face face-4">
          <span class="dice-pip pip-tl"></span>
          <span class="dice-pip pip-tr"></span>
          <span class="dice-pip pip-bl"></span>
          <span class="dice-pip pip-br"></span>
        </span>
        <span class="dice-fx-face face-5">
          <span class="dice-pip pip-tl"></span>
          <span class="dice-pip pip-tr"></span>
          <span class="dice-pip pip-mm"></span>
          <span class="dice-pip pip-bl"></span>
          <span class="dice-pip pip-br"></span>
        </span>
        <span class="dice-fx-face face-6">
          <span class="dice-pip pip-tl"></span>
          <span class="dice-pip pip-ml"></span>
          <span class="dice-pip pip-bl"></span>
          <span class="dice-pip pip-tr"></span>
          <span class="dice-pip pip-mr"></span>
          <span class="dice-pip pip-br"></span>
        </span>
      </span>
    `;
  }

  function setFxCubeValue(cubeEl, value) {
    if (!cubeEl) {
      return;
    }

    const safeValue = normalizeDiceValue(value);
    const pose = DICE_POSE_BY_VALUE[safeValue] ?? DICE_POSE_BY_VALUE[1];
    cubeEl.dataset.value = String(safeValue);
    cubeEl.style.setProperty("--dice-rx", `${pose.x}deg`);
    cubeEl.style.setProperty("--dice-ry", `${pose.y}deg`);
  }

  function resolveRollPower(playerId) {
    if (!playerId || playerId !== state.playerId) {
      return 0.42;
    }

    const hintAgeMs = Date.now() - (state.rollFxPowerHintAt ?? 0);
    const validHint = hintAgeMs >= 0 && hintAgeMs <= 6500;
    if (!validHint) {
      state.rollFxPowerHint = 0;
      state.rollFxPowerHintAt = 0;
      return 0.5;
    }

    const power = clamp01(Number(state.rollFxPowerHint));
    state.rollFxPowerHint = 0;
    state.rollFxPowerHintAt = 0;
    return power || 0.5;
  }

  function normalizeDiceValue(value) {
    const parsed = Number.parseInt(String(value ?? ""), 10);
    if (!Number.isFinite(parsed)) {
      return 1;
    }
    if (parsed <= 1) {
      return 1;
    }
    if (parsed >= 6) {
      return 6;
    }
    return parsed;
  }

  function randomDiceFace(previousFace) {
    const current = normalizeDiceValue(previousFace);
    let next = current;
    while (next === current) {
      next = 1 + Math.floor(Math.random() * 6);
    }
    return next;
  }

  function clamp01(value) {
    if (!Number.isFinite(value)) {
      return 0;
    }
    if (value <= 0) {
      return 0;
    }
    if (value >= 1) {
      return 1;
    }
    return value;
  }

  function resolvePlayerName(playerId, room) {
    if (!playerId) {
      return "ผู้เล่น";
    }

    const players = room?.players ?? state.room?.players ?? [];
    const player = players.find((x) => x.playerId === playerId);
    return player?.displayName ?? playerId;
  }

  function winnerMetaText(finishReason) {
    if (finishReason === "RoundLimit" || finishReason === "RoundLimitNetWorth") {
      return "ชนะด้วยกติกาจำกัดรอบ";
    }
    if (finishReason === "FinalDuelTimeoutNetWorth") {
      return "ชนะ Final Duel ด้วยมูลค่าสุทธิสูงสุด";
    }
    if (finishReason === "FinalDuelBankruptcy") {
      return "ชนะ Final Duel เพราะอีกฝ่ายล้มละลาย";
    }
    if (finishReason === "LastPlayerStanding" || finishReason === "MonopolyLastStanding") {
      return "ยืนหยัดเป็นคนสุดท้าย";
    }
    return "เข้าเส้นชัยได้อย่างเฉียบขาด";
  }

  async function animateTurnStart(room, playerId, badge) {
    if (
      !el.firstTurnOverlay ||
      !el.firstTurnBadge ||
      !el.firstTurnAvatar ||
      !el.firstTurnNameText ||
      !room
    ) {
      return;
    }

    if (!playerId) {
      return;
    }

    const player = (room.players ?? []).find((x) => x.playerId === playerId);
    if (!player) {
      return;
    }

    const safeAvatarId = normalizeAvatarId(player.avatarId, 1);
    const anchorTurnsLeft =
      Number.parseInt(String(player.anchorTurnsLeft ?? 0), 10) || 0;
    const anchorActive = Boolean(player.anchorActive) && anchorTurnsLeft > 0;
    const badgeText = String(badge ?? "ถึงเทิร์นของ");
    el.firstTurnBadge.textContent = anchorActive
      ? `${badgeText} • Anchor x${anchorTurnsLeft}`
      : badgeText;
    renderOverlayAvatar(el.firstTurnAvatar, safeAvatarId, "first-turn");
    el.firstTurnNameText.textContent = player.displayName ?? playerId;
    el.firstTurnOverlay.classList.toggle("anchor-active", anchorActive);

    el.firstTurnOverlay.classList.remove("hidden");
    el.firstTurnOverlay.classList.add("show");
    await wait(anchorActive ? 1980 : 1650);
    el.firstTurnOverlay.classList.remove("show");
    await wait(220);
    el.firstTurnOverlay.classList.add("hidden");
    el.firstTurnOverlay.classList.remove("anchor-active");
  }

  function wait(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  function renderOverlayAvatar(host, avatarId, variant) {
    if (!host) {
      return;
    }

    syncAvatarHost(host, avatarId, {
      className: `${variant}-avatar-media`,
      alt: `Avatar ${normalizeAvatarId(avatarId, 1)}`,
      variant,
    });
  }

  async function showJumpHint(text, durationMs = 600) {
    if (!el.jumpHintBadge) {
      await wait(durationMs);
      return;
    }

    el.jumpHintBadge.textContent = String(text ?? "").trim();
    if (!el.jumpHintBadge.textContent) {
      return;
    }

    el.jumpHintBadge.className = "jump-hint-badge show";
    await wait(durationMs);
    el.jumpHintBadge.className = "jump-hint-badge hide";
    await wait(220);
    el.jumpHintBadge.className = "jump-hint-badge hidden";
    el.jumpHintBadge.textContent = "";
  }

  async function showEventOverlay(notice) {
    if (!notice?.text) {
      return;
    }

    if (!el.boardEventOverlay || !el.boardEventCard) {
      await wait(notice?.holdMs ?? 3200);
      return;
    }

    const tone = String(notice?.tone ?? "event").trim();
    const title = String(notice?.title ?? "เหตุการณ์").trim();
    const text = String(notice?.text ?? "").trim();
    el.boardEventCard.className = `board-event-card tone-${escapeHtml(tone)}`;
    el.boardEventCard.innerHTML = `
      <div class="board-event-kicker">${escapeHtml(title)}</div>
      <div class="board-event-text">${escapeHtml(text)}</div>
    `;
    el.boardEventOverlay.classList.remove("hidden");
    el.boardEventOverlay.classList.add("show");
    await wait(notice?.holdMs ?? 3400);
    el.boardEventOverlay.classList.remove("show");
    el.boardEventOverlay.classList.add("hide");
    await wait(220);
    el.boardEventOverlay.className = "board-event-overlay hidden";
    el.boardEventCard.innerHTML = "";
  }

  function showFinalDuelVotePrompt(room) {
    if (!el.finalDuelVoteOverlay || !el.finalDuelVoteCard) {
      return false;
    }

    const monopoly = room?.monopolyState ?? room?.MonopolyState ?? null;
    const eligible = Boolean(
      monopoly?.isFinalDuelVoteEligible ?? monopoly?.IsFinalDuelVoteEligible,
    );
    const isFinalDuel = Boolean(
      monopoly?.isFinalDuel ?? monopoly?.IsFinalDuel,
    );
    const me = room?.players?.find((player) => player.playerId === state.playerId);
    if (!eligible || isFinalDuel || !me || me.isBot || me.isBankrupt) {
      hideFinalDuelVotePrompt();
      return false;
    }

    const yesCount =
      Number.parseInt(
        String(monopoly?.finalDuelVoteYesCount ?? monopoly?.FinalDuelVoteYesCount ?? 0),
        10,
      ) || 0;
    const required =
      Number.parseInt(
        String(monopoly?.finalDuelVoteRequired ?? monopoly?.FinalDuelVoteRequired ?? 0),
        10,
      ) || 0;
    const pendingStart = Boolean(
      monopoly?.isFinalDuelVotePendingStart ?? monopoly?.IsFinalDuelVotePendingStart,
    );
    const votedIds = Array.isArray(
      monopoly?.finalDuelVotedPlayerIds ?? monopoly?.FinalDuelVotedPlayerIds,
    )
      ? monopoly.finalDuelVotedPlayerIds ?? monopoly.FinalDuelVotedPlayerIds
      : [];
    const hasVoted = votedIds.includes(state.playerId);

    el.finalDuelVoteCard.innerHTML = `
      <div class="final-duel-vote-kicker">Late-Game Vote</div>
      <div class="final-duel-vote-title">โหวตเข้า Final Duel</div>
      <div class="final-duel-vote-copy">${
        pendingStart
          ? "ครบเสียงแล้ว ระบบจะเข้าสู่ Final Duel ตอนต้นเทิร์นถัดไป"
          : "เกมเริ่มยื้อแล้ว ถ้าต้องการเร่งปิดเกม สามารถโหวตเข้า Final Duel ได้"
      }</div>
      <div class="final-duel-vote-meta">
        <span class="final-duel-vote-pill">เสียงตอนนี้ ${yesCount}/${required}</span>
        <span class="final-duel-vote-pill">${pendingStart ? "รอเริ่มโหมด" : "ต้องการเสียงมากกว่าครึ่ง"}</span>
      </div>
      <div class="final-duel-vote-actions">
        <button class="btn btn-primary" type="button" data-final-duel-vote-action="${hasVoted ? "withdraw" : "vote"}">${hasVoted ? "ถอนโหวต" : "โหวตเข้า Final Duel"}</button>
        <button class="btn" type="button" data-final-duel-vote-action="dismiss">ยังไม่โหวตตอนนี้</button>
      </div>
    `;
    el.finalDuelVoteOverlay.className = "final-duel-vote-overlay show";
    return true;
  }

  function hideFinalDuelVotePrompt() {
    if (!el.finalDuelVoteOverlay || !el.finalDuelVoteCard) {
      return;
    }

    el.finalDuelVoteOverlay.className = "final-duel-vote-overlay hidden";
    el.finalDuelVoteCard.innerHTML = "";
  }

  async function showMoneyFlow(moneyEvent) {
    if (!moneyEvent?.playerName) {
      return;
    }

    if (!el.moneyFlowOverlay || !el.moneyFlowCard) {
      await wait(2800);
      return;
    }

    const incoming = Boolean(moneyEvent?.incoming);
    const title = String(moneyEvent?.title ?? (incoming ? "รับเงิน" : "จ่ายเงิน"));
    const detail = String(moneyEvent?.detail ?? "").trim();
    const amount = Number.parseInt(String(moneyEvent?.amount ?? 0), 10) || 0;
    const beforeCash = Number.parseInt(String(moneyEvent?.beforeCash ?? 0), 10) || 0;
    const afterCash = Number.parseInt(String(moneyEvent?.afterCash ?? 0), 10) || 0;

    el.moneyFlowCard.className = `money-flow-card ${incoming ? "incoming" : "outgoing"}`;
    el.moneyFlowCard.innerHTML = `
      <div class="money-flow-title">${escapeHtml(title)}</div>
      <div class="money-flow-player">${escapeHtml(String(moneyEvent.playerName ?? ""))}</div>
      <div class="money-flow-amount">${incoming ? "+" : "-"}${moneyText(amount)}</div>
      <div class="money-flow-balance-label">${incoming ? "เงินคงเหลือใหม่" : "เงินคงเหลือ"}</div>
      <div class="money-flow-balance">${moneyText(beforeCash)}</div>
      ${detail ? `<div class="money-flow-detail">${escapeHtml(detail)}</div>` : ""}
    `;
    const balanceEl = el.moneyFlowCard.querySelector(".money-flow-balance");

    el.moneyFlowOverlay.classList.remove("hidden");
    el.moneyFlowOverlay.classList.add("show");
    const countDurationMs = Math.max(
      180,
      Number.parseInt(String(moneyEvent?.countDurationMs ?? 1200), 10) || 1200,
    );
    const holdMs = Math.max(
      countDurationMs + 220,
      Number.parseInt(String(moneyEvent?.holdMs ?? 3000), 10) || 3000,
    );
    animateNumber(balanceEl, beforeCash, afterCash, countDurationMs, moneyText);
    await wait(holdMs);
    el.moneyFlowOverlay.classList.remove("show");
    el.moneyFlowOverlay.classList.add("hide");
    await wait(220);
    el.moneyFlowOverlay.className = "money-flow-overlay hidden";
    el.moneyFlowCard.innerHTML = "";
  }

  function normalizeItemFxNotice(effect) {
    const itemType = Number.parseInt(String(effect?.itemType ?? "-1"), 10);
    return {
      itemType,
      meta: boardItemMeta(itemType),
      summary: String(effect?.summary ?? "").trim(),
      trapTriggered: Boolean(effect?.isTrapTrigger),
    };
  }

  async function showItemBadge(meta, title, detail, toneClass, durationMs) {
    if (!el.jumpHintBadge) {
      await wait(durationMs);
      return;
    }

    const summaryHtml = detail
      ? `<span class="item-pickup-summary">${escapeHtml(detail)}</span>`
      : "";
    if (meta.imageSrc) {
      void preloadImage?.(meta.imageSrc);
    }
    if (meta.imageSrc) {
      el.jumpHintBadge.innerHTML = `
        <span class="item-pickup-row">
          <img class="item-pickup-img" src="${escapeHtml(meta.imageSrc)}" alt="${escapeHtml(meta.name)}" loading="eager" decoding="async">
          <span class="item-pickup-text">
            <span class="item-pickup-name">${escapeHtml(title)}</span>
            ${summaryHtml}
          </span>
        </span>
      `;
    } else {
      el.jumpHintBadge.innerHTML = `
        <span class="item-pickup-row">
          <span class="item-pickup-text">
            <span class="item-pickup-name">${escapeHtml(title)}</span>
            ${summaryHtml}
          </span>
        </span>
      `;
    }

    el.jumpHintBadge.className = `jump-hint-badge ${toneClass} show`;
    await wait(durationMs);
    el.jumpHintBadge.className = `jump-hint-badge ${toneClass} hide`;
    await wait(220);
    el.jumpHintBadge.className = "jump-hint-badge hidden";
    el.jumpHintBadge.innerHTML = "";
  }

  async function showItemPickup(effect, durationMs = 980) {
    const { meta, summary, trapTriggered } = normalizeItemFxNotice(effect);
    const title = trapTriggered ? "โดน Banana Trap" : `เก็บ ${meta.name}`;
    const detail =
      summary ||
      (trapTriggered ? "กับดักกำลังเริ่มทำงาน" : "กำลังเปิดใช้ไอเท็ม");
    await showItemBadge(
      meta,
      title,
      detail,
      "item-pickup",
      Math.max(760, durationMs),
    );
  }

  async function showItemResult(effect, durationMs = 1500) {
    const { meta, summary, trapTriggered } = normalizeItemFxNotice(effect);
    const title = trapTriggered ? "ผลของ Banana Trap" : `ผลของ ${meta.name}`;
    const detail = summary || "ไอเท็มทำงานแล้ว";
    const holdMs = Math.max(
      durationMs,
      detail ? Math.min(3200, 1350 + detail.length * 18) : 1200,
    );
    await showItemBadge(meta, title, detail, "item-result", holdMs);
  }

  async function showItemActivation(effect, durationMs = 560) {
    if (!el.boardStage) {
      await wait(durationMs);
      return;
    }

    const { itemType, meta, summary, trapTriggered } =
      normalizeItemFxNotice(effect);
    const activation = ITEM_ACTIVATION[itemType] ?? {
      toneClass: "fx-generic",
      stageClass: "",
      label: "Item Activated!",
    };
    const title = trapTriggered
      ? "Banana Trap!"
      : activation.label || meta.name;
    const sub = trapTriggered
      ? "กับดักกำลังทำงาน..."
      : summary || "กำลังแสดงพลังไอเท็ม";
    const holdMs = Math.max(380, durationMs);
    if (meta.imageSrc) {
      void preloadImage?.(meta.imageSrc);
    }
    const fxEl = document.createElement("div");
    fxEl.className = `item-cast-fx ${activation.toneClass}`;
    fxEl.innerHTML = `
      <span class="item-cast-core">
        ${meta.imageSrc ? `<img class="item-cast-img" src="${escapeHtml(meta.imageSrc)}" alt="${escapeHtml(meta.name)}" loading="eager" decoding="async">` : "<span class='item-cast-fallback'>?</span>"}
        <span class="item-cast-copy">
          <span class="item-cast-title">${escapeHtml(title)}</span>
          <span class="item-cast-sub">${escapeHtml(sub)}</span>
        </span>
      </span>
    `;

    if (activation.stageClass) {
      el.boardStage.classList.add(activation.stageClass);
    }
    el.boardStage.appendChild(fxEl);
    void fxEl.offsetWidth;
    fxEl.classList.add("show");
    await wait(holdMs);
    fxEl.classList.remove("show");
    fxEl.classList.add("hide");
    await wait(220);
    fxEl.remove();
    if (activation.stageClass) {
      el.boardStage.classList.remove(activation.stageClass);
    }
  }

  function renderWinnerPlacements(gameResult, room) {
    if (!el.winnerPodium || !el.winnerPlacementList) {
      return;
    }

    const placements = Array.isArray(gameResult?.placements)
      ? gameResult.placements
      : [];
    if (placements.length === 0) {
      el.winnerPodium.classList.add("hidden");
      el.winnerPodium.innerHTML = "";
      el.winnerPlacementList.classList.add("hidden");
      el.winnerPlacementList.innerHTML = "";
      return;
    }

    const podium = placements.slice(0, 3);
    el.winnerPodium.innerHTML = podium
      .map(
        (entry) => `
          <div class="winner-podium-card rank-${escapeHtml(String(entry.rank ?? ""))}">
            <div class="winner-podium-rank">อันดับ ${escapeHtml(String(entry.rank ?? ""))}</div>
            <div class="winner-podium-name">${escapeHtml(String(entry.displayName ?? ""))}</div>
          </div>
        `,
      )
      .join("");
    el.winnerPodium.classList.remove("hidden");

    const isMonopoly =
      String(room?.gameKey ?? "").trim().toLowerCase() ===
      String(root.GAME_KEYS?.MONOPOLY ?? "monopoly").trim().toLowerCase();
    el.winnerPlacementList.innerHTML = placements
      .map(
        (entry) => `
          <div class="winner-placement-row">
            <div class="winner-placement-head">
              <strong>#${escapeHtml(String(entry.rank ?? ""))} ${escapeHtml(String(entry.displayName ?? ""))}</strong>
              <span>${isMonopoly ? moneyText(entry.netWorth ?? 0) : `ช่อง ${escapeHtml(String(entry.netWorth ?? 0))}`}</span>
            </div>
            <div class="winner-placement-meta">${escapeHtml(String(entry.outcomeReason ?? ""))}</div>
          </div>
        `,
      )
      .join("");
    el.winnerPlacementList.classList.remove("hidden");
  }

  function animateNumber(targetEl, fromValue, toValue, durationMs, formatter) {
    if (!targetEl) {
      return;
    }

    const start = performance.now();
    const safeDuration = Math.max(240, durationMs);
    const render = (timestamp) => {
      const progress = Math.min(1, (timestamp - start) / safeDuration);
      const eased = 1 - Math.pow(1 - progress, 3);
      const current = Math.round(fromValue + (toValue - fromValue) * eased);
      targetEl.textContent = formatter(current);
      if (progress < 1) {
        requestAnimationFrame(render);
      }
    };
    requestAnimationFrame(render);
  }

  function moneyText(value) {
    const amount = Number.parseInt(String(value ?? 0), 10) || 0;
    const abs = Math.abs(amount).toLocaleString("th-TH");
    return amount < 0 ? `-฿${abs}` : `฿${abs}`;
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  root.boardFx = {
    showDice,
    showWinner,
    showTurnStart,
    showJumpHint,
    showEventOverlay,
    showFinalDuelVotePrompt,
    hideFinalDuelVotePrompt,
    showMoneyFlow,
    showItemActivation,
    showItemPickup,
    showItemResult,
    reset,
  };
})();
