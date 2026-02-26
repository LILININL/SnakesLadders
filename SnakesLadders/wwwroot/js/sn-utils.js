(() => {
  const root = window.SNL;
  const MIN_AVATAR_ID = 1;
  const MAX_AVATAR_ID = 8;

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
    if (!Number.isFinite(parsed) || parsed < MIN_AVATAR_ID || parsed > MAX_AVATAR_ID) {
      const safeFallback = Number.parseInt(String(fallback ?? ""), 10);
      return Number.isFinite(safeFallback) && safeFallback >= MIN_AVATAR_ID && safeFallback <= MAX_AVATAR_ID
        ? safeFallback
        : MIN_AVATAR_ID;
    }

    return parsed;
  }

  function avatarSrc(avatarId) {
    const safeId = normalizeAvatarId(avatarId);
    const suffix = String(safeId).padStart(2, "0");
    const extension = safeId === 8 ? "gif" : "png";
    return `/assets/avatars/avatar-${suffix}.${extension}`;
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
    avatarSrc
  };
})();
