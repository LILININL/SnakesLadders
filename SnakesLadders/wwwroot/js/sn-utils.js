(() => {
  const root = window.SNL;

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

  root.utils = {
    normalizeName,
    escapeHtml,
    shortId,
    densityLabel,
    formatClock,
    buildPlayerMarkerMap,
    resolvePlayerMarker
  };
})();
