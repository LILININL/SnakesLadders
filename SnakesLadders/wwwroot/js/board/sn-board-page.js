(() => {
  const root = window.SNL;
  const { state } = root;

  const DEFAULT_PAGE_SIZE = 100;
  const DEFAULT_TRANSITION_MS = 550;

  function getPageSize() {
    const raw = Number.parseInt(
      String(state.pageSize ?? DEFAULT_PAGE_SIZE),
      10,
    );
    return Number.isFinite(raw) && raw > 0 ? raw : DEFAULT_PAGE_SIZE;
  }

  function clampCell(cell, boardSize) {
    const size = Math.max(1, Number.parseInt(String(boardSize ?? 1), 10) || 1);
    const parsed = Number.parseInt(String(cell ?? 1), 10) || 1;
    return Math.max(1, Math.min(size, parsed));
  }

  function getPageStartForCell(cell, pageSize = getPageSize()) {
    const safeCell = Math.max(1, Number.parseInt(String(cell ?? 1), 10) || 1);
    return Math.floor((safeCell - 1) / pageSize) * pageSize + 1;
  }

  function getLastPageStart(boardSize, pageSize = getPageSize()) {
    const safeBoardSize = Math.max(
      1,
      Number.parseInt(String(boardSize ?? 1), 10) || 1,
    );
    return getPageStartForCell(safeBoardSize, pageSize);
  }

  function normalizeVisiblePageStart(
    boardSize,
    start = state.visiblePageStart,
  ) {
    const pageSize = getPageSize();
    const safeBoardSize = Math.max(
      1,
      Number.parseInt(String(boardSize ?? 1), 10) || 1,
    );
    const safeStart = Math.max(1, Number.parseInt(String(start ?? 1), 10) || 1);
    const lastStart = getLastPageStart(safeBoardSize, pageSize);
    return Math.min(getPageStartForCell(safeStart, pageSize), lastStart);
  }

  function getVisibleRange(boardSize, start = state.visiblePageStart) {
    const safeBoardSize = Math.max(
      1,
      Number.parseInt(String(boardSize ?? 1), 10) || 1,
    );
    const pageSize = getPageSize();
    const normalizedStart = normalizeVisiblePageStart(safeBoardSize, start);
    return {
      start: normalizedStart,
      end: Math.min(safeBoardSize, normalizedStart + pageSize - 1),
      pageSize,
      totalPages: Math.max(1, Math.ceil(safeBoardSize / pageSize)),
      pageIndex: Math.floor((normalizedStart - 1) / pageSize) + 1,
    };
  }

  function isCellVisible(cell, rangeOrBoardSize) {
    const range =
      typeof rangeOrBoardSize === "object" && rangeOrBoardSize
        ? rangeOrBoardSize
        : getVisibleRange(rangeOrBoardSize ?? state.room?.board?.size ?? 1);
    const safeCell = Number.parseInt(String(cell ?? 0), 10) || 0;
    return safeCell >= range.start && safeCell <= range.end;
  }

  function toVisibleIndex(cell, rangeOrBoardSize) {
    const range =
      typeof rangeOrBoardSize === "object" && rangeOrBoardSize
        ? rangeOrBoardSize
        : getVisibleRange(rangeOrBoardSize ?? state.room?.board?.size ?? 1);

    if (!isCellVisible(cell, range)) {
      return null;
    }

    return Number.parseInt(String(cell), 10) - range.start + 1;
  }

  function fromVisibleIndex(index, rangeOrBoardSize) {
    const range =
      typeof rangeOrBoardSize === "object" && rangeOrBoardSize
        ? rangeOrBoardSize
        : getVisibleRange(rangeOrBoardSize ?? state.room?.board?.size ?? 1);

    const safe = Number.parseInt(String(index ?? 1), 10) || 1;
    const absolute = range.start + safe - 1;
    return absolute <= range.end ? absolute : null;
  }

  function getGridPositionByVisibleIndex(index) {
    const cols = 10;
    const safeIndex = Math.max(1, Number.parseInt(String(index ?? 1), 10) || 1);
    const rowIndex = Math.floor((safeIndex - 1) / cols);
    const colInRow = (safeIndex - 1) % cols;
    const reverse = rowIndex % 2 === 1;
    const col = reverse ? cols - colInRow : colInRow + 1;
    const row = 10 - rowIndex;
    return { row, col };
  }

  async function setVisiblePageStart(start, options = {}) {
    const boardSize = Math.max(
      1,
      Number.parseInt(
        String(options.boardSize ?? state.room?.board?.size ?? 1),
        10,
      ) || 1,
    );
    const current = normalizeVisiblePageStart(
      boardSize,
      state.visiblePageStart,
    );
    const target = normalizeVisiblePageStart(boardSize, start);

    state.visiblePageStart = target;
    if (current === target) {
      state.pageTransitioning = false;
      state.pageTransitionDirection = 0;
      return false;
    }

    const animate = Boolean(options.animate);
    if (!animate) {
      state.pageTransitioning = false;
      state.pageTransitionDirection = 0;
      if (options.renderNow !== false) {
        root.feedback?.renderAll?.();
      }
      return true;
    }

    state.pageTransitionDirection = target > current ? 1 : -1;
    state.pageTransitioning = true;
    root.feedback?.renderAll?.();

    await wait(options.durationMs ?? DEFAULT_TRANSITION_MS);

    state.pageTransitioning = false;
    state.pageTransitionDirection = 0;
    root.feedback?.renderAll?.();
    return true;
  }

  function jumpToCell(cell, options = {}) {
    const boardSize = Math.max(
      1,
      Number.parseInt(
        String(options.boardSize ?? state.room?.board?.size ?? 1),
        10,
      ) || 1,
    );
    const targetCell = clampCell(cell, boardSize);
    const targetPageStart = getPageStartForCell(targetCell);
    return setVisiblePageStart(targetPageStart, {
      animate: Boolean(options.animate),
      durationMs: options.durationMs ?? DEFAULT_TRANSITION_MS,
      boardSize,
      renderNow: options.renderNow,
    });
  }

  function getPageStartForPlayer(playerId) {
    if (!playerId) {
      return 1;
    }

    const position = root.viewState?.getPlayerPosition?.(playerId);
    if (!position) {
      return 1;
    }

    return getPageStartForCell(position);
  }

  function wait(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  root.boardPage = {
    DEFAULT_TRANSITION_MS,
    getPageSize,
    clampCell,
    getPageStartForCell,
    getLastPageStart,
    normalizeVisiblePageStart,
    getVisibleRange,
    isCellVisible,
    toVisibleIndex,
    fromVisibleIndex,
    getGridPositionByVisibleIndex,
    setVisiblePageStart,
    jumpToCell,
    getPageStartForPlayer,
  };
})();
