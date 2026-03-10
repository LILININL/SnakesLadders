(() => {
  const root = window.SNL;
  const { state } = root;

  const STAY_WAIT_MS = 320;
  const SEGMENT_END_WAIT_MS = 360;
  const CROSS_PAGE_PAUSE_MS = 130;
  const JUMP_HINT_MS = 600;
  const PAGE_TRANSITION_MS = 550;
  const MONOPOLY_RELOCATION_MIN_DISTANCE = 8;
  const MONOPOLY_QUEUE_SOFT_FAST_FORWARD = 3;
  const MONOPOLY_QUEUE_HARD_FAST_FORWARD = 6;
  const FAST_FORWARD_SETTLE_MS = 80;
  const MONOPOLY_GAME_KEY = String(
    root.GAME_KEYS?.MONOPOLY ?? "monopoly",
  ).trim().toLowerCase();

  let chain = Promise.resolve();
  let queuedTurns = 0;
  let lastFinishedTurnKey = "";
  const queuedTurnKeys = new Set();

  function queue(payload) {
    const turnKey = buildTurnKey(payload);
    if (
      turnKey &&
      (queuedTurnKeys.has(turnKey) || turnKey === lastFinishedTurnKey)
    ) {
      return chain;
    }

    if (turnKey) {
      queuedTurnKeys.add(turnKey);
    }

    queuedTurns += 1;
    chain = chain
      .then(() => play(payload))
      .catch(() => {})
      .finally(() => {
        if (turnKey) {
          queuedTurnKeys.delete(turnKey);
        }
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

    const turnKey = buildTurnKey(payload);
    const turn = payload.turn;
    const room = payload.room;
    const playback = resolvePlaybackProfile(payload);
    const boardSize = room.board?.size ?? 0;
    if (!boardSize) {
      state.lastTurn = turn;
      root.viewState.acceptRoomSnapshot(room);
      root.feedback.renderAll();
      if (turnKey) {
        lastFinishedTurnKey = turnKey;
      }
      return;
    }

    const followPlayer = shouldFollowPlayer(
      turn.playerId,
      turn.startPosition,
      room,
    );

    beginAnimation(
      turn.playerId,
      turn.startPosition,
      room,
      turn.playerId,
      turn,
    );
    try {
      if (hasDiceRoll(turn) && !playback.skipDiceFx) {
        const diceOne = Number.parseInt(String(turn.diceOne ?? 0), 10) || 0;
        const diceTwo = Number.parseInt(String(turn.diceTwo ?? 0), 10) || 0;
        const diceTotal =
          Number.parseInt(String(turn.diceValue ?? 0), 10) ||
          (diceOne > 0 && diceTwo > 0 ? diceOne + diceTwo : diceOne || diceTwo || 1);
        await root.boardFx?.showDice?.(turn.playerId, diceOne, diceTwo, diceTotal, {
          actionType: turn.actionType,
          isDouble: Boolean(turn.isDouble),
          isJailRoll:
            Number.parseInt(
              String(turn.actionType ?? root.GAME_ACTION_TYPES.ROLL_DICE),
              10,
            ) === root.GAME_ACTION_TYPES.TRY_JAIL_ROLL,
          durationScale: playback.durationScale,
        });
      }

      if (followPlayer) {
        await ensureVisiblePage(turn.startPosition, false, room.board.size);
      }

      const pendingItemEffects = normalizeItemEffects(
        turn.itemEffects,
        room.board.size,
      );
      const segments = buildSegments(turn, room, pendingItemEffects);
      for (const segment of segments) {
        await runSegment(
          segment,
          room.board.size,
          followPlayer,
          pendingItemEffects,
          playback,
        );
      }

      await consumeLeftoverItemEffects(
        room.board.size,
        followPlayer,
        pendingItemEffects,
        playback,
      );
      state.animPlayerPosition = clamp(
        Number.parseInt(
          String(turn.endPosition ?? state.animPlayerPosition),
          10,
        ) || state.animPlayerPosition,
        1,
        room.board.size,
      );
      root.feedback.renderAll();
      await playMoneyEvents(turn, playback);

      if (turn.isGameFinished) {
        await root.boardFx?.showWinner?.(turn, room);
      }
    } finally {
      if (turnKey) {
        lastFinishedTurnKey = turnKey;
      }
      root.pieceTransit?.reset?.();
      endAnimation(room, turn);
    }
  }

  function shouldFollowPlayer(playerId, startPosition, room) {
    return true;
  }

  function beginAnimation(
    playerId,
    startPosition,
    room,
    turnPlayerId,
    turn = null,
  ) {
    state.animating = true;
    state.animPlayerId = playerId;
    state.animPlayerPosition = startPosition;
    state.animTurnPlayerId = turnPlayerId;
    state.animFrenzySnake = turn?.frenzySnake ?? null;
    root.viewState.stageDeferredRoom(room, { force: true });
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
    root.viewState.acceptRoomSnapshot(room);
    root.viewState.clearDeferredRoom();
    state.lastTurn = turn;
    root.boardFocus?.onRoomBound?.(false);
    root.feedback.renderAll();
  }

  async function runSegment(
    segment,
    boardSize,
    followPlayer,
    pendingItemEffects,
    playback,
  ) {
    if (segment.mode === "event") {
      if (!playback.skipEventOverlay) {
        await root.boardFx?.showEventOverlay?.({
          ...segment.notice,
          holdMs: scaleMs(segment.notice?.holdMs ?? 3400, playback.waitScale, 240),
        });
      }
      return;
    }

    const animatedFrom = clamp(
      Number.parseInt(String(state.animPlayerPosition ?? segment.from), 10) ||
        segment.from,
      1,
      boardSize,
    );
    const effectiveSegment =
      animatedFrom === segment.from
        ? segment
        : { ...segment, from: animatedFrom };

    if (animatedFrom !== segment.from) {
      if (segment.to === animatedFrom) {
        return;
      }

      if (segment.mode === "path") {
        return;
      }

      const plannedDirection = Math.sign(segment.to - segment.from);
      const liveDirection = Math.sign(segment.to - animatedFrom);
      if (
        plannedDirection !== 0 &&
        liveDirection !== 0 &&
        plannedDirection !== liveDirection
      ) {
        return;
      }
    }

    if (!followPlayer) {
      state.animPlayerPosition = effectiveSegment.to;
      root.feedback.renderAll();
      await wait(scaleMs(90, playback.waitScale, 28));
      await consumeItemEffectsAtCell(
        effectiveSegment.to,
        boardSize,
        followPlayer,
        pendingItemEffects,
        playback,
      );
      return;
    }

    if (playback.instantMovement) {
      await fastForwardSegment(
        effectiveSegment,
        boardSize,
        followPlayer,
        pendingItemEffects,
        playback,
      );
      return;
    }

    if (effectiveSegment.mode === "path") {
      const crossPage =
        root.boardPage.getPageStartForCell(effectiveSegment.from) !==
        root.boardPage.getPageStartForCell(effectiveSegment.to);
      if (crossPage) {
        await runCrossPageJump(
          effectiveSegment,
          boardSize,
          followPlayer,
          pendingItemEffects,
          playback,
        );
        return;
      }

      await ensureVisiblePage(effectiveSegment.from, false, boardSize);
      const completed = await root.pieceTransit?.run?.(effectiveSegment);
      if (completed) {
        const movedTo = await consumeItemEffectsAtCell(
          effectiveSegment.to,
          boardSize,
          followPlayer,
          pendingItemEffects,
          playback,
        );
        if (movedTo !== effectiveSegment.to) {
          state.animPlayerPosition = movedTo;
          root.feedback.renderAll();
        }
        await wait(scaleMs(SEGMENT_END_WAIT_MS, playback.waitScale, 36));
        return;
      }
    }

    if (effectiveSegment.mode === "relocate") {
      await runRelocationSegment(
        effectiveSegment,
        boardSize,
        followPlayer,
        pendingItemEffects,
        playback,
      );
      return;
    }

    const finalPos = await runStepSegment(
      effectiveSegment.from,
      effectiveSegment.to,
      boardSize,
      followPlayer,
      pendingItemEffects,
      playback,
      Boolean(effectiveSegment.wrapForward),
    );
    if (Number.isFinite(finalPos)) {
      state.animPlayerPosition = clamp(finalPos, 1, boardSize);
      root.feedback.renderAll();
    }
  }

  async function runCrossPageJump(
    segment,
    boardSize,
    followPlayer,
    pendingItemEffects,
    playback,
  ) {
    const icon = segment.jumpType === "snake" ? "🐍" : "🪜";
    await root.boardFx?.showJumpHint?.(
      `${icon} ไปช่อง ${segment.to}`,
      scaleMs(JUMP_HINT_MS, playback.waitScale, 120),
    );
    await ensureVisiblePage(segment.to, true, boardSize);
    state.animPlayerPosition = segment.to;
    root.feedback.renderAll();
    const movedTo = await consumeItemEffectsAtCell(
      segment.to,
      boardSize,
      followPlayer,
      pendingItemEffects,
      playback,
    );
    if (movedTo !== segment.to) {
      state.animPlayerPosition = movedTo;
      root.feedback.renderAll();
    }
    await wait(scaleMs(SEGMENT_END_WAIT_MS, playback.waitScale, 36));
  }

  async function runRelocationSegment(
    segment,
    boardSize,
    followPlayer,
    pendingItemEffects,
    playback,
  ) {
    if (!followPlayer) {
      state.animPlayerPosition = segment.to;
      root.feedback.renderAll();
      await consumeItemEffectsAtCell(
        segment.to,
        boardSize,
        followPlayer,
        pendingItemEffects,
        playback,
      );
      await wait(scaleMs(STAY_WAIT_MS, playback.waitScale, 36));
      return;
    }

    const fromPage = root.boardPage.getPageStartForCell(segment.from);
    const toPage = root.boardPage.getPageStartForCell(segment.to);
    if (fromPage !== toPage) {
      await ensureVisiblePage(segment.to, true, boardSize);
    } else {
      await ensureVisiblePage(segment.from, false, boardSize);
    }

    const completed = await root.pieceTransit?.run?.(segment);
    if (!completed) {
      state.animPlayerPosition = segment.to;
      root.feedback.renderAll();
    }

    const movedTo = await consumeItemEffectsAtCell(
      segment.to,
      boardSize,
      followPlayer,
      pendingItemEffects,
      playback,
    );
    if (movedTo !== segment.to) {
      state.animPlayerPosition = movedTo;
      root.feedback.renderAll();
    }
    await wait(scaleMs(SEGMENT_END_WAIT_MS, playback.waitScale, 36));
  }

  async function runStepSegment(
    from,
    to,
    boardSize,
    followPlayer,
    pendingItemEffects,
    playback,
    wrapForward = false,
  ) {
    if (from === to) {
      await consumeItemEffectsAtCell(
        to,
        boardSize,
        followPlayer,
        pendingItemEffects,
        playback,
      );
      await wait(scaleMs(STAY_WAIT_MS, playback.waitScale, 36));
      return to;
    }

    let current = from;
    let guard = 0;
    while (current !== to) {
      const delay = stepDelay(Math.abs(to - current), playback);
      let next;
      if (wrapForward) {
        next = current >= boardSize ? 1 : current + 1;
      } else {
        const direction = to > current ? 1 : -1;
        next = current + direction;
      }
      const nextPageStart = root.boardPage.getPageStartForCell(next);
      if (nextPageStart !== state.visiblePageStart) {
        await root.boardPage.setVisiblePageStart(nextPageStart, {
          animate: true,
          durationMs: scaleMs(PAGE_TRANSITION_MS, playback.waitScale, 120),
          boardSize,
        });
        await wait(scaleMs(CROSS_PAGE_PAUSE_MS, playback.waitScale, 28));
      }

      current = next;
      state.animPlayerPosition = current;
      root.feedback.renderAll();
      await wait(delay);

      const movedByEffect = await consumeItemEffectsAtCell(
        current,
        boardSize,
        followPlayer,
        pendingItemEffects,
        playback,
      );
      if (movedByEffect !== current) {
        current = movedByEffect;
        state.animPlayerPosition = current;
        root.feedback.renderAll();
        // Item/trap effect overrides this segment target.
        return current;
      }

      guard++;
      if (guard > boardSize + 2) {
        break;
      }
    }

    await wait(scaleMs(SEGMENT_END_WAIT_MS, playback.waitScale, 36));
    return current;
  }

  function normalizeItemEffects(itemEffects, boardSize) {
    if (!Array.isArray(itemEffects) || itemEffects.length === 0) {
      return [];
    }

    const normalized = itemEffects.map((effect) => {
      const cell = clamp(
        Number.parseInt(String(effect?.cell ?? 0), 10) || 0,
        1,
        boardSize,
      );
      const fromPosition = clamp(
        Number.parseInt(String(effect?.fromPosition ?? cell), 10) || cell,
        1,
        boardSize,
      );
      const toPosition = clamp(
        Number.parseInt(String(effect?.toPosition ?? fromPosition), 10) ||
          fromPosition,
        1,
        boardSize,
      );
      return {
        itemType: effect?.itemType,
        cell,
        fromPosition,
        toPosition,
        summary: effect?.summary ?? "",
        isTrapTrigger: Boolean(effect?.isTrapTrigger),
      };
    });

    const deduped = [];
    const seen = new Set();
    for (const effect of normalized) {
      const key = [
        effect.itemType,
        effect.cell,
        effect.fromPosition,
        effect.toPosition,
        effect.isTrapTrigger ? 1 : 0,
      ].join("|");
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      deduped.push(effect);
    }

    return deduped;
  }

  async function consumeItemEffectsAtCell(
    cell,
    boardSize,
    followPlayer,
    pendingItemEffects,
    playback,
  ) {
    if (!Array.isArray(pendingItemEffects) || pendingItemEffects.length === 0) {
      return cell;
    }

    let current = cell;
    while (
      pendingItemEffects.length > 0 &&
      pendingItemEffects[0].cell === cell
    ) {
      const effect = pendingItemEffects.shift();
      if (!effect) {
        continue;
      }

      if (effect.fromPosition !== current) {
        continue;
      }

      await root.boardFx?.showItemPickup?.(effect);
      await root.boardFx?.showItemActivation?.(effect);

      if (effect.toPosition !== current) {
        current = await runStepSegment(
          current,
          effect.toPosition,
          boardSize,
          followPlayer,
          pendingItemEffects,
          playback,
        );
      }

      await root.boardFx?.showItemResult?.(effect);
    }

    return current;
  }

  async function consumeLeftoverItemEffects(
    boardSize,
    followPlayer,
    pendingItemEffects,
    playback,
  ) {
    if (!Array.isArray(pendingItemEffects) || pendingItemEffects.length === 0) {
      return;
    }

    while (pendingItemEffects.length > 0) {
      const effect = pendingItemEffects.shift();
      if (!effect) {
        continue;
      }
      let current = clamp(
        Number.parseInt(
          String(state.animPlayerPosition ?? effect.fromPosition),
          10,
        ) || effect.fromPosition,
        1,
        boardSize,
      );

      if (effect.fromPosition !== current) {
        continue;
      }

      await root.boardFx?.showItemPickup?.(effect);
      await root.boardFx?.showItemActivation?.(effect);

      if (effect.fromPosition === current && effect.toPosition !== current) {
        current = await runStepSegment(
          current,
          effect.toPosition,
          boardSize,
          followPlayer,
          pendingItemEffects,
          playback,
        );
      }

      await root.boardFx?.showItemResult?.(effect);
      state.animPlayerPosition = current;
      root.feedback.renderAll();
    }
  }

  function buildSegments(turn, room, pendingItemEffects) {
    const size = room.board.size;
    const segments = [];
    let cursor = turn.startPosition;
    const monopolyMode = isMonopolyGameKey(room.gameKey);

    const primary = resolvePrimaryLanding(
      turn,
      room.boardOptions.overflowMode,
      size,
      room.gameKey,
    );
    if (primary !== cursor) {
      segments.push({
        mode: "step",
        playerId: turn.playerId,
        from: cursor,
        to: primary,
        wrapForward:
          monopolyMode &&
          Number.parseInt(String(turn.diceValue ?? 0), 10) > 0,
      });
      cursor = primary;
    }

    const monopolyEventNotice = monopolyMode
      ? turn?.clientFx?.eventNotice ?? null
      : null;
    if (monopolyEventNotice) {
      segments.push({
        mode: "event",
        notice: monopolyEventNotice,
      });
    }

    if (turn.forkCell) {
      const forkTarget =
        turn.forkChoice === 1 ? turn.forkCell.riskyTo : turn.forkCell.safeTo;
      if (forkTarget !== cursor) {
        segments.push({
          mode: "step",
          playerId: turn.playerId,
          from: cursor,
          to: forkTarget,
        });
        cursor = forkTarget;
      }
    }

    const jumpBlocked =
      turn.triggeredJump?.type === 0 &&
      (turn.shieldBlockedSnake || turn.snakeRepellentBlockedSnake);
    if (turn.triggeredJump && !jumpBlocked) {
      const jumpFrom = clamp(turn.triggeredJump.from, 1, size);
      const jumpTo = clamp(turn.triggeredJump.to, 1, size);
      if (jumpFrom !== cursor) {
        segments.push({
          mode: "step",
          playerId: turn.playerId,
          from: cursor,
          to: jumpFrom,
        });
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
          to: jumpTo,
        });
        cursor = jumpTo;
      }
    }

    if (turn.frenzySnake && turn.frenzySnakeTriggered) {
      const frenzyFrom = clamp(turn.frenzySnake.from, 1, size);
      const frenzyTo = clamp(turn.frenzySnake.to, 1, size);
      if (frenzyFrom !== cursor) {
        segments.push({
          mode: "step",
          playerId: turn.playerId,
          from: cursor,
          to: frenzyFrom,
        });
        cursor = frenzyFrom;
      }

      if (frenzyTo !== cursor) {
        segments.push({
          mode: "path",
          playerId: turn.playerId,
          jumpType: "snake",
          seed: turn.frenzySnake.from + turn.frenzySnake.to + 5,
          from: cursor,
          to: frenzyTo,
        });
        cursor = frenzyTo;
      }
    }

    if (cursor !== turn.endPosition) {
      const finalMode =
        monopolyMode &&
        shouldUseMonopolyRelocation(cursor, turn.endPosition, size)
          ? "relocate"
          : "step";
      segments.push({
        mode: finalMode,
        playerId: turn.playerId,
        from: cursor,
        to: turn.endPosition,
      });
    }

    return segments;
  }

  function shouldUseMonopolyRelocation(from, to, boardSize) {
    const safeFrom = clamp(
      Number.parseInt(String(from ?? 0), 10) || 0,
      1,
      boardSize,
    );
    const safeTo = clamp(
      Number.parseInt(String(to ?? 0), 10) || 0,
      1,
      boardSize,
    );
    const absoluteDistance = Math.abs(safeTo - safeFrom);
    const isSpecialDestination = safeTo === 1 || safeTo === 11;
    const isBackwardTravel = safeTo < safeFrom;
    return (
      isSpecialDestination ||
      absoluteDistance >= MONOPOLY_RELOCATION_MIN_DISTANCE ||
      (isBackwardTravel && absoluteDistance >= 6)
    );
  }

  async function playMoneyEvents(turn, playback) {
    const moneyEvents = Array.isArray(turn?.clientFx?.moneyEvents)
      ? turn.clientFx.moneyEvents
      : [];
    if (playback.skipMoneyOverlay) {
      return;
    }
    for (const event of moneyEvents) {
      await root.boardFx?.showMoneyFlow?.({
        ...event,
        countDurationMs: scaleMs(1200, playback.waitScale, 180),
        holdMs: scaleMs(3000, playback.waitScale, 420),
      });
    }
  }

  function resolvePrimaryLanding(turn, overflowMode, size, gameKey) {
    if (isMonopolyGameKey(gameKey)) {
      const start = clamp(turn.startPosition, 1, size);
      const actionType =
        Number.parseInt(
          String(turn?.actionType ?? root.GAME_ACTION_TYPES.ROLL_DICE),
          10,
        ) || root.GAME_ACTION_TYPES.ROLL_DICE;
      const end = clamp(turn.endPosition, 1, size);
      if (
        actionType === root.GAME_ACTION_TYPES.TRY_JAIL_ROLL &&
        !Boolean(turn?.isDouble) &&
        end === start
      ) {
        return start;
      }
      const dice = Math.max(
        0,
        Number.parseInt(String(turn.diceValue ?? 0), 10) || 0,
      );
      return ((start - 1 + dice) % size) + 1;
    }

    const rawTarget = turn.startPosition + turn.diceValue;
    if (!turn.overflowAmount || turn.overflowAmount <= 0) {
      return clamp(rawTarget, 1, size);
    }

    if (overflowMode === 1) {
      return clamp(turn.startPosition - turn.overflowAmount * 2, 1, size);
    }

    return clamp(turn.startPosition, 1, size);
  }

  function isMonopolyGameKey(gameKey) {
    return String(gameKey ?? "").trim().toLowerCase() === MONOPOLY_GAME_KEY;
  }
  async function ensureVisiblePage(cell, animate, boardSize) {
    const targetPageStart = root.boardPage.getPageStartForCell(cell);
    if (targetPageStart === state.visiblePageStart) {
      return;
    }

    await root.boardPage.setVisiblePageStart(targetPageStart, {
      animate,
      durationMs: PAGE_TRANSITION_MS,
      boardSize,
    });
  }

  function stepDelay(distance, playback = null) {
    const base = distance <= 6 ? 560 : distance <= 20 ? 440 : distance <= 60 ? 320 : 250;
    return scaleMs(base, playback?.stepScale ?? 1, 42);
  }

  async function fastForwardSegment(
    segment,
    boardSize,
    followPlayer,
    pendingItemEffects,
    playback,
  ) {
    if (followPlayer) {
      await ensureVisiblePage(segment.to, false, boardSize);
    }

    state.animPlayerPosition = segment.to;
    root.feedback.renderAll();

    const movedTo = await consumeItemEffectsAtCell(
      segment.to,
      boardSize,
      followPlayer,
      pendingItemEffects,
      playback,
    );
    if (movedTo !== segment.to) {
      state.animPlayerPosition = movedTo;
      root.feedback.renderAll();
    }

    await wait(scaleMs(FAST_FORWARD_SETTLE_MS, playback.waitScale, 18));
  }

  function resolvePlaybackProfile(payload) {
    const room = payload?.room;
    const turn = payload?.turn;
    if (!isMonopolyGameKey(room?.gameKey)) {
      return {
        durationScale: 1,
        stepScale: 1,
        waitScale: 1,
        skipDiceFx: false,
        skipEventOverlay: false,
        skipMoneyOverlay: false,
        instantMovement: false,
      };
    }

    const queueDepth = Math.max(0, queuedTurns - 1);
    const actor = Array.isArray(room?.players)
      ? room.players.find((player) => player.playerId === turn?.playerId)
      : null;
    const isLocalPlayer = actor?.playerId === state.playerId;
    const isAutomated = Boolean(
      actor?.isBot || actor?.fullAutoEnabled || turn?.autoRollReason,
    );

    if (
      queueDepth >= MONOPOLY_QUEUE_HARD_FAST_FORWARD &&
      (isAutomated || !isLocalPlayer)
    ) {
      return {
        durationScale: 0.28,
        stepScale: 0.2,
        waitScale: 0.16,
        skipDiceFx: true,
        skipEventOverlay: true,
        skipMoneyOverlay: true,
        instantMovement: true,
      };
    }

    if (
      queueDepth >= MONOPOLY_QUEUE_SOFT_FAST_FORWARD &&
      (isAutomated || !isLocalPlayer)
    ) {
      return {
        durationScale: 0.5,
        stepScale: 0.34,
        waitScale: 0.38,
        skipDiceFx: false,
        skipEventOverlay: false,
        skipMoneyOverlay: false,
        instantMovement: true,
      };
    }

    return {
      durationScale: 1,
      stepScale: 1,
      waitScale: 1,
      skipDiceFx: false,
      skipEventOverlay: false,
      skipMoneyOverlay: false,
      instantMovement: false,
    };
  }

  function scaleMs(ms, scale = 1, min = 0) {
    return Math.max(min, Math.round((Number(ms) || 0) * (Number(scale) || 1)));
  }

  function wait(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  function buildTurnKey(payload) {
    if (!payload?.turn || !payload?.room) {
      return "";
    }

    const revision =
      root.viewState?.resolveRoomSnapshotRevision?.(payload.room) ??
      Number.parseInt(
        String(
          payload.room?.snapshotRevision ?? payload.room?.SnapshotRevision ?? 0,
        ),
        10,
      ) ||
      0;
    if (revision > 0) {
      const roomCode = String(
        payload.room?.roomCode ?? payload.room?.RoomCode ?? "",
      )
        .trim()
        .toUpperCase();
      return `${roomCode}|${revision}`;
    }

    const turn = payload.turn;
    return [
      payload.room.roomCode ?? "",
      payload.room.turnCounter ?? "",
      turn.playerId ?? "",
      turn.actionType ?? "",
      turn.startPosition ?? "",
      turn.endPosition ?? "",
    ].join("|");
  }

  function hasDiceRoll(turn) {
    if (!turn) {
      return false;
    }

    const d1 = Number.parseInt(String(turn.diceOne ?? 0), 10) || 0;
    const d2 = Number.parseInt(String(turn.diceTwo ?? 0), 10) || 0;
    const total = Number.parseInt(String(turn.diceValue ?? 0), 10) || 0;
    return d1 > 0 || d2 > 0 || total > 0;
  }

  function reset() {
    chain = Promise.resolve();
    queuedTurns = 0;
    lastFinishedTurnKey = "";
    queuedTurnKeys.clear();
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
    isBusy,
  };
})();
