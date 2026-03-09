(() => {
  const root = window.SNL;
  const GAME_KEY = root.GAME_KEYS?.MONOPOLY ?? "monopoly";
  const { escapeHtml } = root.utils;
  let lastEconomyFrame = null;

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

    const room = state?.room ?? null;
    const roomKey = String(root.state?.roomCode ?? room?.code ?? room?.roomCode ?? "")
      .trim()
      .toUpperCase();
    const displayPlayers = root.viewState.getDisplayPlayers();
    const displayTurnPlayerId = root.viewState.getDisplayTurnPlayerId();
    const turnPosition = root.viewState.getPlayerPosition(displayTurnPlayerId);
    const freeParkingPot = resolveMonopolyFreeParkingPot(board);
    const completedRounds =
      root.monopolyHelpers?.resolveCompletedRounds?.(room) ??
      (Number.parseInt(String(room?.completedRounds ?? 0), 10) || 0);
    const tollBoostPercent =
      root.monopolyHelpers?.tollGrowthPercent?.(room) ??
      Math.max(0, completedRounds * 10);
    const cityPriceBoostPercent =
      root.monopolyHelpers?.cityPriceGrowthPercent?.(room) ?? 0;
    const nextEconomyFrame = buildEconomyFrame(roomKey, room, cells, completedRounds);
    const ringCells = cells
      .map((cell) => renderCell(state, room, cell, turnPosition))
      .join("");
    const center = renderCenter(state, room, freeParkingPot, tollBoostPercent, cityPriceBoostPercent);
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
    animateEconomyFrame(el.board, lastEconomyFrame, nextEconomyFrame);
    lastEconomyFrame = nextEconomyFrame;

    const ownedAssets = cells.filter((x) => Boolean(resolveOwnerId(x))).length;
    el.boardLegend.textContent = `กระดานเกมเศรษฐี 40 ช่อง | ทรัพย์สินที่มีเจ้าของ ${ownedAssets} | เงินกองกลาง ${money(freeParkingPot)} | ค่าผ่านทาง +${tollBoostPercent}% | ราคาเมือง +${cityPriceBoostPercent}%`;
    root.roomUi?.updateFloatingRollButton();
  }

  function clear({ el }) {
    el.board.classList.remove("monopoly-ring");
    el.board.style.setProperty("--rows", "10");
    el.board.innerHTML = "";
    el.boardLegend.textContent = "";
    hideBoardItemTooltip(el);
    lastEconomyFrame = null;

    root.boardOverlay?.clear();
    root.boardTokens?.clear();
    root.boardBeacon?.hide?.();
  }

  function renderCell(state, room, cell, turnPosition) {
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
    const ownerAccent = resolveOwnerAccent(state, ownerId);
    const estateTier = resolveEstateTier(type, houseCount, hasHotel, hasLandmark, isMortgaged);

    const classes = [
      "m-cell",
      `type-${typeLabel.toLowerCase().replaceAll(" ", "-")}`,
    ];
    if (pos.isCorner) {
      classes.push("corner");
    }
    if (ownerId) {
      classes.push("owned");
      classes.push(`estate-${estateTier.className}`);
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

    const price = currentCityPrice(cell, room);
    const toll = currentToll(cell, room);
    const rent = resolveNumber(cell?.rent ?? cell?.Rent);
    const fee = resolveNumber(cell?.fee ?? cell?.Fee);
    const metaMarkup = renderCellMeta(typeLabel, type, price, toll, fee, rent);

    const ownerChip =
      ownerName === "-"
        ? ""
        : renderOwnerChip(state, ownerId, ownerName, ownerAccent, estateTier);

    const ownerFlag =
      ownerName === "-"
        ? ""
        : renderOwnerFlag(state, ownerId, ownerName, ownerAccent, estateTier);

    const buildings = ownerId
      ? renderEstateStage(estateTier, houseCount)
      : "";
    const mortgageBadge = isMortgaged
      ? '<span class="m-mortgage" title="Mortgaged">M</span>'
      : "";

    const ownerStyle = ownerId
      ? `--owner-accent:${ownerAccent.base};--owner-accent-bright:${ownerAccent.bright};--owner-accent-edge:${ownerAccent.edge};--owner-accent-deep:${ownerAccent.deep};--owner-accent-soft:${ownerAccent.soft};--owner-accent-glow:${ownerAccent.glow};`
      : "";

    return `
      <div class="${classes.join(" ")}" data-cell="${cellNo}" style="grid-column:${pos.col};grid-row:${pos.row};${ownerStyle}" title="${escapeHtml(resolveName(cell))}">
        ${colorBand}
        ${ownerFlag}
        <div class="m-cell-top">
          <span class="m-num">${cellNo}</span>
          <span class="m-icon" aria-hidden="true">${icon}</span>
        </div>
        <div class="m-name">${escapeHtml(shortName(resolveName(cell), pos.isCorner ? 24 : 18))}</div>
        <div class="m-meta">${metaMarkup}</div>
        <div class="m-foot">
          ${ownerChip}
          ${buildings}
          ${mortgageBadge}
        </div>
      </div>
    `;
  }

  function renderCellMeta(typeLabel, type, price, toll, fee, rent) {
    const assetTypes = new Set([1, 2, 3]);
    if (assetTypes.has(type)) {
      return `
        <span class="m-type">${escapeHtml(typeLabel)}</span>
        <span class="m-metrics">
          ${renderMetricChip("ซื้อ", "price", price)}
          ${renderMetricChip("ทาง", "toll", toll)}
        </span>
      `;
    }

    const costText =
      fee > 0
        ? `-${money(fee)}`
        : rent > 0
          ? money(rent)
          : "";

    return `
      <span class="m-type">${escapeHtml(typeLabel)}</span>
      ${costText ? `<span class="m-cost">${escapeHtml(costText)}</span>` : ""}
    `;
  }

  function renderMetricChip(label, kind, value) {
    if (value <= 0) {
      return "";
    }

    return `
      <span class="m-metric m-metric-${escapeHtml(kind)}">
        <span class="m-metric-label">${escapeHtml(label)}</span>
        <span class="m-metric-value" data-economy-metric="${escapeHtml(kind)}" data-value="${value}">${escapeHtml(money(value))}</span>
      </span>
    `;
  }

  function buildEconomyFrame(roomKey, room, cells, completedRounds) {
    const metricsByCell = {};
    cells.forEach((cell) => {
      const cellNo = resolveCellNo(cell);
      metricsByCell[cellNo] = {
        price: currentCityPrice(cell, room),
        toll: currentToll(cell, room),
      };
    });

    return {
      roomKey,
      completedRounds,
      metricsByCell,
    };
  }

  function animateEconomyFrame(boardEl, previousFrame, nextFrame) {
    if (
      !boardEl ||
      !previousFrame ||
      !nextFrame ||
      previousFrame.roomKey !== nextFrame.roomKey ||
      nextFrame.completedRounds <= previousFrame.completedRounds
    ) {
      return;
    }

    Object.entries(nextFrame.metricsByCell).forEach(([cellNo, nextMetrics]) => {
      const previousMetrics = previousFrame.metricsByCell?.[cellNo];
      if (!previousMetrics) {
        return;
      }

      const cellEl = boardEl.querySelector(`.m-cell[data-cell="${cellNo}"]`);
      if (!cellEl) {
        return;
      }

      let changed = false;
      ["price", "toll"].forEach((kind) => {
        const fromValue = resolveNumber(previousMetrics[kind]);
        const toValue = resolveNumber(nextMetrics[kind]);
        if (fromValue === toValue) {
          return;
        }

        const metricEl = cellEl.querySelector(`[data-economy-metric="${kind}"]`);
        if (!metricEl) {
          return;
        }

        changed = true;
        animateMoneyValue(metricEl, fromValue, toValue, 1120);
      });

      if (!changed) {
        return;
      }

      cellEl.classList.remove("economy-bump");
      void cellEl.offsetWidth;
      cellEl.classList.add("economy-bump");
      window.setTimeout(() => cellEl.classList.remove("economy-bump"), 1300);
    });
  }

  function animateMoneyValue(targetEl, fromValue, toValue, durationMs) {
    if (!targetEl) {
      return;
    }

    const safeDuration = Math.max(280, durationMs);
    const start = performance.now();
    targetEl.dataset.value = String(toValue);
    targetEl.classList.add("economy-live");

    const render = (timestamp) => {
      const progress = Math.min(1, (timestamp - start) / safeDuration);
      const eased = 1 - Math.pow(1 - progress, 3);
      const current = Math.round(fromValue + (toValue - fromValue) * eased);
      targetEl.textContent = money(current);
      if (progress < 1) {
        requestAnimationFrame(render);
        return;
      }

      window.setTimeout(() => targetEl.classList.remove("economy-live"), 120);
    };

    requestAnimationFrame(render);
  }

  function currentCityPrice(cell, room) {
    return root.monopolyHelpers?.scaleCityPriceForCell?.(cell, room) ??
      resolveNumber(cell?.price ?? cell?.Price);
  }

  function currentToll(cell, room) {
    return root.monopolyHelpers?.previewCellToll?.(cell, room) ?? 0;
  }

  function renderOwnerChip(state, ownerId, ownerName, ownerAccent, estateTier) {
    const mineClass = ownerId === String(state?.playerId ?? "").trim() ? " mine" : "";
    const emblemClass = `emblem-${escapeHtml(ownerAccent.emblemKey || "nova")}`;
    return `<span class="m-owner${mineClass}" title="เจ้าของ: ${escapeHtml(ownerName)}">
      <span class="m-owner-mark ${emblemClass}" aria-hidden="true"><span class="owner-emblem-sigil"></span></span>
      <span class="m-owner-copy">
        <span class="m-owner-name">${escapeHtml(shortName(ownerName, 10))}</span>
        <span class="m-owner-stage">${escapeHtml(estateTier.label)}</span>
      </span>
    </span>`;
  }

  function renderOwnerFlag(state, ownerId, ownerName, ownerAccent, estateTier) {
    const mineClass = ownerId === String(state?.playerId ?? "").trim() ? " me" : "";
    const emblemClass = `emblem-${escapeHtml(ownerAccent.emblemKey || "nova")}`;
    return `<span class="m-owner-flag${mineClass} tier-${escapeHtml(estateTier.className)}" title="เจ้าของ: ${escapeHtml(ownerName)}" aria-label="เจ้าของ ${escapeHtml(ownerName)}">
      <span class="m-owner-flag-core ${emblemClass}" aria-hidden="true"><span class="owner-emblem-sigil"></span></span>
    </span>`;
  }

  function resolveOwnerAccent(state, ownerId) {
    return root.utils?.resolvePlayerAccent?.(state?.room?.players ?? [], ownerId) ?? {
      slot: 0,
      base: "#5d91b7",
      bright: "#8ac2ec",
      edge: "#d9eefb",
      deep: "#2d5e81",
      soft: "rgba(93, 145, 183, 0.16)",
      glow: "rgba(93, 145, 183, 0.3)",
      emblemKey: "nova",
    };
  }

  function renderCenter(state, room, freeParkingPot, tollBoostPercent, cityPriceBoostPercent) {
    const summaryRows = renderCenterPlayerSummary(state, room);
    return `
      <div class="m-center" style="grid-column:3 / span 7;grid-row:3 / span 7;">
        <div class="m-center-badge">CITY ESTATE</div>
        <h4 class="m-center-title">เกมเศรษฐีคลาสสิก</h4>
        <div class="m-center-sub">จ่ายทาง • ซื้อสิทธิ์ • ครองเมือง</div>
        <div class="m-economy-strip">
          <span class="m-economy-pill toll">ค่าผ่านทาง +${escapeHtml(String(tollBoostPercent))}%</span>
          <span class="m-economy-pill price">ราคาเมือง +${escapeHtml(String(cityPriceBoostPercent))}%</span>
        </div>
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
        <div class="m-center-player-list">${summaryRows}</div>
      </div>
    `;
  }

  function renderCenterPlayerSummary(state, room) {
    const economyRows = Array.isArray(room?.monopolyState?.playerEconomy)
      ? room.monopolyState.playerEconomy
      : [];
    const economyById = new Map(economyRows.map((row) => [String(row.playerId), row]));
    const players = Array.isArray(room?.players) ? room.players.slice() : [];
    const activePlayerId = String(room?.monopolyState?.activePlayerId ?? "").trim();
    const pendingPlayerId = String(room?.monopolyState?.pendingDecisionPlayerId ?? "").trim();

    return players
      .sort((left, right) => {
        const leftActive = left.playerId === activePlayerId ? 1 : 0;
        const rightActive = right.playerId === activePlayerId ? 1 : 0;
        if (leftActive !== rightActive) {
          return rightActive - leftActive;
        }

        const leftEconomy = economyById.get(String(left.playerId)) ?? {};
        const rightEconomy = economyById.get(String(right.playerId)) ?? {};
        const leftNetWorth = resolveNumber(leftEconomy?.netWorth ?? leftEconomy?.NetWorth);
        const rightNetWorth = resolveNumber(rightEconomy?.netWorth ?? rightEconomy?.NetWorth);
        return rightNetWorth - leftNetWorth;
      })
      .map((player) => {
        const playerId = String(player.playerId ?? "");
        const economy = economyById.get(playerId) ?? {};
        const accent = resolveOwnerAccent(state, playerId);
        const classes = ["m-center-player"];
        if (playerId === activePlayerId) {
          classes.push("active");
        }
        if (playerId === pendingPlayerId) {
          classes.push("pending");
        }
        if (playerId === String(state?.playerId ?? "")) {
          classes.push("me");
        }
        if (Boolean(economy?.isBankrupt ?? economy?.IsBankrupt ?? player.isBankrupt)) {
          classes.push("bankrupt");
        }

        const cash = resolveNumber(economy?.cash ?? economy?.Cash ?? player.cash);
        const netWorth = resolveNumber(economy?.netWorth ?? economy?.NetWorth);
        const propertyCount = resolveNumber(economy?.propertyCount ?? economy?.PropertyCount);
        const inJail = Boolean(economy?.inJail ?? economy?.InJail);
        const badges = [];
        if (playerId === activePlayerId) {
          badges.push('<span class="m-center-player-badge turn">ตาเล่น</span>');
        }
        if (playerId === pendingPlayerId && playerId !== activePlayerId) {
          badges.push('<span class="m-center-player-badge wait">รอตัดสินใจ</span>');
        }
        if (inJail) {
          badges.push('<span class="m-center-player-badge jail">ติดคุก</span>');
        }
        if (classes.includes("bankrupt")) {
          badges.push('<span class="m-center-player-badge out">ล้มละลาย</span>');
        }

        return `
          <div class="${classes.join(" ")}" style="--player-accent:${accent.base};--player-soft:${accent.soft};--player-edge:${accent.edge};">
            <div class="m-center-player-head">
              <span class="m-center-player-name">${escapeHtml(shortName(player.displayName, 12))}</span>
              <span class="m-center-player-cash">${money(cash)}</span>
            </div>
            <div class="m-center-player-meta">
              <span>สุทธิ <b>${money(netWorth)}</b></span>
              <span>ทรัพย์ <b>${escapeHtml(String(propertyCount))}</b></span>
            </div>
            ${badges.length ? `<div class="m-center-player-badges">${badges.join("")}</div>` : ""}
          </div>
        `;
      })
      .join("");
  }

  function resolveEstateTier(type, houseCount, hasHotel, hasLandmark, isMortgaged) {
    if (isMortgaged) {
      return {
        className: "mortgaged",
        label: "จำนอง",
        badgeLabel: "พักทรัพย์",
      };
    }

    if (type !== 1) {
      return {
        className: "owned",
        label: "ถือครอง",
        badgeLabel: "โฉนด",
      };
    }

    if (hasLandmark) {
      return {
        className: "landmark",
        label: "แลนด์มาร์ก",
        badgeLabel: "แลนด์มาร์ก",
      };
    }

    if (hasHotel) {
      return {
        className: "hotel",
        label: "โรงแรม",
        badgeLabel: "โรงแรม",
      };
    }

    if (houseCount > 0) {
      return {
        className: `house-${Math.min(4, houseCount)}`,
        label: `บ้าน ${Math.min(4, houseCount)}`,
        badgeLabel: `บ้าน ${Math.min(4, houseCount)}`,
      };
    }

    return {
      className: "owned",
      label: "ถือครอง",
      badgeLabel: "โฉนด",
    };
  }

  function renderEstateStage(estateTier, houseCount) {
    switch (estateTier.className) {
      case "landmark":
        return `
          <span class="m-estate-stage tier-landmark" title="${escapeHtml(estateTier.badgeLabel)}">
            <span class="m-estate-monument">
              <i></i><i></i><i></i>
            </span>
            <span class="m-estate-badge-text">LM</span>
          </span>
        `;
      case "hotel":
        return `
          <span class="m-estate-stage tier-hotel" title="${escapeHtml(estateTier.badgeLabel)}">
            <span class="m-estate-hotel">
              <i></i><i></i><i></i>
            </span>
            <span class="m-estate-badge-text">HT</span>
          </span>
        `;
      case "house-1":
      case "house-2":
      case "house-3":
      case "house-4":
        return `
          <span class="m-estate-stage ${escapeHtml(estateTier.className)}" title="${escapeHtml(estateTier.badgeLabel)}">
            <span class="m-estate-skyline">${new Array(Math.min(4, Math.max(1, houseCount))).fill("<i></i>").join("")}</span>
            <span class="m-estate-badge-text">H${Math.min(4, Math.max(1, houseCount))}</span>
          </span>
        `;
      case "mortgaged":
        return `
          <span class="m-estate-stage tier-mortgaged" title="${escapeHtml(estateTier.badgeLabel)}">
            <span class="m-estate-slash"></span>
            <span class="m-estate-badge-text">พัก</span>
          </span>
        `;
      default:
        return `
          <span class="m-estate-stage tier-owned" title="${escapeHtml(estateTier.badgeLabel)}">
            <span class="m-estate-plot">
              <i></i><i></i><i></i>
            </span>
            <span class="m-estate-badge-text">LOT</span>
          </span>
        `;
    }
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
