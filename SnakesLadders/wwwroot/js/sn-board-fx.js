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
      return "Player";
    }

    const players = room?.players ?? state.room?.players ?? [];
    const player = players.find((x) => x.playerId === playerId);
    return player?.displayName ?? playerId;
  }

  function winnerMetaText(finishReason) {
    if (finishReason === "RoundLimit") {
      return "wins by round limit";
    }
    if (finishReason === "LastPlayerStanding") {
      return "last player standing";
    }
    return "reached the finish";
  }

  function wait(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  root.boardFx = {
    showDice,
    showWinner,
    reset
  };
})();
