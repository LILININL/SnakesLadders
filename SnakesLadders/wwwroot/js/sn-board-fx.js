(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml, avatarSrc, normalizeAvatarId, boardItemMeta } = root.utils;
  let diceChain = Promise.resolve();
  let turnStartChain = Promise.resolve();

  function showDice(playerId, diceValue) {
    diceChain = diceChain.then(() => animateDice(playerId, diceValue)).catch(() => {});
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
      .then(() => animateTurnStart(room, playerId ?? room?.currentTurnPlayerId ?? "", badge))
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
    if (!el.firstTurnOverlay || !el.firstTurnBadge || !el.firstTurnAvatar || !el.firstTurnNameText || !room) {
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
    el.firstTurnBadge.textContent = String(badge ?? "ถึงเทิร์นของ");
    el.firstTurnAvatar.src = avatarSrc(safeAvatarId);
    el.firstTurnAvatar.alt = `Avatar ${safeAvatarId}`;
    el.firstTurnNameText.textContent = player.displayName ?? playerId;

    el.firstTurnOverlay.classList.remove("hidden");
    el.firstTurnOverlay.classList.add("show");
    await wait(1650);
    el.firstTurnOverlay.classList.remove("show");
    await wait(220);
    el.firstTurnOverlay.classList.add("hidden");
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

  async function showItemPickup(effect, durationMs = 780) {
    if (!el.jumpHintBadge) {
      await wait(durationMs);
      return;
    }

    const meta = boardItemMeta(effect?.itemType);
    if (meta.imageSrc) {
      el.jumpHintBadge.innerHTML = `
        <span class="item-pickup-row">
          <img class="item-pickup-img" src="${escapeHtml(meta.imageSrc)}" alt="${escapeHtml(meta.name)}" loading="eager" decoding="async">
          <span class="item-pickup-name">${escapeHtml(meta.name)}</span>
        </span>
      `;
    } else {
      el.jumpHintBadge.textContent = `${meta.icon} ${meta.name}`;
    }

    el.jumpHintBadge.className = "jump-hint-badge item-pickup show";
    await wait(durationMs);
    el.jumpHintBadge.className = "jump-hint-badge item-pickup hide";
    await wait(220);
    el.jumpHintBadge.className = "jump-hint-badge hidden";
    el.jumpHintBadge.textContent = "";
  }

  root.boardFx = {
    showDice,
    showWinner,
    showTurnStart,
    showJumpHint,
    showItemPickup,
    reset
  };
})();
