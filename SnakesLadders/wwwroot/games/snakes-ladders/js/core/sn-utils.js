(() => {
  const root = window.SNL;
  const MIN_AVATAR_ID = 1;
  const MAX_AVATAR_ID = 11;
  const DEFAULT_GAME_KEY = "snakes-ladders";
  const GAME_LABELS = {
    "snakes-ladders": "Snakes & Ladders",
    monopoly: "เกมเศรษฐี",
  };
  const ITEM_META = {
    0: {
      name: "Rocket Boots",
      imageSrc: "/games/snakes-ladders/assets/item/RocketBoots.png",
      desc: "เหยียบแล้วพุ่งต่อทันที +2 ช่อง",
    },
    1: {
      name: "Magnet Dice",
      imageSrc: "/games/snakes-ladders/assets/item/MagnetDice.png",
      desc: "สุ่มดันตำแหน่งทันที +1 หรือ -1",
    },
    2: {
      name: "Snake Repellent",
      imageSrc: "/games/snakes-ladders/assets/item/SnakeRepellent.png",
      desc: "กันงูครั้งถัดไป (สะสมได้)",
    },
    3: {
      name: "Ladder Hack",
      imageSrc: "/games/snakes-ladders/assets/item/LadderHack.png",
      desc: "ขึ้นบันไดครั้งถัดไปแล้วพุ่งเพิ่ม",
    },
    4: {
      name: "Banana Peel",
      imageSrc: "/games/snakes-ladders/assets/item/BananaPeel.png",
      desc: "วางกับดักถาวรหน้าคนที่นำหน้า จนมีคนเหยียบ",
    },
    5: {
      name: "Swap Glove",
      imageSrc: "/games/snakes-ladders/assets/item/Swap.png",
      desc: "สลับตำแหน่งกับคนที่อยู่เหนือกว่า",
    },
    6: {
      name: "Anchor",
      imageSrc: "/games/snakes-ladders/assets/item/Anchor.png",
      desc: "กันโดนสลับ/ผลักถอย 3 เทิร์นของผู้ใช้",
    },
    7: {
      name: "Chaos Button",
      imageSrc: "/games/snakes-ladders/assets/item/ChaosButton.png",
      desc: "สุ่มเหตุการณ์ปั่นทั้งห้อง",
    },
    8: {
      name: "Snake Row",
      imageSrc: "/games/snakes-ladders/assets/item/SnakeRow.png",
      desc: "เสกงูเป็นแถวโดยมีช่องรอด 1 ช่อง",
    },
    9: {
      name: "Bridge to Leader",
      imageSrc: "/games/snakes-ladders/assets/item/BridgetoLeader.png",
      desc: "พุ่งไปตำแหน่งเท่าผู้นำทันที",
    },
    10: {
      name: "Global Snake Round",
      imageSrc: "/games/snakes-ladders/assets/item/GlobalSnakeRound.png",
      desc: "เพิ่มงูชั่วคราวทั้งกระดาน 1 รอบ",
    },
  };
  const STATIC_IMAGE_CACHE = new Map();
  const PLAYER_ACCENT_PALETTE = [
    {
      base: "#f26445",
      bright: "#ff8f74",
      edge: "#ffd2c7",
      deep: "#b8422a",
      soft: "rgba(242, 100, 69, 0.18)",
      glow: "rgba(242, 100, 69, 0.34)",
      emblemKey: "nova",
    },
    {
      base: "#2f9cf2",
      bright: "#6bc0ff",
      edge: "#d3ecff",
      deep: "#1f6fb4",
      soft: "rgba(47, 156, 242, 0.18)",
      glow: "rgba(47, 156, 242, 0.32)",
      emblemKey: "prism",
    },
    {
      base: "#31b87c",
      bright: "#6be2aa",
      edge: "#d4f8e5",
      deep: "#228557",
      soft: "rgba(49, 184, 124, 0.18)",
      glow: "rgba(49, 184, 124, 0.32)",
      emblemKey: "crest",
    },
    {
      base: "#f0af37",
      bright: "#ffd16d",
      edge: "#ffefcf",
      deep: "#b67d14",
      soft: "rgba(240, 175, 55, 0.18)",
      glow: "rgba(240, 175, 55, 0.3)",
      emblemKey: "crown",
    },
    {
      base: "#b45cff",
      bright: "#d29cff",
      edge: "#f0ddff",
      deep: "#7a31bc",
      soft: "rgba(180, 92, 255, 0.18)",
      glow: "rgba(180, 92, 255, 0.3)",
      emblemKey: "orbit",
    },
    {
      base: "#ff5fa1",
      bright: "#ff97c2",
      edge: "#ffd6e7",
      deep: "#bc2d6b",
      soft: "rgba(255, 95, 161, 0.18)",
      glow: "rgba(255, 95, 161, 0.3)",
      emblemKey: "bloom",
    },
    {
      base: "#34c3c9",
      bright: "#74e4e8",
      edge: "#d6f9fb",
      deep: "#21848a",
      soft: "rgba(52, 195, 201, 0.18)",
      glow: "rgba(52, 195, 201, 0.32)",
      emblemKey: "compass",
    },
    {
      base: "#9da9b8",
      bright: "#c2cad4",
      edge: "#ebeff4",
      deep: "#677587",
      soft: "rgba(157, 169, 184, 0.18)",
      glow: "rgba(157, 169, 184, 0.28)",
      emblemKey: "tide",
    },
  ];

  function normalizeName(name) {
    const value = String(name ?? "").trim();
    if (!value) {
      return "";
    }
    return value.length <= 24 ? value : value.slice(0, 24);
  }

  function escapeHtml(input) {
    return String(input)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#39;");
  }

  function shortId(value) {
    const text = String(value ?? "");
    if (text.length <= 8) {
      return text;
    }
    return `${text.slice(0, 8)}...`;
  }

  function densityLabel(value) {
    return value === 0 ? "น้อย" : value === 1 ? "กลาง" : "เยอะ";
  }

  function formatClock(dateLike) {
    const date = dateLike instanceof Date ? dateLike : new Date(dateLike);
    return date.toLocaleTimeString();
  }

  function getPlayerMarkerBase(displayName) {
    const normalized = normalizeName(displayName);
    return (normalized[0] ?? "ผ").toUpperCase();
  }

  function buildPlayerMarkerMap(players) {
    const grouped = new Map();
    const markerMap = new Map();

    for (const player of players ?? []) {
      if (!player?.playerId) {
        continue;
      }

      const base = getPlayerMarkerBase(player.displayName);
      const bucket = grouped.get(base) ?? [];
      bucket.push(player);
      grouped.set(base, bucket);
    }

    for (const [base, bucket] of grouped) {
      if (bucket.length === 1) {
        markerMap.set(bucket[0].playerId, base);
        continue;
      }

      bucket.forEach((player, index) => {
        markerMap.set(player.playerId, `${base}${markerSuffix(index)}`);
      });
    }

    return markerMap;
  }

  function resolvePlayerMarker(playerId, displayName, markerMap) {
    if (markerMap instanceof Map && markerMap.has(playerId)) {
      return markerMap.get(playerId);
    }
    return getPlayerMarkerBase(displayName);
  }

  function resolvePlayerSlot(players, playerId) {
    const safePlayerId = String(playerId ?? "").trim();
    if (!safePlayerId) {
      return 0;
    }

    const index = (players ?? []).findIndex(
      (player) => String(player?.playerId ?? "").trim() === safePlayerId,
    );
    return index >= 0 ? index + 1 : 0;
  }

  function resolvePlayerAccent(players, playerId) {
    const slot = resolvePlayerSlot(players, playerId);
    const palette =
      PLAYER_ACCENT_PALETTE[
        slot > 0 ? (slot - 1) % PLAYER_ACCENT_PALETTE.length : 0
      ];

    return {
      slot,
      ...palette,
    };
  }

  function markerSuffix(index) {
    if (index >= 0 && index < 26) {
      return String.fromCharCode(65 + index);
    }
    return String(index + 1);
  }

  function normalizeAvatarId(value, fallback = MIN_AVATAR_ID) {
    const parsed = Number.parseInt(String(value ?? ""), 10);
    if (
      !Number.isFinite(parsed) ||
      parsed < MIN_AVATAR_ID ||
      parsed > MAX_AVATAR_ID
    ) {
      const safeFallback = Number.parseInt(String(fallback ?? ""), 10);
      return Number.isFinite(safeFallback) &&
        safeFallback >= MIN_AVATAR_ID &&
        safeFallback <= MAX_AVATAR_ID
        ? safeFallback
        : MIN_AVATAR_ID;
    }

    return parsed;
  }

  function avatarSrc(avatarId) {
    const safeId = normalizeAvatarId(avatarId);
    if (safeId === 9) {
      return "/games/snakes-ladders/experimental/token-3d/assets/avatar-09-token3d.svg";
    }
    const suffix = String(safeId).padStart(2, "0");
    const extension = safeId === 8 ? "gif" : "png";
    return `/games/snakes-ladders/assets/avatars/avatar-${suffix}.${extension}`;
  }

  function isModelAvatar(avatarId) {
    return normalizeAvatarId(avatarId) === 9;
  }

  function avatarMarkup(avatarId, options = {}) {
    const safeId = normalizeAvatarId(avatarId);
    const className = String(options.className ?? "").trim();
    const variant = String(options.variant ?? "inline").trim() || "inline";
    const altText = escapeHtml(String(options.alt ?? `Avatar ${safeId}`));

    if (isModelAvatar(safeId)) {
      const classes = [className, "avatar-model-preview"].filter(Boolean).join(" ");
      return `<span class="${escapeHtml(classes)}" data-avatar-model-id="${safeId}" data-avatar-model-variant="${escapeHtml(variant)}" role="img" aria-label="${altText}"></span>`;
    }

    const classAttr = className ? ` class="${escapeHtml(className)}"` : "";
    return `<img${classAttr} src="${avatarSrc(safeId)}" alt="${altText}">`;
  }

  function preloadImage(src) {
    const safeSrc = String(src ?? "").trim();
    if (!safeSrc) {
      return Promise.resolve("");
    }

    const cached = STATIC_IMAGE_CACHE.get(safeSrc);
    if (cached) {
      return cached;
    }

    const loadPromise = new Promise((resolve) => {
      const image = new Image();
      let settled = false;

      const finish = () => {
        if (settled) {
          return;
        }
        settled = true;
        resolve(safeSrc);
      };

      image.decoding = "async";
      image.onload = finish;
      image.onerror = finish;
      image.src = safeSrc;

      if (image.complete) {
        queueMicrotask(finish);
      }
    });

    STATIC_IMAGE_CACHE.set(safeSrc, loadPromise);
    return loadPromise;
  }

  function preloadAvatarAsset(avatarId) {
    const safeId = normalizeAvatarId(avatarId);
    if (isModelAvatar(safeId)) {
      return (
        window.SNL?.experimentalToken3d?.primeAvatarId?.(safeId) ??
        Promise.resolve(null)
      );
    }
    return preloadImage(avatarSrc(safeId));
  }

  function preloadUiAssets() {
    for (let avatarId = MIN_AVATAR_ID; avatarId <= MAX_AVATAR_ID; avatarId += 1) {
      void preloadAvatarAsset(avatarId);
    }

    for (const meta of Object.values(ITEM_META)) {
      if (meta?.imageSrc) {
        void preloadImage(meta.imageSrc);
      }
    }
  }

  function ensureAvatarImageElement(host, className) {
    let image = host.querySelector("img");
    if (!image) {
      image = document.createElement("img");
    }

    if (host.firstElementChild !== image || host.childElementCount !== 1) {
      host.replaceChildren(image);
    }

    if (image.className !== className) {
      image.className = className;
    }

    image.decoding = "async";
    return image;
  }

  function syncAvatarHost(host, avatarId, options = {}) {
    if (!host) {
      return;
    }

    const safeId = normalizeAvatarId(avatarId);
    const className = String(options.className ?? "").trim();
    const variant = String(options.variant ?? "inline").trim() || "inline";
    const alt = String(options.alt ?? `Avatar ${safeId}`);
    const renderedMode = host.dataset.renderedAvatarMode ?? "";

    if (
      host.dataset.renderedAvatarId === String(safeId) &&
      host.dataset.renderedAvatarClass === className &&
      host.dataset.renderedAvatarVariant === variant &&
      host.dataset.renderedAvatarAlt === alt &&
      renderedMode === (isModelAvatar(safeId) ? "model" : "image")
    ) {
      if (isModelAvatar(safeId)) {
        window.SNL?.experimentalToken3d?.mountAvatarHost?.(
          host,
          { avatarId: safeId },
          { variant },
        );
      }
      return;
    }

    if (isModelAvatar(safeId)) {
      void preloadAvatarAsset(safeId);
      const mounted = window.SNL?.experimentalToken3d?.mountAvatarHost?.(
        host,
        { avatarId: safeId },
        { variant },
      );
      if (!mounted) {
        const image = ensureAvatarImageElement(host, className);
        const src = avatarSrc(safeId);
        void preloadImage(src);
        if (image.getAttribute("src") !== src) {
          image.src = src;
        }
        if (image.alt !== alt) {
          image.alt = alt;
        }
      }
      host.dataset.renderedAvatarMode = mounted ? "model" : "image";
    } else {
      window.SNL?.experimentalToken3d?.unmountAvatarHost?.(host);
      const image = ensureAvatarImageElement(host, className);
      const src = avatarSrc(safeId);
      void preloadImage(src);
      if (image.getAttribute("src") !== src) {
        image.src = src;
      }
      if (image.alt !== alt) {
        image.alt = alt;
      }
      host.dataset.renderedAvatarMode = "image";
    }

    host.dataset.renderedAvatarId = String(safeId);
    host.dataset.renderedAvatarClass = className;
    host.dataset.renderedAvatarVariant = variant;
    host.dataset.renderedAvatarAlt = alt;
  }

  function normalizeGameKey(gameKey, fallback = DEFAULT_GAME_KEY) {
    const value = String(gameKey ?? "").trim().toLowerCase();
    if (!value) {
      return fallback;
    }
    return value;
  }

  function gameLabel(gameKey) {
    const key = normalizeGameKey(gameKey);
    return GAME_LABELS[key] ?? key;
  }

  function gameSupportsBoardOptions(gameKey) {
    if (!String(gameKey ?? "").trim()) {
      return false;
    }
    return normalizeGameKey(gameKey) === DEFAULT_GAME_KEY;
  }

  function boardItemMeta(itemType) {
    return (
      ITEM_META[itemType] ?? {
        name: "Mystery Item",
        imageSrc: "",
        desc: "ไอเท็มสุ่มพิเศษ",
      }
    );
  }

  root.utils = {
    normalizeName,
    escapeHtml,
    shortId,
    densityLabel,
    formatClock,
    buildPlayerMarkerMap,
    resolvePlayerMarker,
    resolvePlayerSlot,
    resolvePlayerAccent,
    normalizeAvatarId,
    avatarSrc,
    isModelAvatar,
    avatarMarkup,
    preloadImage,
    preloadAvatarAsset,
    preloadUiAssets,
    syncAvatarHost,
    normalizeGameKey,
    gameLabel,
    gameSupportsBoardOptions,
    boardItemMeta,
  };
})();
