(() => {
  const root = window.SNL;
  const MIN_AVATAR_ID = 1;
  const MAX_AVATAR_ID = 8;
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
    const suffix = String(safeId).padStart(2, "0");
    const extension = safeId === 8 ? "gif" : "png";
    return `/games/snakes-ladders/assets/avatars/avatar-${suffix}.${extension}`;
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
    normalizeAvatarId,
    avatarSrc,
    normalizeGameKey,
    gameLabel,
    gameSupportsBoardOptions,
    boardItemMeta,
  };
})();
