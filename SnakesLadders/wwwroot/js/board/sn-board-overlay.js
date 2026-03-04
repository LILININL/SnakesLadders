(() => {
  const root = window.SNL;
  const { el, state } = root;
  const SVG_NS = "http://www.w3.org/2000/svg";
  let resizeTimer = 0;

  function render(board, range = null) {
    if (!board || !el.board) {
      clear();
      return;
    }

    const jumps = [
      ...(Array.isArray(board.jumps) ? board.jumps : []),
      ...(Array.isArray(board.temporaryJumps) ? board.temporaryJumps : []),
    ];
    if (board.activeFrenzySnake?.type === 0) {
      jumps.push(board.activeFrenzySnake);
    }

    if (jumps.length === 0) {
      clear();
      return;
    }

    const visibleRange =
      range ??
      root.boardPage.getVisibleRange(board.size, state.visiblePageStart);
    const visibleJumps = jumps.filter(
      (jump) =>
        root.boardPage.isCellVisible(jump.from, visibleRange) &&
        root.boardPage.isCellVisible(jump.to, visibleRange),
    );

    if (visibleJumps.length === 0) {
      clear();
      return;
    }

    const boardEl = el.board;
    const width = boardEl.clientWidth;
    const height = boardEl.clientHeight;
    if (
      !Number.isFinite(width) ||
      !Number.isFinite(height) ||
      width <= 0 ||
      height <= 0
    ) {
      clear();
      return;
    }

    const svg = ensureSvg(boardEl, width, height);
    svg.replaceChildren();

    const ladderLayer = document.createElementNS(SVG_NS, "g");
    ladderLayer.setAttribute("class", "ladder-layer");
    const snakeLayer = document.createElementNS(SVG_NS, "g");
    snakeLayer.setAttribute("class", "snake-layer");

    for (const jump of visibleJumps) {
      const pathData = getJumpPathData(
        jump.from,
        jump.to,
        jump.type,
        jump.from + jump.to,
      );
      if (!pathData) {
        continue;
      }

      if (jump.type === 0) {
        drawSnake(snakeLayer, pathData);
      } else {
        drawLadder(ladderLayer, pathData.from, pathData.to);
      }
    }

    svg.appendChild(ladderLayer);
    svg.appendChild(snakeLayer);
  }

  function ensureSvg(boardEl, width, height) {
    let svg = boardEl.querySelector(".board-overlay");
    if (!svg) {
      svg = document.createElementNS(SVG_NS, "svg");
      svg.classList.add("board-overlay");
      boardEl.prepend(svg);
    }

    svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
    svg.setAttribute("width", String(width));
    svg.setAttribute("height", String(height));
    svg.style.width = `${width}px`;
    svg.style.height = `${height}px`;
    return svg;
  }

  function clear() {
    const overlay = el.board?.querySelector(".board-overlay");
    if (overlay) {
      overlay.remove();
    }
  }

  function renderFromState() {
    const board = state.room?.board;
    if (!board) {
      clear();
      return;
    }

    render(
      board,
      root.boardPage.getVisibleRange(board.size, state.visiblePageStart),
    );
  }

  function findCellCenter(cellNumber) {
    const boardEl = el.board;
    if (!boardEl) {
      return null;
    }

    const cellEl = boardEl.querySelector(`[data-cell="${cellNumber}"]`);
    if (!cellEl) {
      return null;
    }

    const boardRect = boardEl.getBoundingClientRect();
    const cellRect = cellEl.getBoundingClientRect();
    const x = cellRect.left - boardRect.left + cellRect.width / 2;
    const y = cellRect.top - boardRect.top + cellRect.height / 2;

    if (![x, y, cellRect.width, cellRect.height].every(Number.isFinite)) {
      return null;
    }

    return { x, y };
  }

  function getJumpPathData(fromCell, toCell, jumpType, seed = 0) {
    const from = findCellCenter(fromCell);
    const to = findCellCenter(toCell);
    if (!from || !to) {
      return null;
    }

    if (jumpType === 0 || jumpType === "snake") {
      const snake = root.jumpGeometry.buildSnakePath(from, to, seed);
      return {
        kind: "snake",
        from,
        to,
        c1: snake.c1,
        c2: snake.c2,
        d: snake.d,
      };
    }

    return {
      kind: "ladder",
      from,
      to,
    };
  }

  function drawSnake(layer, pathData) {
    const bodyWrap = svgNode("g", { class: "snake-wrap" });
    bodyWrap.appendChild(
      svgNode("path", { class: "snake-link-glow", d: pathData.d }),
    );
    bodyWrap.appendChild(
      svgNode("path", { class: "snake-link", d: pathData.d }),
    );
    bodyWrap.appendChild(
      svgNode("path", { class: "snake-link-stripe", d: pathData.d }),
    );
    bodyWrap.appendChild(
      svgNode("circle", {
        class: "snake-tail-dot",
        cx: pathData.to.x,
        cy: pathData.to.y,
        r: 3.2,
      }),
    );

    const heading = angleAtPath(pathData, 0);
    const head = svgNode("g", {
      class: "snake-head",
      transform: `translate(${pathData.from.x} ${pathData.from.y}) rotate(${heading})`,
    });
    head.appendChild(
      svgNode("ellipse", {
        class: "snake-head-body",
        cx: 0,
        cy: 0,
        rx: 7.6,
        ry: 5.8,
      }),
    );
    head.appendChild(
      svgNode("circle", { class: "snake-eye", cx: 2, cy: -2, r: 1.1 }),
    );
    head.appendChild(
      svgNode("circle", { class: "snake-eye", cx: 2, cy: 2, r: 1.1 }),
    );
    head.appendChild(
      svgNode("path", {
        class: "snake-tongue",
        d: "M 6.8 0 L 10 -1.5 M 6.8 0 L 10 1.5",
      }),
    );

    bodyWrap.appendChild(head);
    layer.appendChild(bodyWrap);
  }

  function drawLadder(layer, from, to) {
    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const length = Math.hypot(dx, dy);
    if (length < 10) {
      return;
    }

    const nx = -dy / length;
    const ny = dx / length;
    const railOffset = 6;
    const a1 = { x: from.x + nx * railOffset, y: from.y + ny * railOffset };
    const a2 = { x: from.x - nx * railOffset, y: from.y - ny * railOffset };
    const b1 = { x: to.x + nx * railOffset, y: to.y + ny * railOffset };
    const b2 = { x: to.x - nx * railOffset, y: to.y - ny * railOffset };

    layer.appendChild(
      svgNode("line", {
        class: "ladder-rail",
        x1: a1.x,
        y1: a1.y,
        x2: b1.x,
        y2: b1.y,
      }),
    );
    layer.appendChild(
      svgNode("line", {
        class: "ladder-rail",
        x1: a2.x,
        y1: a2.y,
        x2: b2.x,
        y2: b2.y,
      }),
    );

    const rungCount = Math.max(2, Math.min(12, Math.floor(length / 22)));
    for (let i = 1; i < rungCount; i++) {
      const t = i / rungCount;
      const r1 = root.jumpGeometry.lerpPoint(a1, b1, t);
      const r2 = root.jumpGeometry.lerpPoint(a2, b2, t);
      layer.appendChild(
        svgNode("line", {
          class: "ladder-rung",
          x1: r1.x,
          y1: r1.y,
          x2: r2.x,
          y2: r2.y,
        }),
      );
    }
  }

  function pointAtPath(pathData, t) {
    return root.jumpGeometry.pointAtPath(pathData, t);
  }

  function angleAtPath(pathData, t) {
    return root.jumpGeometry.angleAtPath(pathData, t);
  }

  function svgNode(tagName, attrs) {
    const node = document.createElementNS(SVG_NS, tagName);
    for (const [name, value] of Object.entries(attrs)) {
      node.setAttribute(name, String(value));
    }
    return node;
  }

  window.addEventListener("resize", () => {
    if (!state.room?.board) {
      return;
    }

    clearTimeout(resizeTimer);
    resizeTimer = window.setTimeout(() => renderFromState(), 120);
  });

  root.boardOverlay = {
    render,
    clear,
    renderFromState,
    getJumpPathData,
    pointAtPath,
    angleAtPath,
  };
})();
