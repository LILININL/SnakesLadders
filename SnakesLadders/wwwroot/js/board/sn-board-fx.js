(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml, avatarSrc, normalizeAvatarId, boardItemMeta } =
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

  function showDice(playerId, diceValue) {
    diceChain = diceChain
      .then(() => animateDice(playerId, diceValue))
      .catch(() => {});
    return diceChain;
  }

  function showWinner(turn, room) {
    if (!el.winnerOverlay || !el.winnerNameText || !el.winnerMetaText) {
      return Promise.resolve();
    }

    const winnerId = turn?.winnerPlayerId ?? room?.winnerPlayerId;
    const winner = (room?.players ?? []).find((x) => x.playerId === winnerId);
    const winnerName = resolvePlayerName(winnerId, room);
    const winnerAvatarId = normalizeAvatarId(winner?.avatarId, 1);
    el.winnerNameText.textContent = winnerName;
    el.winnerMetaText.textContent = winnerMetaText(turn?.finishReason);
    if (el.winnerAvatar) {
      el.winnerAvatar.src = avatarSrc(winnerAvatarId);
      el.winnerAvatar.alt = `Avatar ${winnerAvatarId}`;
    }

    el.winnerOverlay.classList.remove("hidden");
    el.winnerOverlay.classList.add("show");
    return wait(3000)
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
      el.diceResultFx.textContent = "";
    }

    if (el.winnerOverlay) {
      el.winnerOverlay.className = "winner-overlay hidden";
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

  async function animateDice(playerId, diceValue) {
    if (!el.diceResultFx) {
      return;
    }

    const name = resolvePlayerName(playerId, state.room);
    el.diceResultFx.innerHTML = `
      <span class="dice-fx-name">${escapeHtml(name)}</span>
      <span class="dice-fx-value">${escapeHtml(String(diceValue))}</span>
    `;
    el.diceResultFx.className = "dice-result-fx show";
    await wait(900);
    el.diceResultFx.className = "dice-result-fx hide";
    await wait(220);
    el.diceResultFx.className = "dice-result-fx hidden";
    el.diceResultFx.textContent = "";
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
    if (finishReason === "RoundLimit") {
      return "ชนะด้วยกติกาจำกัดรอบ";
    }
    if (finishReason === "LastPlayerStanding") {
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
    el.firstTurnAvatar.src = avatarSrc(safeAvatarId);
    el.firstTurnAvatar.alt = `Avatar ${safeAvatarId}`;
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

  root.boardFx = {
    showDice,
    showWinner,
    showTurnStart,
    showJumpHint,
    showItemActivation,
    showItemPickup,
    showItemResult,
    reset,
  };
})();
