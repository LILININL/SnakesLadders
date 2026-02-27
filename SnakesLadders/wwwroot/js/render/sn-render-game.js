(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const { escapeHtml, formatClock, avatarSrc, normalizeAvatarId, boardItemMeta } = root.utils;
  let boardItemTooltipBound = false;

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

      const itemStatus = [];
      if (!waiting && Number.parseInt(String(player.snakeRepellentCharges ?? 0), 10) > 0) {
        itemStatus.push(`กันงู ${player.snakeRepellentCharges}`);
      }
      if (!waiting && player.ladderHackPending) {
        itemStatus.push("LadderHack พร้อม");
      }
      if (!waiting && player.anchorActive) {
        const anchorTurnsLeft = Number.parseInt(String(player.anchorTurnsLeft ?? 0), 10) || 0;
        itemStatus.push(anchorTurnsLeft > 0 ? `Anchor ${anchorTurnsLeft}` : "Anchor");
      }

      const stats = waiting
        ? `สถานะ: <span class="inline-pill ${tone}">${escapeHtml(label)}</span>`
        : `ตำแหน่ง: ${player.position} | โล่: ${player.shields}${itemStatus.length ? ` | ${escapeHtml(itemStatus.join(" / "))}` : ""}`;
      const safeAvatarId = normalizeAvatarId(player.avatarId, 1);

      return `
        <li class="${classes.join(" ")}">
          <div class="player-name-row">
            <img class="inline-avatar" src="${avatarSrc(safeAvatarId)}" alt="Avatar ${safeAvatarId}">
            <strong>${escapeHtml(player.displayName)}</strong>
          </div>
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
      hideBoardItemTooltip();
      clearBoardClasses();
      root.boardOverlay?.clear();
      root.boardTokens?.clear();
      root.boardBeacon?.hide?.();
      return;
    }

    const page = root.boardPage.getVisibleRange(board.size, state.visiblePageStart);
    state.visiblePageStart = page.start;
    root.boardFocus?.refreshPendingBeaconTarget?.();

    const jumps = [...(board.jumps ?? []), ...(board.temporaryJumps ?? [])];
    const activeFrenzySnake = state.animating && state.animFrenzySnake
      ? state.animFrenzySnake
      : board.activeFrenzySnake;
    if (activeFrenzySnake?.type === 0) {
      jumps.push(activeFrenzySnake);
    }
    const itemByCell = new Map((board.items ?? []).map((item) => [item.cell, item]));
    const bananaTrapCells = new Set(board.bananaTrapCells ?? []);
    const jumpsByFrom = new Map(jumps.map((jump) => [jump.from, jump]));
    const snakeHeads = new Set(jumps.filter((x) => x.type === 0).map((x) => x.from));
    const snakeTails = new Set(jumps.filter((x) => x.type === 0).map((x) => x.to));
    const ladderStarts = new Set(jumps.filter((x) => x.type === 1).map((x) => x.from));
    const ladderEnds = new Set(jumps.filter((x) => x.type === 1).map((x) => x.to));
    const forkCells = new Set((board.forkCells ?? []).map((x) => x.cell));

    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const turnPosition = root.viewState.getPlayerPosition(displayTurnPlayerId);

    const parts = [];
    for (let visibleIndex = 1; visibleIndex <= page.pageSize; visibleIndex++) {
      const absoluteCell = page.start + visibleIndex - 1;
      const position = root.boardPage.getGridPositionByVisibleIndex(visibleIndex);

      if (absoluteCell > board.size) {
        parts.push(`
          <div class="cell void" style="grid-column:${position.col};grid-row:${position.row};" aria-hidden="true"></div>
        `);
        continue;
      }

      const jump = jumpsByFrom.get(absoluteCell);
      const jumpCrossPage = Boolean(jump && !root.boardPage.isCellVisible(jump.to, page));
      const isSnakeHead = snakeHeads.has(absoluteCell);
      const isSnakeTail = snakeTails.has(absoluteCell);
      const isLadderStart = ladderStarts.has(absoluteCell);
      const isLadderEnd = ladderEnds.has(absoluteCell);
      const boardItem = itemByCell.get(absoluteCell);
      const trapHere = bananaTrapCells.has(absoluteCell);

      const classes = ["cell"];
      if (isSnakeHead) classes.push("snake-head");
      if (isSnakeTail) classes.push("snake-tail");
      if (isLadderStart) classes.push("ladder-start");
      if (isLadderEnd) classes.push("ladder-end");
      if (forkCells.has(absoluteCell)) classes.push("fork");
      if (turnPosition === absoluteCell) classes.push("turn-cell");
      if (jumpCrossPage) classes.push("cross-jump");
      if (boardItem) classes.push("item-cell");
      if (trapHere) classes.push("trap-cell");

      const marks = [];
      if (isSnakeHead) {
        if (activeFrenzySnake && activeFrenzySnake.from === absoluteCell) {
          marks.push("<span class='jump-tag snake'>🐍⚡</span>");
        } else {
          marks.push("<span class='jump-tag snake'>🐍</span>");
        }
      }
      if (isLadderStart) marks.push("<span class='jump-tag ladder'>🪜</span>");
      if (isSnakeTail) marks.push("<span class='jump-tag snake-end'>▾</span>");
      if (isLadderEnd) marks.push("<span class='jump-tag ladder-end'>▴</span>");
      if (absoluteCell === board.size) marks.push("<span class='jump-tag finish'>🏁</span>");
      if (jumpCrossPage) marks.push(`<span class='jump-tag jump-cross'>ไป ${jump.to}</span>`);
      if (trapHere) marks.push("<span class='jump-tag trap' title='Banana Trap: เหยียบแล้วลื่นถอย'>🍌</span>");
      let itemMeta = null;
      if (boardItem) {
        itemMeta = boardItemMeta(boardItem.type);
        const hasItemImage = Boolean(itemMeta.imageSrc);
        const itemVisual = hasItemImage
          ? `<img class='item-chip-img' src='${escapeHtml(itemMeta.imageSrc)}' alt='${escapeHtml(itemMeta.name)}' loading='lazy' decoding='async'>`
          : "?";
        const itemClass = hasItemImage ? "jump-tag item item-tag-image" : "jump-tag item";
        marks.push(`<span class='${itemClass}' title='${escapeHtml(itemMeta.name)}: ${escapeHtml(itemMeta.desc)}'>${itemVisual}</span>`);
      }

      const cellTitle = [`ช่อง ${absoluteCell}`];
      if (itemMeta) {
        cellTitle.push(`${itemMeta.name}: ${itemMeta.desc}`);
      }
      if (trapHere) {
        cellTitle.push("มีกับดักกล้วย");
      }
      const hoverTip = (itemMeta || trapHere)
        ? cellTitle.slice(1).join(" | ")
        : "";
      const hoverAttr = hoverTip ? ` data-hover-tip="${escapeHtml(hoverTip)}"` : "";

      parts.push(`
        <div class="${classes.join(" ")}" data-cell="${absoluteCell}" data-visible-index="${visibleIndex}" style="grid-column:${position.col};grid-row:${position.row};" title="${escapeHtml(cellTitle.join(" | "))}"${hoverAttr}>
          <div class="num">${absoluteCell}</div>
          <div class="marks">${marks.join("")}</div>
        </div>
      `);
    }

    el.board.style.setProperty("--rows", "10");
    el.board.innerHTML = parts.join("");
    bindBoardItemTooltip();
    applyBoardTransitionClasses();

    const overlayBoard = activeFrenzySnake && board.activeFrenzySnake !== activeFrenzySnake
      ? { ...board, activeFrenzySnake }
      : board;
    root.boardOverlay?.render(overlayBoard, page);
    root.boardTokens?.render(root.viewState.getDisplayPlayers(), displayTurnPlayerId, page);
    root.boardBeacon?.render?.();
    root.roomUi?.updateFloatingRollButton();

    el.boardLegend.textContent = `ช่วง ${page.start}-${page.end} | เส้นชัย ${board.size} | งู ${snakeHeads.size} | บันได ${ladderStarts.size} | ไอเท็ม ${itemByCell.size} | กับดัก ${bananaTrapCells.size}`;
  }

  function renderLastTurn() {
    if (!state.lastTurn) {
      el.turnSummary.textContent = "ยังไม่มีการทอยในรอบนี้";
      return;
    }

    const t = state.lastTurn;
    const lines = [`${playerName(t.playerId)} ทอยได้ ${t.diceValue} เดินจาก ${t.startPosition} -> ${t.endPosition}`];

    if (t.autoRollReason === "Disconnected") lines.push("ผู้เล่นออฟไลน์ ระบบทอยให้อัตโนมัติ");
    if (t.autoRollReason === "TimerExpired") lines.push("หมดเวลาเทิร์น ระบบทอยให้อัตโนมัติ");
    if (t.comebackBoostApplied) {
      const boostAmount = Number.parseInt(String(t.comebackBoostAmount ?? 0), 10) || 0;
      const baseDice = Number.parseInt(String(t.baseDiceValue ?? t.diceValue), 10) || t.diceValue;
      if (boostAmount > 0) {
        lines.push(`ได้โบนัสเร่งแซง +${boostAmount} (${baseDice} -> ${t.diceValue})`);
      } else {
        lines.push(`ได้โบนัสเร่งแซงแต่แต้มชนเพดาน (${baseDice} -> ${t.diceValue})`);
      }
    }
    if (t.usedLuckyReroll) lines.push("ใช้สิทธิ์ทอยซ้ำ");
    if (t.overflowAmount > 0) lines.push(`แต้มเกินเส้นชัย: ${t.overflowAmount}`);
    if (t.forkCell) lines.push(`เจอทางแยกที่ช่อง ${t.forkCell.cell}: เลือกเส้น ${t.forkChoice === 1 ? "เสี่ยงดวง" : "ปลอดภัย"}`);
    if (t.triggeredJump) {
      const jumpKind = t.triggeredJump.type === 0
        ? (t.triggeredJump.isTemporary ? "งูชั่วคราว" : "งู")
        : (t.triggeredJump.isTemporary ? "สะพาน/บันไดชั่วคราว" : "บันได");
      lines.push(`โดนทางลัด: ${jumpKind} ${t.triggeredJump.from} -> ${t.triggeredJump.to}`);
    }
    if (t.frenzySnakeTriggered && t.frenzySnake) {
      lines.push(`งูคลุ้มคลั่งทำงาน: ${t.frenzySnake.from} -> ${t.frenzySnake.to}`);
    } else if (t.frenzySnakeBlockedByShield && t.frenzySnake) {
      lines.push(`งูคลุ้มคลั่งโผล่ที่ ${t.frenzySnake.from} แต่โล่ช่วยกันไว้ได้`);
    } else if (t.frenzySnake) {
      lines.push(`งูคลุ้มคลั่งโผล่: ${t.frenzySnake.from} -> ${t.frenzySnake.to} (รอบนี้ไม่มีใครโดน)`);
    }
    if (t.shieldBlockedSnake) lines.push("เกราะช่วยกันการโดนงูกัดได้สำเร็จ");
    if (t.snakeRepellentBlockedSnake) lines.push("Snake Repellent ช่วยกันงูครั้งนี้ไว้ได้");
    if (t.mercyLadderApplied) lines.push("บันไดเมตตาช่วยดันตำแหน่งขึ้น");
    if (t.ladderHackApplied) lines.push(`Ladder Hack ทำงาน พุ่งเพิ่ม +${t.ladderHackBoostAmount ?? 0}`);
    if (Array.isArray(t.itemEffects) && t.itemEffects.length > 0) {
      for (const effect of t.itemEffects) {
        const meta = boardItemMeta(effect.itemType);
        lines.push(`ไอเท็ม ${meta.name} @${effect.cell}: ${effect.summary ?? "-"}`);
      }
    }
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

  function clearBoardClasses() {
    el.board.classList.remove("page-transitioning", "page-forward", "page-backward");
  }

  function bindBoardItemTooltip() {
    if (boardItemTooltipBound || !el.board) {
      return;
    }

    boardItemTooltipBound = true;
    el.board.addEventListener("mousemove", onBoardMouseMove);
    el.board.addEventListener("mouseleave", hideBoardItemTooltip);
    el.board.addEventListener("scroll", hideBoardItemTooltip, { passive: true });
  }

  function onBoardMouseMove(event) {
    if (!el.boardItemTooltip || !el.boardStage || !el.board) {
      return;
    }

    const cell = event.target?.closest?.(".cell[data-hover-tip]");
    if (!cell || !el.board.contains(cell)) {
      hideBoardItemTooltip();
      return;
    }

    const tip = String(cell.dataset.hoverTip ?? "").trim();
    if (!tip) {
      hideBoardItemTooltip();
      return;
    }

    showBoardItemTooltip(tip, cell);
  }

  function showBoardItemTooltip(text, cell) {
    if (!el.boardItemTooltip) {
      return;
    }

    el.boardItemTooltip.textContent = text;
    el.boardItemTooltip.classList.remove("hidden");
    el.boardItemTooltip.classList.add("show");
    positionBoardItemTooltip(cell);
  }

  function positionBoardItemTooltip(cell) {
    if (!el.boardItemTooltip || !el.boardStage || !cell) {
      return;
    }

    const stageRect = el.boardStage.getBoundingClientRect();
    const cellRect = cell.getBoundingClientRect();
    if (!Number.isFinite(stageRect.width) || stageRect.width <= 0 || !Number.isFinite(cellRect.width)) {
      return;
    }

    const tipEl = el.boardItemTooltip;
    const tipWidth = tipEl.offsetWidth || 180;
    const tipHeight = tipEl.offsetHeight || 48;
    const margin = 8;

    const rawCenterX = cellRect.left - stageRect.left + (cellRect.width / 2);
    const minCenterX = margin + (tipWidth / 2);
    const maxCenterX = Math.max(minCenterX, stageRect.width - margin - (tipWidth / 2));
    const centerX = clamp(rawCenterX, minCenterX, maxCenterX);

    let top = cellRect.top - stageRect.top - tipHeight - 10;
    const wouldOverflowTop = top < margin;
    if (wouldOverflowTop) {
      top = cellRect.bottom - stageRect.top + 10;
      top = Math.min(top, Math.max(margin, stageRect.height - tipHeight - margin));
      tipEl.classList.add("flip");
    } else {
      tipEl.classList.remove("flip");
    }

    tipEl.style.left = `${centerX}px`;
    tipEl.style.top = `${top}px`;
  }

  function hideBoardItemTooltip() {
    if (!el.boardItemTooltip) {
      return;
    }

    el.boardItemTooltip.classList.remove("show", "flip");
    el.boardItemTooltip.classList.add("hidden");
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  function applyBoardTransitionClasses() {
    const transitioning = Boolean(state.pageTransitioning);
    const forward = transitioning && state.pageTransitionDirection > 0;
    const backward = transitioning && state.pageTransitionDirection < 0;

    el.board.classList.toggle("page-transitioning", transitioning);
    el.board.classList.toggle("page-forward", forward);
    el.board.classList.toggle("page-backward", backward);
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
