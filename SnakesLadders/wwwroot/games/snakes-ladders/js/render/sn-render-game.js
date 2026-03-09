(() => {
  const root = window.SNL;
  const { state, el, GAME_STATUS } = root;
  const {
    escapeHtml,
    formatClock,
    avatarSrc,
    normalizeAvatarId,
    gameLabel,
    boardItemMeta,
    normalizeGameKey,
    resolvePlayerAccent,
  } = root.utils;
  const MONOPOLY_GAME_KEY = normalizeGameKey(
    root.GAME_KEYS?.MONOPOLY ?? "monopoly",
  );
  const DEFAULT_GAME_KEY = normalizeGameKey(
    root.GAME_KEYS?.SNAKES_LADDERS ?? "snakes-ladders",
  );
  const viewCache = {
    playerListHtml: "",
  };

  function renderRoomHeader() {
    if (!state.room) {
      el.roomTitle.textContent = "ห้อง: -";
      el.roomMeta.textContent = "สร้างห้องหรือเข้าห้องก่อนเริ่มเล่น";
      return;
    }

    const statusLabel =
      state.room.status === GAME_STATUS.WAITING
        ? "รอเริ่มเกม"
        : state.room.status === GAME_STATUS.STARTED
          ? "กำลังเล่น"
          : "จบเกมแล้ว";

    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const turnPlayer = state.room.players.find(
      (x) => x.playerId === displayTurnPlayerId,
    );
    const deadline = state.room.turnDeadlineUtc
      ? ` | หมดเวลา: ${formatClock(state.room.turnDeadlineUtc)}`
      : "";
    const gameName = gameLabel(state.room.gameKey);

    el.roomTitle.textContent = `ห้อง: ${state.room.roomCode}`;
    el.roomMeta.textContent = `เกม: ${gameName} | สถานะ: ${statusLabel} | เทิร์นที่: ${state.room.turnCounter} | ตาปัจจุบัน: ${turnPlayer?.displayName ?? "-"}${deadline}`;
  }

  function renderPlayers() {
    if (!state.room) {
      el.playerList.innerHTML = "";
      viewCache.playerListHtml = "";
      return;
    }

    const monopoly = isMonopolyGame();
    const waiting = state.room.status === GAME_STATUS.WAITING;
    const hostId = state.room.hostPlayerId;
    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const displayPlayers = root.viewState.getDisplayPlayers();
    const monopolyCells = getMonopolyCells(state.room?.board);
    const economyRows = Array.isArray(state.room?.monopolyState?.playerEconomy)
      ? state.room.monopolyState.playerEconomy
      : [];
    const economyById = new Map(
      economyRows.map((entry) => [entry.playerId, entry]),
    );

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
        tone =
          player.playerId === hostId
            ? "host"
            : player.connected
              ? player.isReady
                ? "ready"
                : "not-ready"
              : "offline";
        label =
          tone === "host"
            ? "หัวห้อง"
            : tone === "ready"
              ? "พร้อม"
              : tone === "offline"
                ? "ออฟไลน์"
                : "ยังไม่พร้อม";
        classes.push(`state-${tone}`);
      }

      const itemStatus = [];
      if (
        !waiting &&
        Number.parseInt(String(player.snakeRepellentCharges ?? 0), 10) > 0
      ) {
        itemStatus.push(`กันงู ${player.snakeRepellentCharges}`);
      }
      if (!waiting && player.ladderHackPending) {
        itemStatus.push("LadderHack พร้อม");
      }
      if (!waiting && player.anchorActive) {
        const anchorTurnsLeft =
          Number.parseInt(String(player.anchorTurnsLeft ?? 0), 10) || 0;
        itemStatus.push(
          anchorTurnsLeft > 0 ? `Anchor ${anchorTurnsLeft}` : "Anchor",
        );
      }

      let stats = "";
      if (waiting) {
        stats = `สถานะ: <span class="inline-pill ${tone}">${escapeHtml(label)}</span>`;
      } else if (monopoly) {
        const ownedCount = countMonopolyAssets(monopolyCells, player.playerId);
        const economy = economyById.get(player.playerId) ?? {};
        const cash = Number.parseInt(String(player.cash ?? 0), 10) || 0;
        const netWorth = Number.parseInt(String(economy.netWorth ?? cash), 10) || 0;
        const houses = Number.parseInt(String(economy.houses ?? 0), 10) || 0;
        const hotels = Number.parseInt(String(economy.hotels ?? 0), 10) || 0;
        const landmarks = Number.parseInt(String(economy.landmarks ?? 0), 10) || 0;
        const mortgaged = Number.parseInt(String(economy.mortgaged ?? 0), 10) || 0;
        const sets = Number.parseInt(String(economy.monopolySetCount ?? 0), 10) || 0;
        const jailTurns =
          Number.parseInt(String(player.jailTurnsRemaining ?? 0), 10) || 0;
        const bankrupt = Boolean(player.isBankrupt);
        const extra = bankrupt
          ? " | สถานะ: ล้มละลาย"
          : jailTurns > 0
            ? ` | ติดคุกอีก ${jailTurns} ตา`
            : "";
        stats = `
          <div class="mono-player-inline">
            <span>เงินสด <b>${monopolyMoney(cash)}</b></span>
            <span>สุทธิ <b>${monopolyMoney(netWorth)}</b></span>
            <span>ช่อง <b>${player.position}</b></span>
            <span>ทรัพย์ <b>${ownedCount}</b></span>
            <span>ชุดสี <b>${sets}</b></span>
            <span>บ้าน <b>${houses}</b></span>
            <span>โรงแรม <b>${hotels}</b></span>
            <span>แลนด์มาร์ก <b>${landmarks}</b></span>
            <span>จำนอง <b>${mortgaged}</b></span>
          </div>
          <div class="mono-player-inline mono-player-inline-state">${extra ? escapeHtml(extra.replace(/^ \| /, "")) : "พร้อมเล่น"}</div>
        `;
      } else {
        stats = `ตำแหน่ง: ${player.position} | โล่: ${player.shields}${itemStatus.length ? ` | ${escapeHtml(itemStatus.join(" / "))}` : ""}`;
      }
      const safeAvatarId = normalizeAvatarId(player.avatarId, 1);
      const accent = monopoly
        ? resolvePlayerAccent(displayPlayers, player.playerId)
        : null;
      const badge = monopoly && accent?.slot
        ? `
            <span
              class="mono-owner-badge"
              style="--owner-accent:${accent.base};--owner-accent-bright:${accent.bright};--owner-accent-edge:${accent.edge};--owner-accent-deep:${accent.deep};--owner-accent-soft:${accent.soft};--owner-accent-glow:${accent.glow};"
              title="สัญลักษณ์เจ้าของทรัพย์สิน"
            >${escapeHtml(String(accent.emblem || "✦"))}</span>
          `
        : "";

      return `
        <li class="${classes.join(" ")}">
          <div class="player-name-row">
            ${badge}
            <img class="inline-avatar" src="${avatarSrc(safeAvatarId)}" alt="Avatar ${safeAvatarId}">
            <strong>${escapeHtml(player.displayName)}</strong>
          </div>
          <div class="player-stats-block">${stats}</div>
          ${player.connected ? "" : "<br><em>หลุดการเชื่อมต่อ</em>"}
        </li>
      `;
    });

    const nextHtml = rows.join("");
    if (nextHtml === viewCache.playerListHtml) {
      return;
    }

    el.playerList.innerHTML = nextHtml;
    viewCache.playerListHtml = nextHtml;
  }

  function renderBoard() {
    const gameKey = resolveActiveGameKey();
    const board = state.room?.board;

    clearInactiveBoardRenderers(gameKey);
    const activeRenderer = resolveBoardRenderer(gameKey);

    if (!board) {
      if (activeRenderer?.clear) {
        activeRenderer.clear({ root, state, el, gameKey });
      } else {
        clearBoardFallback();
      }
      return;
    }

    if (activeRenderer?.render) {
      activeRenderer.render({ root, state, el, board, gameKey });
      return;
    }

    clearBoardFallback();
    root.roomUi?.updateFloatingRollButton();
  }

  function renderLastTurn() {
    if (!state.lastTurn) {
      el.turnSummary.textContent = "ยังไม่มีการทอยในรอบนี้";
      return;
    }

    const t = state.lastTurn;
    if (isMonopolyGame()) {
      const logs = Array.isArray(t.actionLogs) && t.actionLogs.length > 0
        ? t.actionLogs
        : [
          t.actionSummary ||
            `${playerName(t.playerId)} เดินจาก ${t.startPosition} ไป ${t.endPosition}`,
        ];
      if (t.isGameFinished) {
        logs.push(`ผู้ชนะ: ${playerName(t.winnerPlayerId)}`);
      }
      el.turnSummary.innerHTML = logs
        .map((line) => `<div>${escapeHtml(String(line ?? ""))}</div>`)
        .join("");
      return;
    }

    const lines = [
      `${playerName(t.playerId)} ทอยได้ ${t.diceValue} เดินจาก ${t.startPosition} -> ${t.endPosition}`,
    ];

    if (t.autoRollReason === "Disconnected") {
      lines.push("ผู้เล่นออฟไลน์ ระบบทอยให้อัตโนมัติ");
    }
    if (t.autoRollReason === "TimerExpired") {
      lines.push("หมดเวลาเทิร์น ระบบทอยให้อัตโนมัติ");
    }
    if (t.comebackBoostApplied) {
      const boostAmount =
        Number.parseInt(String(t.comebackBoostAmount ?? 0), 10) || 0;
      const baseDice =
        Number.parseInt(String(t.baseDiceValue ?? t.diceValue), 10) ||
        t.diceValue;
      if (boostAmount > 0) {
        lines.push(`ได้โบนัสเร่งแซง +${boostAmount} (${baseDice} -> ${t.diceValue})`);
      } else {
        lines.push(
          `ได้โบนัสเร่งแซงแต่แต้มชนเพดาน (${baseDice} -> ${t.diceValue})`,
        );
      }
    }
    if (t.usedLuckyReroll) lines.push("ใช้สิทธิ์ทอยซ้ำ");
    if (t.overflowAmount > 0) lines.push(`แต้มเกินเส้นชัย: ${t.overflowAmount}`);
    if (t.forkCell) {
      lines.push(
        `เจอทางแยกที่ช่อง ${t.forkCell.cell}: เลือกเส้น ${t.forkChoice === 1 ? "เสี่ยงดวง" : "ปลอดภัย"}`,
      );
    }
    if (t.triggeredJump) {
      const jumpKind =
        t.triggeredJump.type === 0
          ? t.triggeredJump.isTemporary
            ? "งูชั่วคราว"
            : "งู"
          : t.triggeredJump.isTemporary
            ? "สะพาน/บันไดชั่วคราว"
            : "บันได";
      lines.push(
        `โดนทางลัด: ${jumpKind} ${t.triggeredJump.from} -> ${t.triggeredJump.to}`,
      );
    }
    if (t.frenzySnakeTriggered && t.frenzySnake) {
      lines.push(`งูคลุ้มคลั่งทำงาน: ${t.frenzySnake.from} -> ${t.frenzySnake.to}`);
    } else if (t.frenzySnakeBlockedByShield && t.frenzySnake) {
      lines.push(`งูคลุ้มคลั่งโผล่ที่ ${t.frenzySnake.from} แต่โล่ช่วยกันไว้ได้`);
    } else if (t.frenzySnake) {
      lines.push(
        `งูคลุ้มคลั่งโผล่: ${t.frenzySnake.from} -> ${t.frenzySnake.to} (รอบนี้ไม่มีใครโดน)`,
      );
    }
    if (t.shieldBlockedSnake) lines.push("เกราะช่วยกันการโดนงูกัดได้สำเร็จ");
    if (t.snakeRepellentBlockedSnake) lines.push("Snake Repellent ช่วยกันงูครั้งนี้ไว้ได้");
    if (t.mercyLadderApplied) lines.push("บันไดเมตตาช่วยดันตำแหน่งขึ้น");
    if (t.ladderHackApplied) {
      lines.push(`Ladder Hack ทำงาน พุ่งเพิ่ม +${t.ladderHackBoostAmount ?? 0}`);
    }
    if (Array.isArray(t.itemEffects) && t.itemEffects.length > 0) {
      for (const effect of t.itemEffects) {
        const meta = boardItemMeta(effect.itemType);
        lines.push(`ไอเท็ม ${meta.name} @${effect.cell}: ${effect.summary ?? "-"}`);
      }
    }
    if (t.shieldsEarned > 0) lines.push(`ได้รับโล่เพิ่ม: +${t.shieldsEarned}`);
    if (t.isGameFinished) lines.push(`ผู้ชนะ: ${playerName(t.winnerPlayerId)}`);

    el.turnSummary.innerHTML = lines
      .map((line) => `<div>${escapeHtml(line)}</div>`)
      .join("");
  }

  function updateActionState() {
    if (state.requireNamePrompt) {
      el.startGameBtn.disabled = true;
      el.leaveRoomBtn.disabled = true;
      el.refreshRoomBtn.disabled = true;
      el.rollDiceFloatingBtn.disabled = true;
      root.actions?.syncRollInteraction?.();
      return;
    }

    const inRoom = Boolean(state.roomCode && state.room);
    const started = state.room?.status === GAME_STATUS.STARTED;
    const myTurn = root.monopolyHelpers?.isMonopolyRoom?.(state.room)
      ? root.monopolyHelpers.canRollNow()
      : started &&
        !state.animating &&
        root.viewState.getDisplayTurnPlayerId() === state.playerId;

    el.startGameBtn.disabled = !inRoom || started || !root.readyUi.canStartGame();
    el.leaveRoomBtn.disabled = !inRoom;
    el.refreshRoomBtn.disabled = !inRoom;
    el.rollDiceFloatingBtn.disabled = !myTurn;
    root.actions?.syncRollInteraction?.();
  }

  function clearInactiveBoardRenderers(activeGameKey) {
    const renderers = root.boardRenderers ?? {};
    for (const [gameKey, renderer] of Object.entries(renderers)) {
      if (gameKey === activeGameKey) {
        continue;
      }
      renderer?.clear?.({ root, state, el, gameKey });
    }
  }

  function resolveBoardRenderer(gameKey) {
    const renderers = root.boardRenderers ?? {};
    return renderers[gameKey] ?? renderers[DEFAULT_GAME_KEY] ?? null;
  }

  function clearBoardFallback() {
    el.board.style.setProperty("--rows", "10");
    el.board.innerHTML = "";
    el.boardLegend.textContent = "";
    hideBoardItemTooltipFallback();
    clearBoardClassesFallback();
    root.boardOverlay?.clear();
    root.boardTokens?.clear();
    root.boardBeacon?.hide?.();
  }

  function clearBoardClassesFallback() {
    el.board.classList.remove("page-transitioning", "page-forward", "page-backward");
  }

  function hideBoardItemTooltipFallback() {
    if (!el.boardItemTooltip) {
      return;
    }

    el.boardItemTooltip.classList.remove("show", "flip");
    el.boardItemTooltip.classList.add("hidden");
  }

  function resolveActiveGameKey() {
    return normalizeGameKey(state.room?.gameKey, DEFAULT_GAME_KEY);
  }

  function playerName(playerId) {
    if (!playerId) {
      return "-";
    }
    const player = state.room?.players.find((x) => x.playerId === playerId);
    return player?.displayName ?? playerId;
  }

  function isMonopolyGame() {
    return resolveActiveGameKey() === MONOPOLY_GAME_KEY;
  }

  function countMonopolyAssets(cells, playerId) {
    if (!Array.isArray(cells) || !playerId) {
      return 0;
    }

    return cells.filter((cell) => {
      const ownerId = String(cell?.ownerPlayerId ?? cell?.OwnerPlayerId ?? "");
      return ownerId === playerId;
    }).length;
  }

  function getMonopolyCells(board) {
    const monopolyRenderer = root.boardRenderers?.[MONOPOLY_GAME_KEY];
    if (typeof monopolyRenderer?.getCells === "function") {
      return monopolyRenderer.getCells(board);
    }

    if (!board) {
      return [];
    }
    if (Array.isArray(board.monopolyCells)) {
      return board.monopolyCells;
    }
    if (Array.isArray(board.MonopolyCells)) {
      return board.MonopolyCells;
    }
    if (Array.isArray(board.monopolycells)) {
      return board.monopolycells;
    }
    return [];
  }

  function monopolyMoney(value) {
    const formatter = root.monopolyHelpers?.money;
    if (typeof formatter === "function") {
      return formatter(value);
    }

    const amount = Number.parseInt(String(value ?? 0), 10) || 0;
    const abs = Math.abs(amount).toLocaleString("th-TH");
    return amount < 0 ? `-฿${abs}` : `฿${abs}`;
  }

  root.renderGame = {
    renderRoomHeader,
    renderPlayers,
    renderBoard,
    renderLastTurn,
    updateActionState,
  };
})();
