(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml, formatClock } = root.utils;

  function renderRoomHeader() {
    if (!state.room) {
      el.roomTitle.textContent = "ห้อง: -";
      el.roomMeta.textContent = "สร้างห้องหรือเข้าห้องก่อนเริ่มเล่น";
      return;
    }

    const statusLabel = state.room.status === GAME_STATUS.WAITING ? "รอเริ่มเกม" :
      state.room.status === GAME_STATUS.STARTED ? "กำลังเล่น" : "จบเกมแล้ว";

    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const turnPlayer = state.room.players.find((x) => x.playerId === displayTurnPlayerId);
    const deadline = state.room.turnDeadlineUtc ? ` | หมดเวลา: ${formatClock(state.room.turnDeadlineUtc)}` : "";

    el.roomTitle.textContent = `ห้อง: ${state.room.roomCode}`;
    el.roomMeta.textContent = `สถานะ: ${statusLabel} | เทิร์นที่: ${state.room.turnCounter} | ตาปัจจุบัน: ${turnPlayer?.displayName ?? "-"}${deadline}`;
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
        label = tone === "host" ? "หัวห้อง" : tone === "ready" ? "พร้อม" : tone === "offline" ? "ออฟไลน์" : "ยังไม่พร้อม";
        classes.push(`state-${tone}`);
      }

      const stats = waiting
        ? `สถานะ: <span class="inline-pill ${tone}">${escapeHtml(label)}</span>`
        : `ตำแหน่ง: ${player.position} | โล่: ${player.shields}`;

      return `
        <li class="${classes.join(" ")}">
          <strong>${escapeHtml(player.displayName)}</strong>
          <br>
          ${stats}
          ${player.connected ? "" : "<br><em>หลุดการเชื่อมต่อ</em>"}
        </li>
      `;
    });

    el.playerList.innerHTML = rows.join("");
  }

  function renderBoard() {
    const board = state.room?.board;
    if (!board) {
      el.board.style.setProperty("--rows", "10");
      el.board.innerHTML = "";
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
      if (isSnakeHead) marks.push("<span class='jump-tag snake'>🐍</span>");
      if (isLadderStart) marks.push("<span class='jump-tag ladder'>🪜</span>");
      if (isSnakeTail) marks.push("<span class='jump-tag snake-end'>▾</span>");
      if (isLadderEnd) marks.push("<span class='jump-tag ladder-end'>▴</span>");
      if (cell === finishCell) marks.push("<span class='jump-tag finish'>🏁</span>");

      const pos = getGridPosition(cell, cols, rows);
      parts.push(`
        <div class="${classes.join(" ")}" data-cell="${cell}" style="grid-column:${pos.col};grid-row:${pos.row};" title="ช่อง ${cell}">
          <div class="num">${cell}</div>
          <div class="marks">${marks.join("")}</div>
        </div>
      `);
    }

    el.board.innerHTML = parts.join("");
    root.boardOverlay?.render(board);
    root.boardTokens?.render(displayPlayers, displayTurnPlayerId);
    root.roomUi?.updateFloatingRollButton();
    el.boardLegend.textContent = `ขนาด: ${size} | งู: ${snakeHeads.size} | บันได: ${ladderStarts.size} | ทางแยก: ${forkCells.size} | 🐍/🪜 = จุดเริ่มกระโดด`;
  }

  function renderLastTurn() {
    if (!state.lastTurn) {
      el.turnSummary.textContent = "ยังไม่มีการทอยในรอบนี้";
      return;
    }

    const t = state.lastTurn;
    const lines = [`${playerName(t.playerId)} ทอยได้ ${t.diceValue} เดินจาก ${t.startPosition} -> ${t.endPosition}`];

    if (t.comebackBoostApplied) lines.push("ได้โบนัสเร่งแซงจากกติกา");
    if (t.usedLuckyReroll) lines.push("ใช้สิทธิ์ทอยซ้ำ");
    if (t.overflowAmount > 0) lines.push(`แต้มเกินเส้นชัย: ${t.overflowAmount}`);
    if (t.forkCell) lines.push(`เจอทางแยกที่ช่อง ${t.forkCell.cell}: เลือกเส้น ${t.forkChoice === 1 ? "เสี่ยงดวง" : "ปลอดภัย"}`);
    if (t.triggeredJump) lines.push(`โดนทางลัด: ${t.triggeredJump.type === 0 ? "งู" : "บันได"} ${t.triggeredJump.from} -> ${t.triggeredJump.to}`);
    if (t.shieldBlockedSnake) lines.push("เกราะช่วยกันการโดนงูกัดได้สำเร็จ");
    if (t.mercyLadderApplied) lines.push("บันไดเมตตาช่วยดันตำแหน่งขึ้น");
    if (t.shieldsEarned > 0) lines.push(`ได้รับโล่เพิ่ม: +${t.shieldsEarned}`);
    if (t.isGameFinished) lines.push(`ผู้ชนะ: ${playerName(t.winnerPlayerId)}`);

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
