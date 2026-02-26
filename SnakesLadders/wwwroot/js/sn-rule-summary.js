(() => {
  const root = window.SNL;

  function buildRoomRuleLines(options) {
    const safeOptions = options ?? {};
    const rules = safeOptions.ruleOptions ?? {};

    const gameMode = toInt(safeOptions.gameMode, 1);
    const boardSize = toInt(safeOptions.boardSize, 100);
    const turnSeconds = Math.max(3, toInt(rules.turnSeconds, 15));
    const maxRounds = Math.max(1, toInt(rules.maxRounds, 80));
    const frenzyEvery = Math.max(2, toInt(rules.snakeFrenzyIntervalTurns, 5));
    const checkpointEvery = Math.max(1, toInt(rules.checkpointInterval, 50));
    const mercyBoost = Math.max(3, toInt(rules.mercyLadderBoost, 12));
    const marathonThreshold = Math.max(50, toInt(rules.marathonThreshold, 300));
    const marathonMultiplier = Math.max(1, toNumber(rules.marathonLadderMultiplier, 1.2));

    const lines = [
      `โหมดห้อง: ${gameModeLabel(gameMode)}`,
      `ขนาดกระดาน: ${boardSize} ช่อง`,
      `ทอยเกินเส้นชัย: ${safeOptions.overflowMode === 1 ? "ถอยหลังตามแต้มเกิน x2" : "อยู่ที่เดิม"}`
    ];

    const featureLines = [];

    if (rules.turnTimerEnabled) {
      featureLines.push(`จับเวลาเทิร์น: คนละ ${turnSeconds} วินาที หมดเวลาแล้วระบบจะทอยให้อัตโนมัติ`);
    }

    if (rules.roundLimitEnabled) {
      featureLines.push(`จำกัดรอบ: เล่นสูงสุด ${maxRounds} รอบ แล้วตัดสินผู้ชนะจากคนที่ขึ้นไปไกลที่สุด`);
    }

    if (rules.itemsEnabled) {
      featureLines.push(gameMode === 2
        ? "Chaos Items: ไอเท็มสุ่มบนกระดาน เหยียบแล้วทำงานทันที"
        : "ระบบไอเท็ม: ไอเท็มสุ่มบนกระดาน เหยียบแล้วทำงานทันที");
    }

    if (rules.snakeFrenzyEnabled) {
      featureLines.push(`งูคลุ้มคลั่ง: ทุก ${frenzyEvery} เทิร์น จะมีงูชั่วคราวโผล่มาเพิ่ม`);
    }

    if (rules.checkpointShieldEnabled) {
      featureLines.push(`เกราะเช็กพอยต์: ผ่านทุก ${checkpointEvery} ช่อง จะได้โล่ 1 ชั้นกันงูได้ 1 ครั้ง`);
    }

    if (rules.comebackBoostEnabled) {
      featureLines.push("เร่งแซงคนตาม: คนที่รั้งท้ายจริง ๆ จะได้โบนัสแต้มทอย +1 (แต่รวมแล้วไม่เกิน 6)");
    }

    if (rules.mercyLadderEnabled) {
      featureLines.push(`บันไดเมตตา: ถ้าโดนงูกัดติดกัน 2 ครั้ง เทิร์นถัดไปจะพุ่งเพิ่มสูงสุด +${mercyBoost} ช่อง`);
    }

    if (rules.marathonSpeedupEnabled) {
      if (boardSize >= marathonThreshold) {
        featureLines.push(`เร่งเกมช่วงท้าย: กระดานนี้เข้าเงื่อนไขแล้ว เพิ่มจำนวนบันไดตอนสร้างด่าน x${formatMultiplier(marathonMultiplier)}`);
      } else {
        featureLines.push(`เร่งเกมช่วงท้าย: เปิดไว้แล้ว แต่กระดานนี้ ${boardSize} ช่อง ยังไม่ถึงเกณฑ์ ${marathonThreshold} ช่อง`);
      }
    }

    if (featureLines.length === 0) {
      lines.push("กฏพิเศษ: ใช้เฉพาะกฏพื้นฐาน (ไม่มีออปชั่นเสริม)");
    } else {
      lines.push(...featureLines);
    }

    return lines;
  }

  function gameModeLabel(value) {
    if (value === 0) return "Classic";
    if (value === 2) return "Chaos";
    return "Custom";
  }

  function formatMultiplier(value) {
    const rounded = Math.round(value * 100) / 100;
    return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(2).replace(/0+$/, "").replace(/\.$/, "");
  }

  function toInt(value, fallback) {
    const parsed = Number.parseInt(String(value ?? ""), 10);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  function toNumber(value, fallback) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }

  root.ruleSummary = {
    buildRoomRuleLines
  };
})();
