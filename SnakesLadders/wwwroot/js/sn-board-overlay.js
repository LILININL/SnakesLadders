(() => {
  const root = window.SNL;
  const { el, state } = root;
  const SVG_NS = "http://www.w3.org/2000/svg";
  let resizeTimer = 0;

  function render(board) {
    if (!board || !Array.isArray(board.jumps) || board.jumps.length === 0) {
      clear();
      return;
    }

    const boardEl = el.board;
    if (!boardEl) {
      return;
    }

    let svg = boardEl.querySelector(".board-overlay");
    if (!svg) {
      svg = document.createElementNS(SVG_NS, "svg");
      svg.classList.add("board-overlay");
      boardEl.prepend(svg);
    }

    const width = boardEl.scrollWidth;
    const height = boardEl.scrollHeight;
    svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
    svg.setAttribute("width", String(width));
    svg.setAttribute("height", String(height));
    svg.style.width = `${width}px`;
    svg.style.height = `${height}px`;
    svg.replaceChildren();

    const ladderLayer = document.createElementNS(SVG_NS, "g");
    ladderLayer.setAttribute("class", "ladder-layer");
    const snakeLayer = document.createElementNS(SVG_NS, "g");
    snakeLayer.setAttribute("class", "snake-layer");

    for (const jump of board.jumps) {
      const pathData = getJumpPathData(jump.from, jump.to, jump.type, jump.from + jump.to);
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

    render(board);
  }

  function findCellCenter(cellNumber) {
    const cellEl = el.board.querySelector(`[data-cell="${cellNumber}"]`);
    if (!cellEl) {
      return null;
    }

    return {
      x: cellEl.offsetLeft + cellEl.offsetWidth / 2,
      y: cellEl.offsetTop + cellEl.offsetHeight / 2
    };
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
        d: snake.d
      };
    }

    return {
      kind: "ladder",
      from,
      to
    };
  }

  function drawSnake(layer, pathData) {
    const bodyWrap = svgNode("g", { class: "snake-wrap" });
    bodyWrap.appendChild(svgNode("path", {
      class: "snake-link-glow",
      d: pathData.d
    }));
    bodyWrap.appendChild(svgNode("path", {
      class: "snake-link",
      d: pathData.d
    }));
    bodyWrap.appendChild(svgNode("path", {
      class: "snake-link-stripe",
      d: pathData.d
    }));
    bodyWrap.appendChild(svgNode("circle", {
      class: "snake-tail-dot",
      cx: pathData.to.x,
      cy: pathData.to.y,
      r: 3.2
    }));

    const heading = angleAtPath(pathData, 0);
    const head = svgNode("g", {
      class: "snake-head",
      transform: `translate(${pathData.from.x} ${pathData.from.y}) rotate(${heading})`
    });
    head.appendChild(svgNode("ellipse", { class: "snake-head-body", cx: 0, cy: 0, rx: 6.8, ry: 5.2 }));
    head.appendChild(svgNode("circle", { class: "snake-eye", cx: 2, cy: -1.9, r: 1.05 }));
    head.appendChild(svgNode("circle", { class: "snake-eye", cx: 2, cy: 1.9, r: 1.05 }));
    head.appendChild(svgNode("path", { class: "snake-tongue", d: "M 6 0 L 9 -1.4 M 6 0 L 9 1.4" }));

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

    layer.appendChild(svgNode("line", {
      class: "ladder-rail",
      x1: a1.x,
      y1: a1.y,
      x2: b1.x,
      y2: b1.y
    }));
    layer.appendChild(svgNode("line", {
      class: "ladder-rail",
      x1: a2.x,
      y1: a2.y,
      x2: b2.x,
      y2: b2.y
    }));

    const rungCount = Math.max(2, Math.min(12, Math.floor(length / 22)));
    for (let i = 1; i < rungCount; i++) {
      const t = i / rungCount;
      const r1 = root.jumpGeometry.lerpPoint(a1, b1, t);
      const r2 = root.jumpGeometry.lerpPoint(a2, b2, t);
      layer.appendChild(svgNode("line", {
        class: "ladder-rung",
        x1: r1.x,
        y1: r1.y,
        x2: r2.x,
        y2: r2.y
      }));
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
    angleAtPath
  };
})();
