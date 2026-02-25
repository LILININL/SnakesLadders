(() => {
  const root = window.SNL;
  const { state, el } = root;
  const { escapeHtml } = root.utils;
  let diceChain = Promise.resolve();

  function showDice(playerId, diceValue) {
    diceChain = diceChain.then(() => animateDice(playerId, diceValue)).catch(() => {});
    return diceChain;
  }

  function showWinner(turn, room) {
    if (!el.winnerOverlay || !el.winnerNameText || !el.winnerMetaText) {
      return Promise.resolve();
    }

    const winnerId = turn?.winnerPlayerId ?? room?.winnerPlayerId;
    const winnerName = resolvePlayerName(winnerId, room);
    el.winnerNameText.textContent = winnerName;
    el.winnerMetaText.textContent = winnerMetaText(turn?.finishReason);

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

  function reset() {
    if (el.diceResultFx) {
      el.diceResultFx.className = "dice-result-fx hidden";
      el.diceResultFx.textContent = "";
    }

    if (el.winnerOverlay) {
      el.winnerOverlay.className = "winner-overlay hidden";
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

  root.boardFx = {
    showDice,
    showWinner,
    showJumpHint,
    reset
  };
})();
