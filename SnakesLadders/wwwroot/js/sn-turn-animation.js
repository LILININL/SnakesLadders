(() => {
  const root = window.SNL;
  const { state } = root;

  const STAY_WAIT_MS = 320;
  const SEGMENT_END_WAIT_MS = 360;
  const CROSS_PAGE_PAUSE_MS = 130;
  const JUMP_HINT_MS = 600;
  const PAGE_TRANSITION_MS = 550;

  let chain = Promise.resolve();
  let queuedTurns = 0;

  function queue(payload) {
    queuedTurns += 1;
    chain = chain
      .then(() => play(payload))
      .catch(() => {})
      .finally(() => {
        queuedTurns = Math.max(0, queuedTurns - 1);
        if (queuedTurns === 0 && !state.animating) {
          root.realtime?.flushPendingTurnTrigger?.(state.room);
          root.feedback.renderAll();
        }
      });
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

    beginAnimation(turn.playerId, turn.startPosition, room, turn.playerId, turn);
    try {
      await root.boardFx?.showDice?.(turn.playerId, turn.diceValue);

      if (followPlayer) {
        await ensureVisiblePage(turn.startPosition, false, room.board.size);
      }

      const pendingItemEffects = normalizeItemEffects(turn.itemEffects, room.board.size);
      const segments = buildSegments(turn, room);
      for (const segment of segments) {
        await runSegment(segment, room.board.size, followPlayer, pendingItemEffects);
      }

      while (pendingItemEffects.length > 0) {
        const leftover = pendingItemEffects.shift();
        if (leftover) {
          await root.boardFx?.showItemPickup?.(leftover);
        }
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

  function beginAnimation(playerId, startPosition, room, turnPlayerId, turn = null) {
    state.animating = true;
    state.animPlayerId = playerId;
    state.animPlayerPosition = startPosition;
    state.animTurnPlayerId = turnPlayerId;
    state.animFrenzySnake = turn?.frenzySnake ?? null;
    state.deferredRoom = room;
    root.feedback.renderAll();
  }

  function endAnimation(room, turn) {
    state.animating = false;
    state.animPlayerId = "";
    state.animPlayerPosition = 1;
    state.animTurnPlayerId = "";
    state.animFrenzySnake = null;
    state.animTransitActive = false;
    state.animTransitPlayerId = "";
    state.room = state.deferredRoom ?? room;
    state.deferredRoom = null;
    state.lastTurn = turn;
    root.boardFocus?.onRoomBound?.(false);
    root.feedback.renderAll();
  }

  async function runSegment(segment, boardSize, followPlayer, pendingItemEffects) {
    const animatedFrom = clamp(
      Number.parseInt(String(state.animPlayerPosition ?? segment.from), 10) || segment.from,
      1,
      boardSize
    );
    const effectiveSegment = animatedFrom === segment.from
      ? segment
      : { ...segment, from: animatedFrom };

    if (!followPlayer) {
      state.animPlayerPosition = effectiveSegment.to;
      root.feedback.renderAll();
      await wait(90);
      await consumeItemEffectsAtCell(effectiveSegment.to, boardSize, followPlayer, pendingItemEffects);
      return;
    }

    if (effectiveSegment.mode === "path") {
      const crossPage = root.boardPage.getPageStartForCell(effectiveSegment.from) !== root.boardPage.getPageStartForCell(effectiveSegment.to);
      if (crossPage) {
        await runCrossPageJump(effectiveSegment, boardSize, followPlayer, pendingItemEffects);
        return;
      }

      await ensureVisiblePage(effectiveSegment.from, false, boardSize);
      const completed = await root.pieceTransit?.run?.(effectiveSegment);
      if (completed) {
        const movedTo = await consumeItemEffectsAtCell(effectiveSegment.to, boardSize, followPlayer, pendingItemEffects);
        if (movedTo !== effectiveSegment.to) {
          state.animPlayerPosition = movedTo;
          root.feedback.renderAll();
        }
        await wait(SEGMENT_END_WAIT_MS);
        return;
      }
    }

    await runStepSegment(effectiveSegment.from, effectiveSegment.to, boardSize, followPlayer, pendingItemEffects);
  }

  async function runCrossPageJump(segment, boardSize, followPlayer, pendingItemEffects) {
    const icon = segment.jumpType === "snake" ? "🐍" : "🪜";
    await root.boardFx?.showJumpHint?.(`${icon} ไปช่อง ${segment.to}`, JUMP_HINT_MS);
    await ensureVisiblePage(segment.to, true, boardSize);
    state.animPlayerPosition = segment.to;
    root.feedback.renderAll();
    const movedTo = await consumeItemEffectsAtCell(segment.to, boardSize, followPlayer, pendingItemEffects);
    if (movedTo !== segment.to) {
      state.animPlayerPosition = movedTo;
      root.feedback.renderAll();
    }
    await wait(SEGMENT_END_WAIT_MS);
  }

  async function runStepSegment(from, to, boardSize, followPlayer, pendingItemEffects) {
    if (from === to) {
      await consumeItemEffectsAtCell(to, boardSize, followPlayer, pendingItemEffects);
      await wait(STAY_WAIT_MS);
      return to;
    }

    let current = from;
    while (current !== to) {
      const direction = to > current ? 1 : -1;
      const delay = stepDelay(Math.abs(to - current));
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

      const movedByEffect = await consumeItemEffectsAtCell(current, boardSize, followPlayer, pendingItemEffects);
      if (movedByEffect !== current) {
        current = movedByEffect;
        state.animPlayerPosition = current;
        root.feedback.renderAll();
      }
    }

    await wait(SEGMENT_END_WAIT_MS);
    return current;
  }

  function normalizeItemEffects(itemEffects, boardSize) {
    if (!Array.isArray(itemEffects) || itemEffects.length === 0) {
      return [];
    }

    return itemEffects.map((effect) => {
      const cell = clamp(Number.parseInt(String(effect?.cell ?? 0), 10) || 0, 1, boardSize);
      const fromPosition = clamp(Number.parseInt(String(effect?.fromPosition ?? cell), 10) || cell, 1, boardSize);
      const toPosition = clamp(Number.parseInt(String(effect?.toPosition ?? fromPosition), 10) || fromPosition, 1, boardSize);
      return {
        itemType: effect?.itemType,
        cell,
        fromPosition,
        toPosition,
        summary: effect?.summary ?? ""
      };
    });
  }

  async function consumeItemEffectsAtCell(cell, boardSize, followPlayer, pendingItemEffects) {
    if (!Array.isArray(pendingItemEffects) || pendingItemEffects.length === 0) {
      return cell;
    }

    let current = cell;
    while (pendingItemEffects.length > 0 && pendingItemEffects[0].cell === cell) {
      const effect = pendingItemEffects.shift();
      if (!effect) {
        continue;
      }

      await root.boardFx?.showItemPickup?.(effect);

      if (effect.fromPosition !== current) {
        current = await runStepSegment(current, effect.fromPosition, boardSize, followPlayer, pendingItemEffects);
      }

      if (effect.toPosition !== current) {
        current = await runStepSegment(current, effect.toPosition, boardSize, followPlayer, pendingItemEffects);
      }
    }

    return current;
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

    if (turn.triggeredJump && !turn.shieldBlockedSnake) {
      const jumpFrom = clamp(turn.triggeredJump.from, 1, size);
      const jumpTo = clamp(turn.triggeredJump.to, 1, size);
      if (jumpFrom !== cursor) {
        segments.push({ mode: "step", playerId: turn.playerId, from: cursor, to: jumpFrom });
        cursor = jumpFrom;
      }

      const jumpType = turn.triggeredJump.type === 0 ? "snake" : "ladder";
      if (jumpTo !== cursor) {
        segments.push({
          mode: "path",
          playerId: turn.playerId,
          jumpType,
          seed: turn.triggeredJump.from + turn.triggeredJump.to,
          from: cursor,
          to: jumpTo
        });
        cursor = jumpTo;
      }
    }

    if (turn.frenzySnake && turn.frenzySnakeTriggered) {
      const frenzyFrom = clamp(turn.frenzySnake.from, 1, size);
      const frenzyTo = clamp(turn.frenzySnake.to, 1, size);
      if (frenzyFrom !== cursor) {
        segments.push({ mode: "step", playerId: turn.playerId, from: cursor, to: frenzyFrom });
        cursor = frenzyFrom;
      }

      if (frenzyTo !== cursor) {
        segments.push({
          mode: "path",
          playerId: turn.playerId,
          jumpType: "snake",
          seed: turn.frenzySnake.from + turn.frenzySnake.to + 5,
          from: cursor,
          to: frenzyTo
        });
        cursor = frenzyTo;
      }
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
    chain = Promise.resolve();
    queuedTurns = 0;
    state.animFrenzySnake = null;
    root.pieceTransit?.reset?.();
    root.boardFx?.reset?.();
  }

  function isBusy() {
    return state.animating || queuedTurns > 0;
  }

  root.turnAnimation = {
    queue,
    reset,
    isBusy
  };
})();
