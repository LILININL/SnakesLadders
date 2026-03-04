(() => {
  const root = window.SNL;

  function buildSnakePath(from, to, seed = 0) {
    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const length = Math.max(1, Math.hypot(dx, dy));
    const nx = -dy / length;
    const ny = dx / length;

    const bendSign = seed % 2 === 0 ? 1 : -1;
    const mainBend = Math.min(54, Math.max(16, length * 0.22)) * bendSign;
    const tailBend = mainBend * -0.72;
    const c1 = lerpPoint(from, to, 0.33);
    const c2 = lerpPoint(from, to, 0.69);
    c1.x += nx * mainBend;
    c1.y += ny * mainBend;
    c2.x += nx * tailBend;
    c2.y += ny * tailBend;

    return {
      c1,
      c2,
      d: `M ${from.x} ${from.y} C ${c1.x} ${c1.y} ${c2.x} ${c2.y} ${to.x} ${to.y}`,
    };
  }

  function pointAtPath(pathData, t) {
    const progress = clamp(t, 0, 1);
    if (pathData.kind === "snake") {
      return cubicPoint(
        pathData.from,
        pathData.c1,
        pathData.c2,
        pathData.to,
        progress,
      );
    }

    return lerpPoint(pathData.from, pathData.to, progress);
  }

  function angleAtPath(pathData, t) {
    const progress = clamp(t, 0, 1);
    if (pathData.kind === "snake") {
      const tangent = cubicTangent(
        pathData.from,
        pathData.c1,
        pathData.c2,
        pathData.to,
        progress,
      );
      return Math.atan2(tangent.y, tangent.x) * (180 / Math.PI);
    }

    return (
      Math.atan2(
        pathData.to.y - pathData.from.y,
        pathData.to.x - pathData.from.x,
      ) *
      (180 / Math.PI)
    );
  }

  function cubicPoint(p0, p1, p2, p3, t) {
    const a = 1 - t;
    const aa = a * a;
    const tt = t * t;
    const aaa = aa * a;
    const ttt = tt * t;
    return {
      x: aaa * p0.x + 3 * aa * t * p1.x + 3 * a * tt * p2.x + ttt * p3.x,
      y: aaa * p0.y + 3 * aa * t * p1.y + 3 * a * tt * p2.y + ttt * p3.y,
    };
  }

  function cubicTangent(p0, p1, p2, p3, t) {
    const a = 1 - t;
    return {
      x:
        3 * a * a * (p1.x - p0.x) +
        6 * a * t * (p2.x - p1.x) +
        3 * t * t * (p3.x - p2.x),
      y:
        3 * a * a * (p1.y - p0.y) +
        6 * a * t * (p2.y - p1.y) +
        3 * t * t * (p3.y - p2.y),
    };
  }

  function lerpPoint(a, b, t) {
    return {
      x: a.x + (b.x - a.x) * t,
      y: a.y + (b.y - a.y) * t,
    };
  }

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  root.jumpGeometry = {
    buildSnakePath,
    pointAtPath,
    angleAtPath,
    lerpPoint,
  };
})();
