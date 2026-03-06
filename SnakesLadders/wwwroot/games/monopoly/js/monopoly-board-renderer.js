(() => {
  const root = window.SNL;
  const GAME_KEY = root.GAME_KEYS?.MONOPOLY ?? "monopoly";
  const { escapeHtml } = root.utils;

  root.boardRenderers ??= {};
  root.boardRenderers[GAME_KEY] = {
    render,
    clear,
    getCells: getMonopolyCells,
  };

  function render({ state, el, board }) {
    if (!board) {
      clear({ el });
      return;
    }

    const cells = getMonopolyCells(board)
      .slice()
      .sort((a, b) => resolveCellNo(a) - resolveCellNo(b));

    if (cells.length === 0) {
      clear({ el });
      return;
    }

    el.board.classList.add("monopoly-ring");
    el.board.style.setProperty("--rows", "11");

    const displayPlayers = root.viewState.getDisplayPlayers();
    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const turnPosition = root.viewState.getPlayerPosition(displayTurnPlayerId);
    const freeParkingPot = resolveMonopolyFreeParkingPot(board);
    const markerMap =
      root.utils?.buildPlayerMarkerMap?.(state?.room?.players ?? displayPlayers) ??
      new Map();

    const ringCells = cells
      .map((cell) => renderCell(state, cell, turnPosition, markerMap))
      .join("");
    const center = renderCenter(freeParkingPot);
    el.board.innerHTML = `${ringCells}${center}`;

    clearBoardClasses(el);
    hideBoardItemTooltip(el);
    root.boardOverlay?.clear();
    root.boardBeacon?.hide?.();

    const range = {
      start: 1,
      end: Math.max(1, Number.parseInt(String(board.size ?? 40), 10) || 40),
      pageSize: 100,
    };
    root.boardTokens?.render(
      displayPlayers,
      displayTurnPlayerId,
      range,
    );

    const ownedAssets = cells.filter((x) => Boolean(resolveOwnerId(x))).length;
    el.boardLegend.textContent = `กระดานเกมเศรษฐี 40 ช่อง | ทรัพย์สินที่มีเจ้าของ ${ownedAssets} | เงินกองกลาง ${money(freeParkingPot)}`;
    root.roomUi?.updateFloatingRollButton();
  }

  function clear({ el }) {
    el.board.classList.remove("monopoly-ring");
    el.board.style.setProperty("--rows", "10");
    el.board.innerHTML = "";
    el.boardLegend.textContent = "";
    hideBoardItemTooltip(el);

    root.boardOverlay?.clear();
    root.boardTokens?.clear();
    root.boardBeacon?.hide?.();
  }

  function renderCell(state, cell, turnPosition, markerMap) {
    const cellNo = resolveCellNo(cell);
    const pos = toBoardPosition(cellNo);
    const type = resolveType(cell);
    const typeLabel = monopolyCellTypeLabel(type);
    const icon = monopolyCellIcon(type);
    const ownerId = resolveOwnerId(cell);
    const ownerName = playerName(state, ownerId);
    const colorClass = colorClassByGroup(resolveColorGroup(cell));
    const houseCount = resolveNumber(cell?.houseCount ?? cell?.HouseCount);
    const hasHotel = Boolean(cell?.hasHotel ?? cell?.HasHotel);
    const hasLandmark = Boolean(cell?.hasLandmark ?? cell?.HasLandmark);
    const isMortgaged = Boolean(cell?.isMortgaged ?? cell?.IsMortgaged);

    const classes = [
      "m-cell",
      `type-${typeLabel.toLowerCase().replaceAll(" ", "-")}`,
    ];
    if (pos.isCorner) {
      classes.push("corner");
    }
    if (ownerId) {
      classes.push("owned");
    }
    if (turnPosition === cellNo) {
      classes.push("turn-cell");
    }
    if (isMortgaged) {
      classes.push("mortgaged");
    }

    const colorBand = colorClass
      ? `<span class="m-band ${escapeHtml(colorClass)}" aria-hidden="true"></span>`
      : '<span class="m-band" aria-hidden="true"></span>';

    const price = resolveNumber(cell?.price ?? cell?.Price);
    const rent = resolveNumber(cell?.rent ?? cell?.Rent);
    const fee = resolveNumber(cell?.fee ?? cell?.Fee);

    const costText =
      price > 0
        ? money(price)
        : fee > 0
          ? `-${money(fee)}`
          : rent > 0
            ? money(rent)
            : "";

    const ownerChip =
      ownerName === "-"
        ? ""
        : renderOwnerChip(state, ownerId, ownerName, markerMap);

    const ownerFlag =
      ownerName === "-"
        ? ""
        : renderOwnerFlag(state, ownerId, ownerName, markerMap);

    let buildings = "";
    if (type === 1 && hasLandmark) {
      buildings = '<span class="m-buildings landmark" title="แลนด์มาร์ก">🏛</span>';
    } else if (type === 1 && hasHotel) {
      buildings = '<span class="m-buildings hotel" title="โรงแรม">🏨</span>';
    } else if (type === 1 && houseCount > 0) {
      buildings = `<span class=\"m-buildings\" title=\"บ้าน\">${new Array(Math.min(4, houseCount)).fill("<i></i>").join("")}</span>`;
    }
    const mortgageBadge = isMortgaged
      ? '<span class="m-mortgage" title="Mortgaged">M</span>'
      : "";

    return `
      <div class="${classes.join(" ")}" data-cell="${cellNo}" style="grid-column:${pos.col};grid-row:${pos.row};" title="${escapeHtml(resolveName(cell))}">
        ${colorBand}
        ${ownerFlag}
        <div class="m-cell-top">
          <span class="m-num">${cellNo}</span>
          <span class="m-icon" aria-hidden="true">${icon}</span>
        </div>
        <div class="m-name">${escapeHtml(shortName(resolveName(cell), pos.isCorner ? 24 : 18))}</div>
        <div class="m-meta">
          <span class="m-type">${escapeHtml(typeLabel)}</span>
          ${costText ? `<span class=\"m-cost\">${escapeHtml(costText)}</span>` : ""}
        </div>
        <div class="m-foot">
          ${ownerChip}
          ${buildings}
          ${mortgageBadge}
        </div>
      </div>
    `;
  }

  function renderOwnerChip(state, ownerId, ownerName, markerMap) {
    const marker = resolveOwnerMarker(state, ownerId, ownerName, markerMap);
    const mineClass = ownerId === String(state?.playerId ?? "").trim() ? " mine" : "";
    return `<span class="m-owner${mineClass}" title="เจ้าของ: ${escapeHtml(ownerName)}">
      <span class="m-owner-mark" aria-hidden="true">${escapeHtml(marker)}</span>
      <span class="m-owner-name">${escapeHtml(shortName(ownerName, 10))}</span>
    </span>`;
  }

  function renderOwnerFlag(state, ownerId, ownerName, markerMap) {
    const marker = resolveOwnerMarker(state, ownerId, ownerName, markerMap);
    const mineClass = ownerId === String(state?.playerId ?? "").trim() ? " me" : "";
    return `<span class="m-owner-flag${mineClass}" title="เจ้าของ: ${escapeHtml(ownerName)}" aria-label="เจ้าของ ${escapeHtml(ownerName)}">${escapeHtml(marker)}</span>`;
  }

  function resolveOwnerMarker(state, ownerId, ownerName, markerMap) {
    const safeOwnerId = String(ownerId ?? "").trim();
    if (!safeOwnerId) {
      return "-";
    }

    const players = Array.isArray(state?.room?.players) ? state.room.players : [];
    const player = players.find(
      (x) => String(x?.playerId ?? "").trim() === safeOwnerId,
    );
    const displayName = String(player?.displayName ?? ownerName ?? "").trim();

    const marker =
      root.utils?.resolvePlayerMarker?.(safeOwnerId, displayName, markerMap) ??
      (displayName[0] ?? "ผ");
    return String(marker || "ผ").slice(0, 2).toUpperCase();
  }

  function renderCenter(freeParkingPot) {
    return `
      <div class="m-center" style="grid-column:3 / span 7;grid-row:3 / span 7;">
        <div class="m-center-badge">CITY ESTATE</div>
        <h4 class="m-center-title">เกมเศรษฐีคลาสสิก</h4>
        <div class="m-center-sub">จ่ายทาง • ซื้อสิทธิ์ • ครองเมือง</div>
        <div class="m-skyline" aria-hidden="true">
          <span style="--h:36px"></span>
          <span style="--h:54px"></span>
          <span style="--h:68px"></span>
          <span style="--h:44px"></span>
          <span style="--h:76px"></span>
          <span style="--h:48px"></span>
          <span style="--h:60px"></span>
        </div>
        <div class="m-parking">เงินกองกลาง: <strong>${money(freeParkingPot)}</strong></div>
      </div>
    `;
  }

  function toBoardPosition(cellNo) {
    const n = resolveNumber(cellNo);

    if (n <= 1) return { col: 11, row: 11, isCorner: true };
    if (n >= 2 && n <= 10) return { col: 12 - n, row: 11, isCorner: false };
    if (n === 11) return { col: 1, row: 11, isCorner: true };
    if (n >= 12 && n <= 20) return { col: 1, row: 22 - n, isCorner: false };
    if (n === 21) return { col: 1, row: 1, isCorner: true };
    if (n >= 22 && n <= 30) return { col: n - 20, row: 1, isCorner: false };
    if (n === 31) return { col: 11, row: 1, isCorner: true };
    if (n >= 32 && n <= 40) return { col: 11, row: n - 30, isCorner: false };

    return { col: 11, row: 11, isCorner: false };
  }

  function getMonopolyCells(board) {
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

  function resolveMonopolyFreeParkingPot(board) {
    return resolveNumber(
      board?.monopolyFreeParkingPot ??
        board?.MonopolyFreeParkingPot ??
        board?.monopolyfreeparkingpot ??
        0,
    );
  }

  function resolveCellNo(cell) {
    return resolveNumber(cell?.cell ?? cell?.Cell);
  }

  function resolveName(cell) {
    return String(cell?.name ?? cell?.Name ?? "-");
  }

  function resolveType(cell) {
    return resolveNumber(cell?.type ?? cell?.Type);
  }

  function resolveOwnerId(cell) {
    return String(cell?.ownerPlayerId ?? cell?.OwnerPlayerId ?? "").trim();
  }

  function resolveColorGroup(cell) {
    return String(cell?.colorGroup ?? cell?.ColorGroup ?? "").trim();
  }

  function resolveNumber(value) {
    return Number.parseInt(String(value ?? 0), 10) || 0;
  }

  function money(value) {
    if (typeof root.monopolyHelpers?.money === "function") {
      return root.monopolyHelpers.money(value);
    }

    const amount = resolveNumber(value);
    const abs = Math.abs(amount).toLocaleString("th-TH");
    return amount < 0 ? `-฿${abs}` : `฿${abs}`;
  }

  function shortName(text, maxLen) {
    const value = String(text ?? "");
    if (value.length <= maxLen) {
      return value;
    }
    return `${value.slice(0, Math.max(1, maxLen - 3))}...`;
  }

  function monopolyCellTypeLabel(type) {
    switch (type) {
      case 0:
        return "GO";
      case 1:
        return "ที่ดิน";
      case 2:
        return "รถไฟ";
      case 3:
        return "สาธารณูปโภค";
      case 4:
        return "ภาษี";
      case 5:
        return "โอกาส";
      case 6:
        return "คลังชุมชน";
      case 7:
        return "คุก";
      case 8:
        return "ที่จอดฟรี";
      case 9:
        return "ไปคุก";
      default:
        return "ช่อง";
    }
  }

  function monopolyCellIcon(type) {
    switch (type) {
      case 0:
        return "🏁";
      case 1:
        return "🏠";
      case 2:
        return "🚉";
      case 3:
        return "⚡";
      case 4:
        return "💸";
      case 5:
        return "❓";
      case 6:
        return "🎁";
      case 7:
        return "🚓";
      case 8:
        return "🅿️";
      case 9:
        return "🚨";
      default:
        return "⬜";
    }
  }

  function colorClassByGroup(group) {
    const key = String(group ?? "")
      .trim()
      .toLowerCase();
    switch (key) {
      case "brown":
        return "cg-brown";
      case "light blue":
        return "cg-light-blue";
      case "pink":
        return "cg-pink";
      case "orange":
        return "cg-orange";
      case "red":
        return "cg-red";
      case "yellow":
        return "cg-yellow";
      case "green":
        return "cg-green";
      case "dark blue":
        return "cg-dark-blue";
      case "railroad":
        return "cg-railroad";
      case "utility":
        return "cg-utility";
      default:
        return "";
    }
  }

  function playerName(state, playerId) {
    if (!playerId) {
      return "-";
    }
    const player = state.room?.players.find((x) => x.playerId === playerId);
    return player?.displayName ?? playerId;
  }

  function hideBoardItemTooltip(el) {
    if (!el.boardItemTooltip) {
      return;
    }

    el.boardItemTooltip.classList.remove("show", "flip");
    el.boardItemTooltip.classList.add("hidden");
  }

  function clearBoardClasses(el) {
    el.board.classList.remove(
      "page-transitioning",
      "page-forward",
      "page-backward",
    );
  }
})();
