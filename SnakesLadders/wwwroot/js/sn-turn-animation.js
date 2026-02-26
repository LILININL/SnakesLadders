(() => {
  const root = window.SNL;
  const { state } = root;

  const STAY_WAIT_MS = 320;
  const SEGMENT_END_WAIT_MS = 360;
  const CROSS_PAGE_PAUSE_MS = 130;
  const JUMP_HINT_MS = 600;
  const PAGE_TRANSITION_MS = 550;

  let chain = Promise.resolve();

  function queue(payload) {
    chain = chain.then(() => play(payload)).catch(() => {});
    return chain;
  }

  async function play(payload) {
    if (!payload?.turn || !payload?.room) {
      return;
    }

    const turn = payload.turn;
    const room = payload.room;
    const boardSize = room.board?.size ?? 0;
    if (!boardSize) {
      state.lastTurn = turn;
      state.room = room;
      root.feedback.renderAll();
      return;
    }

    const followPlayer = shouldFollowPlayer(turn.playerId, turn.startPosition, room);

    beginAnimation(turn.playerId, turn.startPosition, room, turn.playerId);
    try {
      await root.boardFx?.showDice?.(turn.playerId, turn.diceValue);

      if (followPlayer) {
        await ensureVisiblePage(turn.startPosition, false, room.board.size);
      }

      const segments = buildSegments(turn, room);
      for (const segment of segments) {
        await runSegment(segment, room.board.size, followPlayer);
      }

      if (turn.isGameFinished) {
        await root.boardFx?.showWinner?.(turn, room);
      }
    } finally {
      root.pieceTransit?.reset?.();
      endAnimation(room, turn);
    }
  }

  function shouldFollowPlayer(playerId, startPosition, room) {
    return true;
  }

  function beginAnimation(playerId, startPosition, room, turnPlayerId) {
    state.animating = true;
    state.animPlayerId = playerId;
    state.animPlayerPosition = startPosition;
    state.animTurnPlayerId = turnPlayerId;
    state.deferredRoom = room;
    root.feedback.renderAll();
  }

  function endAnimation(room, turn) {
    state.animating = false;
    state.animPlayerId = "";
    state.animPlayerPosition = 1;
    state.animTurnPlayerId = "";
    state.animTransitActive = false;
    state.animTransitPlayerId = "";
    state.room = state.deferredRoom ?? room;
    state.deferredRoom = null;
    state.lastTurn = turn;
    root.boardFocus?.onRoomBound?.(false);
    root.realtime?.flushPendingTurnTrigger?.(state.room);
    root.feedback.renderAll();
  }

  async function runSegment(segment, boardSize, followPlayer) {
    if (!followPlayer) {
      state.animPlayerPosition = segment.to;
      root.feedback.renderAll();
      await wait(90);
      return;
    }

    if (segment.mode === "path") {
      const crossPage = root.boardPage.getPageStartForCell(segment.from) !== root.boardPage.getPageStartForCell(segment.to);
      if (crossPage) {
        await runCrossPageJump(segment, boardSize);
        return;
      }

      await ensureVisiblePage(segment.from, false, boardSize);
      const completed = await root.pieceTransit?.run?.(segment);
      if (completed) {
        await wait(SEGMENT_END_WAIT_MS);
        return;
      }
    }

    await runStepSegment(segment.from, segment.to, boardSize);
  }

  async function runCrossPageJump(segment, boardSize) {
    const icon = segment.jumpType === "snake" ? "🐍" : "🪜";
    await root.boardFx?.showJumpHint?.(`${icon} ไปช่อง ${segment.to}`, JUMP_HINT_MS);
    await ensureVisiblePage(segment.to, true, boardSize);
    state.animPlayerPosition = segment.to;
    root.feedback.renderAll();
    await wait(SEGMENT_END_WAIT_MS);
  }

  async function runStepSegment(from, to, boardSize) {
    if (from === to) {
      await wait(STAY_WAIT_MS);
      return;
    }

    const direction = to > from ? 1 : -1;
    const distance = Math.abs(to - from);
    const delay = stepDelay(distance);

    let current = from;
    while (current !== to) {
      const next = current + direction;
      const nextPageStart = root.boardPage.getPageStartForCell(next);
      if (nextPageStart !== state.visiblePageStart) {
        await root.boardPage.setVisiblePageStart(nextPageStart, {
          animate: true,
          durationMs: PAGE_TRANSITION_MS,
          boardSize
        });
        await wait(CROSS_PAGE_PAUSE_MS);
      }

      current = next;
      state.animPlayerPosition = current;
      root.feedback.renderAll();
      await wait(delay);
    }

    await wait(SEGMENT_END_WAIT_MS);
  }

  function buildSegments(turn, room) {
    const size = room.board.size;
    const segments = [];
    let cursor = turn.startPosition;

    const primary = resolvePrimaryLanding(turn, room.boardOptions.overflowMode, size);
    if (primary !== cursor) {
      segments.push({ mode: "step", playerId: turn.playerId, from: cursor, to: primary });
      cursor = primary;
    }

    if (turn.forkCell) {
      const forkTarget = turn.forkChoice === 1 ? turn.forkCell.riskyTo : turn.forkCell.safeTo;
      if (forkTarget !== cursor) {
        segments.push({ mode: "step", playerId: turn.playerId, from: cursor, to: forkTarget });
        cursor = forkTarget;
      }
    }

    if (turn.triggeredJump && !turn.shieldBlockedSnake && turn.triggeredJump.to !== cursor) {
      const jumpType = turn.triggeredJump.type === 0 ? "snake" : "ladder";
      segments.push({
        mode: "path",
        playerId: turn.playerId,
        jumpType,
        seed: turn.triggeredJump.from + turn.triggeredJump.to,
        from: cursor,
        to: turn.triggeredJump.to
      });
      cursor = turn.triggeredJump.to;
    }

    if (turn.frenzySnake && turn.frenzySnakeTriggered && turn.frenzySnake.to !== cursor) {
      segments.push({
        mode: "path",
        playerId: turn.playerId,
        jumpType: "snake",
        seed: turn.frenzySnake.from + turn.frenzySnake.to + 5,
        from: cursor,
        to: turn.frenzySnake.to
      });
      cursor = turn.frenzySnake.to;
    }

    if (cursor !== turn.endPosition) {
      segments.push({ mode: "step", playerId: turn.playerId, from: cursor, to: turn.endPosition });
    }

    return segments;
  }

  function resolvePrimaryLanding(turn, overflowMode, size) {
    const rawTarget = turn.startPosition + turn.diceValue;
    if (!turn.overflowAmount || turn.overflowAmount <= 0) {
      return clamp(rawTarget, 1, size);
    }

    if (overflowMode === 1) {
      return clamp(turn.startPosition - (turn.overflowAmount * 2), 1, size);
    }

    return clamp(turn.startPosition, 1, size);
  }

  async function ensureVisiblePage(cell, animate, boardSize) {
    const targetPageStart = root.boardPage.getPageStartForCell(cell);
    if (targetPageStart === state.visiblePageStart) {
      return;
    }

    await root.boardPage.setVisiblePageStart(targetPageStart, {
      animate,
      durationMs: PAGE_TRANSITION_MS,
      boardSize
    });
  }

  function stepDelay(distance) {
    if (distance <= 6) return 560;
    if (distance <= 20) return 440;
    if (distance <= 60) return 320;
    return 250;
  }

  function wait(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  function reset() {
    root.pieceTransit?.reset?.();
    root.boardFx?.reset?.();
  }

  root.turnAnimation = {
    queue,
    reset
  };
})();
