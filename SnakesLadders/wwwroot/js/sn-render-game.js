(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml, formatClock } = root.utils;

  function renderRoomHeader() {
    if (!state.room) {
      el.roomTitle.textContent = "Room: -";
      el.roomMeta.textContent = "Create or join a room first.";
      return;
    }

    const statusLabel = state.room.status === GAME_STATUS.WAITING ? "Waiting" :
      state.room.status === GAME_STATUS.STARTED ? "Started" : "Finished";

    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const turnPlayer = state.room.players.find((x) => x.playerId === displayTurnPlayerId);
    const deadline = state.room.turnDeadlineUtc ? ` | Deadline: ${formatClock(state.room.turnDeadlineUtc)}` : "";

    el.roomTitle.textContent = `Room: ${state.room.roomCode}`;
    el.roomMeta.textContent = `Status: ${statusLabel} | Turn: ${state.room.turnCounter} | Current: ${turnPlayer?.displayName ?? "-"}${deadline}`;
  }

  function renderPlayers() {
    if (!state.room) {
      el.playerList.innerHTML = "";
      return;
    }

    const waiting = state.room.status === GAME_STATUS.WAITING;
    const hostId = state.room.hostPlayerId;
    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const displayPlayers = root.viewState.getDisplayPlayers();
    const rows = displayPlayers.map((player) => {
      const classes = ["player-item"];
      if (player.playerId === state.playerId) {
        classes.push("me");
      }
      if (player.playerId === displayTurnPlayerId) {
        classes.push("turn");
      }

      let tone = "";
      let label = "";
      if (waiting) {
        tone = player.playerId === hostId ? "host" : player.connected ? (player.isReady ? "ready" : "not-ready") : "offline";
        label = tone === "host" ? "Host" : tone === "ready" ? "Ready" : tone === "offline" ? "Offline" : "Not Ready";
        classes.push(`state-${tone}`);
      }

      const stats = waiting
        ? `Status: <span class="inline-pill ${tone}">${escapeHtml(label)}</span>`
        : `Position: ${player.position} | Shields: ${player.shields}`;

      return `
        <li class="${classes.join(" ")}">
          <strong>${escapeHtml(player.displayName)}</strong>
          <br>
          ${stats}
          ${player.connected ? "" : "<br><em>Disconnected</em>"}
        </li>
      `;
    });

    el.playerList.innerHTML = rows.join("");
  }

  function renderBoard() {
    const board = state.room?.board;
    if (!board) {
      el.board.style.setProperty("--rows", "10");
      el.board.innerHTML = "<div class='summary-card'>Board will appear after game starts.</div>";
      el.boardLegend.textContent = "";
      root.boardOverlay?.clear();
      root.boardTokens?.clear();
      return;
    }

    const size = board.size;
    const cols = 10;
    const rows = Math.ceil(size / cols);
    el.board.style.setProperty("--rows", String(rows));

    const snakeHeads = new Set(board.jumps.filter((x) => x.type === 0).map((x) => x.from));
    const snakeTails = new Set(board.jumps.filter((x) => x.type === 0).map((x) => x.to));
    const ladderStarts = new Set(board.jumps.filter((x) => x.type === 1).map((x) => x.from));
    const ladderEnds = new Set(board.jumps.filter((x) => x.type === 1).map((x) => x.to));
    const forkCells = new Set(board.forkCells.map((x) => x.cell));
    const finishCell = size;
    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const displayPlayers = root.viewState.getDisplayPlayers();

    const currentPlayer = displayPlayers.find((x) => x.playerId === displayTurnPlayerId);
    const parts = [];

    for (let cell = 1; cell <= size; cell++) {
      const isSnakeHead = snakeHeads.has(cell);
      const isSnakeTail = snakeTails.has(cell);
      const isLadderStart = ladderStarts.has(cell);
      const isLadderEnd = ladderEnds.has(cell);

      const classes = ["cell"];
      if (isSnakeHead) classes.push("snake-head");
      if (isSnakeTail) classes.push("snake-tail");
      if (isLadderStart) classes.push("ladder-start");
      if (isLadderEnd) classes.push("ladder-end");
      if (forkCells.has(cell)) classes.push("fork");
      if (cell === currentPlayer?.position) classes.push("turn-cell");

      const marks = [];
      if (isSnakeHead) marks.push("<span class='jump-tag snake'>S</span>");
      if (isLadderStart) marks.push("<span class='jump-tag ladder'>L</span>");
      if (isSnakeTail) marks.push("<span class='jump-tag snake-end'>s</span>");
      if (isLadderEnd) marks.push("<span class='jump-tag ladder-end'>l</span>");
      if (cell === finishCell) marks.push("<span class='jump-tag finish'>🏁</span>");

      const pos = getGridPosition(cell, cols, rows);
      parts.push(`
        <div class="${classes.join(" ")}" data-cell="${cell}" style="grid-column:${pos.col};grid-row:${pos.row};" title="Cell ${cell}">
          <div class="num">${cell}</div>
          <div class="marks">${marks.join("")}</div>
        </div>
      `);
    }

    el.board.innerHTML = parts.join("");
    root.boardOverlay?.render(board);
    root.boardTokens?.render(displayPlayers, displayTurnPlayerId);
    root.roomUi?.updateFloatingRollButton();
    el.boardLegend.textContent = `Size: ${size} | Snakes: ${snakeHeads.size} | Ladders: ${ladderStarts.size} | Forks: ${forkCells.size} | S/L = jump starts`;
  }

  function renderLastTurn() {
    if (!state.lastTurn) {
      el.turnSummary.textContent = "No turns yet.";
      return;
    }

    const t = state.lastTurn;
    const lines = [`${playerName(t.playerId)} rolled ${t.diceValue}, moved ${t.startPosition} -> ${t.endPosition}.`];

    if (t.comebackBoostApplied) lines.push("Comeback boost applied.");
    if (t.usedLuckyReroll) lines.push("Lucky reroll used.");
    if (t.overflowAmount > 0) lines.push(`Overflow: ${t.overflowAmount}.`);
    if (t.forkCell) lines.push(`Fork at ${t.forkCell.cell}: ${t.forkChoice === 1 ? "Risky" : "Safe"}.`);
    if (t.triggeredJump) lines.push(`Jump: ${t.triggeredJump.type === 0 ? "Snake" : "Ladder"} ${t.triggeredJump.from} -> ${t.triggeredJump.to}.`);
    if (t.shieldBlockedSnake) lines.push("Shield blocked snake hit.");
    if (t.mercyLadderApplied) lines.push("Mercy ladder applied.");
    if (t.shieldsEarned > 0) lines.push(`Shields gained: +${t.shieldsEarned}.`);
    if (t.isGameFinished) lines.push(`Winner: ${playerName(t.winnerPlayerId)}`);

    el.turnSummary.innerHTML = lines.map((line) => `<div>${escapeHtml(line)}</div>`).join("");
  }

  function updateActionState() {
    if (state.requireNamePrompt) {
      el.startGameBtn.disabled = true;
      el.leaveRoomBtn.disabled = true;
      el.refreshRoomBtn.disabled = true;
      el.rollDiceFloatingBtn.disabled = true;
      return;
    }

    const inRoom = Boolean(state.roomCode && state.room);
    const started = state.room?.status === GAME_STATUS.STARTED;
    const myTurn = started &&
      !state.animating &&
      root.viewState.getDisplayTurnPlayerId() === state.playerId;

    el.startGameBtn.disabled = !inRoom || started || !root.readyUi.canStartGame();
    el.leaveRoomBtn.disabled = !inRoom;
    el.refreshRoomBtn.disabled = !inRoom;
    el.rollDiceFloatingBtn.disabled = !myTurn;
  }

  function getGridPosition(cell, cols, totalRows) {
    const rowIndex = Math.floor((cell - 1) / cols);
    const colInRow = (cell - 1) % cols;
    const reverse = rowIndex % 2 === 1;
    const col = reverse ? cols - colInRow : colInRow + 1;
    const row = totalRows - rowIndex;
    return { row, col };
  }

  function playerName(playerId) {
    if (!playerId) {
      return "-";
    }
    const player = state.room?.players.find((x) => x.playerId === playerId);
    return player?.displayName ?? playerId;
  }

  root.renderGame = {
    renderRoomHeader,
    renderPlayers,
    renderBoard,
    renderLastTurn,
    updateActionState
  };
})();
