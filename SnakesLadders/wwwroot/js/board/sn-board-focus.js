(() => {
  const root = window.SNL;
  const { state, GAME_STATUS } = root;

  const PAGE_TRANSITION_MS = 550;

  function init() {
    state.focusMode = "turn";
    state.pendingBeaconTargetPlayerId = "";
    root.storage?.saveFocusMode?.("turn");
  }

  function getPrimaryFocusPlayerId() {
    if (!state.room) {
      return "";
    }

    const turnPlayerId = root.viewState?.getDisplayTurnPlayerId?.() ?? "";
    if (state.room.status === GAME_STATUS.STARTED) {
      return turnPlayerId || state.playerId || "";
    }

    return state.playerId || turnPlayerId;
  }

  function getPlayerPosition(playerId) {
    if (!playerId) {
      return null;
    }

    return root.viewState?.getPlayerPosition?.(playerId) ?? null;
  }

  async function centerOnPrimaryFocus(options = {}) {
    const boardSize = state.room?.board?.size ?? 0;
    if (!boardSize) {
      return false;
    }

    const targetId = getPrimaryFocusPlayerId();
    const position = getPlayerPosition(targetId);
    if (!position) {
      state.visiblePageStart = root.boardPage.normalizeVisiblePageStart(boardSize, state.visiblePageStart);
      return false;
    }

    const targetPageStart = root.boardPage.getPageStartForCell(position);
    return root.boardPage.setVisiblePageStart(targetPageStart, {
      animate: Boolean(options.animate),
      durationMs: options.durationMs ?? PAGE_TRANSITION_MS,
      boardSize,
      renderNow: options.renderNow
    });
  }

  function refreshPendingBeaconTarget() {
    state.pendingBeaconTargetPlayerId = "";
  }

  async function jumpToPlayer(playerId, options = {}) {
    const position = getPlayerPosition(playerId);
    if (!position) {
      return false;
    }

    const changed = await root.boardPage.jumpToCell(position, {
      animate: options.animate !== false,
      durationMs: options.durationMs,
      renderNow: options.renderNow
    });

    if (changed && options.renderAfter !== false) {
      root.feedback?.renderAll?.();
    }

    return changed;
  }

  async function jumpToPendingTarget() {
    return false;
  }

  function onRoomBound(forceCenter = false) {
    const boardSize = state.room?.board?.size ?? 0;
    if (!boardSize) {
      state.visiblePageStart = 1;
      state.pendingBeaconTargetPlayerId = "";
      return;
    }

    const started = state.room?.status === GAME_STATUS.STARTED;
    const shouldAnimate = started && !forceCenter;

    centerOnPrimaryFocus({
      animate: shouldAnimate,
      durationMs: PAGE_TRANSITION_MS,
      renderNow: !shouldAnimate
    }).catch(() => {});
  }

  function clearState() {
    state.visiblePageStart = 1;
    state.pendingBeaconTargetPlayerId = "";
    state.pageTransitioning = false;
    state.pageTransitionDirection = 0;
  }

  root.boardFocus = {
    init,
    getPrimaryFocusPlayerId,
    refreshPendingBeaconTarget,
    centerOnPrimaryFocus,
    jumpToPlayer,
    jumpToPendingTarget,
    onRoomBound,
    clearState
  };
})();
