(() => {
  const root = window.SNL;
  const GAME_KEY = root.GAME_KEYS?.SNAKES_LADDERS ?? "snakes-ladders";
  const { escapeHtml, boardItemMeta } = root.utils;
  let boardItemTooltipBound = false;

  root.boardRenderers ??= {};
  root.boardRenderers[GAME_KEY] = {
    render,
    clear,
  };

  function render({ state, el, board }) {
    if (!board) {
      clear({ el });
      return;
    }

    const page = root.boardPage.getVisibleRange(board.size, state.visiblePageStart);
    state.visiblePageStart = page.start;
    root.boardFocus?.refreshPendingBeaconTarget?.();

    const jumps = [...(board.jumps ?? []), ...(board.temporaryJumps ?? [])];
    const activeFrenzySnake =
      state.animating && state.animFrenzySnake
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
      if (trapHere) {
        marks.push(
          "<span class='jump-tag trap' title='Banana Trap: เหยียบแล้วลื่นถอย'>🍌</span>",
        );
      }

      let itemMeta = null;
      if (boardItem) {
        itemMeta = boardItemMeta(boardItem.type);
        const hasItemImage = Boolean(itemMeta.imageSrc);
        const itemVisual = hasItemImage
          ? `<img class='item-chip-img' src='${escapeHtml(itemMeta.imageSrc)}' alt='${escapeHtml(itemMeta.name)}' loading='lazy' decoding='async'>`
          : "?";
        const itemClass = hasItemImage
          ? "jump-tag item item-tag-image"
          : "jump-tag item";
        marks.push(
          `<span class='${itemClass}' title='${escapeHtml(itemMeta.name)}: ${escapeHtml(itemMeta.desc)}'>${itemVisual}</span>`,
        );
      }

      const cellTitle = [`ช่อง ${absoluteCell}`];
      if (itemMeta) {
        cellTitle.push(`${itemMeta.name}: ${itemMeta.desc}`);
      }
      if (trapHere) {
        cellTitle.push("มีกับดักกล้วย");
      }

      const hoverTip = itemMeta || trapHere ? cellTitle.slice(1).join(" | ") : "";
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
    bindBoardItemTooltip(el);
    applyBoardTransitionClasses(state, el);

    const overlayBoard =
      activeFrenzySnake && board.activeFrenzySnake !== activeFrenzySnake
        ? { ...board, activeFrenzySnake }
        : board;
    root.boardOverlay?.render(overlayBoard, page);
    root.boardTokens?.render(root.viewState.getDisplayPlayers(), displayTurnPlayerId, page);
    root.boardBeacon?.render?.();
    root.roomUi?.updateFloatingRollButton();

    el.boardLegend.textContent = `ช่วง ${page.start}-${page.end} | เส้นชัย ${board.size} | งู ${snakeHeads.size} | บันได ${ladderStarts.size} | ไอเท็ม ${itemByCell.size} | กับดัก ${bananaTrapCells.size}`;
  }

  function clear({ el }) {
    el.board.style.setProperty("--rows", "10");
    el.board.innerHTML = "";
    el.boardLegend.textContent = "";
    hideBoardItemTooltip(el);
    clearBoardClasses(el);
    root.boardOverlay?.clear();
    root.boardTokens?.clear();
    root.boardBeacon?.hide?.();
  }

  function clearBoardClasses(el) {
    el.board.classList.remove("page-transitioning", "page-forward", "page-backward");
  }

  function bindBoardItemTooltip(el) {
    if (boardItemTooltipBound || !el.board) {
      return;
    }

    boardItemTooltipBound = true;
    el.board.addEventListener("mousemove", (event) => onBoardMouseMove(event, el));
    el.board.addEventListener("mouseleave", () => hideBoardItemTooltip(el));
    el.board.addEventListener("scroll", () => hideBoardItemTooltip(el), {
      passive: true,
    });
  }

  function onBoardMouseMove(event, el) {
    if (!el.boardItemTooltip || !el.boardStage || !el.board) {
      return;
    }

    const cell = event.target?.closest?.(".cell[data-hover-tip]");
    if (!cell || !el.board.contains(cell)) {
      hideBoardItemTooltip(el);
      return;
    }

    const tip = String(cell.dataset.hoverTip ?? "").trim();
    if (!tip) {
      hideBoardItemTooltip(el);
      return;
    }

    showBoardItemTooltip(tip, cell, el);
  }

  function showBoardItemTooltip(text, cell, el) {
    if (!el.boardItemTooltip) {
      return;
    }

    el.boardItemTooltip.textContent = text;
    el.boardItemTooltip.classList.remove("hidden");
    el.boardItemTooltip.classList.add("show");
    positionBoardItemTooltip(cell, el);
  }

  function positionBoardItemTooltip(cell, el) {
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

    const rawCenterX = cellRect.left - stageRect.left + cellRect.width / 2;
    const minCenterX = margin + tipWidth / 2;
    const maxCenterX = Math.max(minCenterX, stageRect.width - margin - tipWidth / 2);
    const centerX = clamp(rawCenterX, minCenterX, maxCenterX);

    let top = cellRect.top - stageRect.top - tipHeight - 10;
    if (top < margin) {
      top = cellRect.bottom - stageRect.top + 10;
      top = Math.min(top, Math.max(margin, stageRect.height - tipHeight - margin));
      tipEl.classList.add("flip");
    } else {
      tipEl.classList.remove("flip");
    }

    tipEl.style.left = `${centerX}px`;
    tipEl.style.top = `${top}px`;
  }

  function hideBoardItemTooltip(el) {
    if (!el.boardItemTooltip) {
      return;
    }

    el.boardItemTooltip.classList.remove("show", "flip");
    el.boardItemTooltip.classList.add("hidden");
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  function applyBoardTransitionClasses(state, el) {
    const transitioning = Boolean(state.pageTransitioning);
    const forward = transitioning && state.pageTransitionDirection > 0;
    const backward = transitioning && state.pageTransitionDirection < 0;

    el.board.classList.toggle("page-transitioning", transitioning);
    el.board.classList.toggle("page-forward", forward);
    el.board.classList.toggle("page-backward", backward);
  }
})();
