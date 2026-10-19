var SourceBackground = (() => {
  var __defProp = Object.defineProperty;
  var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
  var __getOwnPropNames = Object.getOwnPropertyNames;
  var __hasOwnProp = Object.prototype.hasOwnProperty;
  var __export = (target, all) => {
    for (var name in all)
      __defProp(target, name, { get: all[name], enumerable: true });
  };
  var __copyProps = (to, from, except, desc) => {
    if (from && typeof from === "object" || typeof from === "function") {
      for (let key of __getOwnPropNames(from))
        if (!__hasOwnProp.call(to, key) && key !== except)
          __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
    }
    return to;
  };
  var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

  // tools/SourceLyrics/background-entry.ts
  var background_entry_exports = {};
  __export(background_entry_exports, {
    build: () => build,
    canvasId: () => canvasId,
    clearCover: () => clearCover,
    cover: () => cover,
    dispose: () => dispose,
    draw: () => draw,
    frameRate: () => frameRate,
    pointer: () => pointer,
    pointerLeave: () => pointerLeave,
    update: () => update
  });

  // ../echo/src/shared/constants/lyricsShaderScenes.ts
  var defaultLyricsShaderSceneCustomColor = "#7c9cff";
  var normalizeShaderSceneCustomColor = (value) => typeof value === "string" && /^#[\da-f]{6}$/i.test(value) ? value.toLowerCase() : defaultLyricsShaderSceneCustomColor;
  var defaultLyricsShaderSceneQuality = "balanced";
  var isLyricsCoverShaderSceneMode = (value) => value === "coverLiquid" || value === "coverPrism" || value === "coverFrost" || value === "coverPond" || value === "coverHolo" || value === "coverVinyl" || value === "coverNeon";

  // ../echo/src/renderer/components/lyrics/flowingLightModel.ts
  var hashFlowingLightTrackSeed = (trackKey) => {
    let hash = 2166136261;
    for (let index = 0; index < trackKey.length; index += 1) {
      hash ^= trackKey.charCodeAt(index);
      hash = Math.imul(hash, 16777619);
    }
    return (hash >>> 0) / 4294967295;
  };
  var spectrumBucketCount = 32;
  var bassBucketEnd = 8;
  var midBucketEnd = 22;
  var midFluxWeight = 0.5;
  var fluxNormalization = bassBucketEnd + midFluxWeight * (midBucketEnd - bassBucketEnd);
  var onsetRefractoryMs = 130;
  var fluxStatisticsTimeConstantMs = 1400;
  var peakHalfLifeMs = 4e3;
  var floorRiseSeconds = 2.5;
  var minimumSwingSpan = 0.06;
  var levelFloor = { energy: 0.12, bass: 0.12, mid: 0.09, treble: 0.06 };
  var initialPeak = { energy: 0.3, bass: 0.3, mid: 0.25, treble: 0.2 };
  var beatAttackSeconds = 0.07;
  var beatReleaseSeconds = 0.35;
  var activityDecaySeconds = 2.2;
  var flowVelocityEaseMs = 110;
  var maxFrameElapsedMs = 250;
  var tempoIntervalMinMs = 250;
  var tempoIntervalMaxMs = 1200;
  var tempoHistorySize = 8;
  var tempoLockConfidence = 0.6;
  var tempoLockFadeMs = 600;
  var tempoGapResetMs = 2500;
  var clampUnit = (value) => {
    const numberValue = Number(value);
    return Number.isFinite(numberValue) ? Math.max(0, Math.min(1, numberValue)) : 0;
  };
  var rmsDbToUnit = (value) => {
    const db = Number(value);
    return Number.isFinite(db) ? clampUnit((db + 54) / 54) : 0;
  };
  var smoothChannel = (current, target, elapsedMs, attackMs, releaseMs) => {
    const timeConstant = target > current ? attackMs : releaseMs;
    const weight = 1 - Math.exp(-Math.max(0, elapsedMs) / timeConstant);
    return current + (target - current) * weight;
  };
  var createBandTracker = (peak) => ({ peak, floor: 0, level: 0, swing: 0 });
  var trackBand = (band2, raw, elapsedMs, minimumLevelDivisor) => {
    const peakDecay = Math.exp(-elapsedMs * Math.LN2 / peakHalfLifeMs);
    band2.peak = Math.max(raw, band2.peak * peakDecay);
    const floorRise = elapsedMs / 1e3 / floorRiseSeconds * Math.max(minimumSwingSpan, band2.peak);
    band2.floor = Math.min(raw, band2.floor + floorRise, band2.peak);
    band2.level = clampUnit(raw / Math.max(minimumLevelDivisor, band2.peak)) ** 0.85;
    band2.swing = clampUnit((raw - band2.floor) / Math.max(minimumSwingSpan, band2.peak - band2.floor));
  };
  var createFlowingLightDrive = () => ({
    energy: 0,
    bass: 0,
    mid: 0,
    treble: 0,
    beat: 0,
    flow: 0,
    activity: 0,
    pulse: 0
  });
  var nearRatio = (value, reference, tolerance) => Math.abs(value - reference) <= reference * tolerance;
  var estimateBeatPeriod = (intervalsMs) => {
    if (intervalsMs.length < 4) {
      return { periodMs: 0, confidence: 0 };
    }
    const sorted = [...intervalsMs].sort((left, right) => left - right);
    const middle = sorted.length >> 1;
    const periodMs = sorted.length % 2 === 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    let agreeing = 0;
    for (const interval of intervalsMs) {
      if (nearRatio(interval, periodMs, 0.12) || nearRatio(interval, periodMs * 2, 0.12) || nearRatio(interval * 2, periodMs, 0.12)) {
        agreeing += 1;
      }
    }
    return { periodMs, confidence: agreeing / intervalsMs.length };
  };
  var createFlowingLightDriver = () => {
    const drive = createFlowingLightDrive();
    const previousSpectrum = new Float32Array(spectrumBucketCount);
    const bands = {
      energy: createBandTracker(initialPeak.energy),
      bass: createBandTracker(initialPeak.bass),
      mid: createBandTracker(initialPeak.mid),
      treble: createBandTracker(initialPeak.treble)
    };
    const targets = { energy: 0, bass: 0, mid: 0, treble: 0 };
    let lastStatus;
    let lastIngestAt = null;
    let lastOnsetAt = Number.NEGATIVE_INFINITY;
    let previousEnergy = 0;
    let previousTransient = 0;
    let fluxMean = 0.015;
    let fluxDeviation = 0.01;
    let beatPeak = 0;
    let beatAttackElapsed = beatAttackSeconds;
    let flowVelocity = 0;
    const onsetIntervals = [];
    let beatPeriodMs = 0;
    let tempoConfidence = 0;
    let tempoLock = 0;
    let beatPhase = 0;
    let phaseCorrection = 0;
    let telemetryAgeMs = 0;
    let onsetAgeMs = Number.POSITIVE_INFINITY;
    const resetTempo = () => {
      onsetIntervals.length = 0;
      beatPeriodMs = 0;
      tempoConfidence = 0;
      phaseCorrection = 0;
    };
    const trackTempo = (nowMs) => {
      const interval = nowMs - lastOnsetAt;
      if (!Number.isFinite(interval) || interval > tempoGapResetMs) {
        resetTempo();
      } else if (interval >= tempoIntervalMinMs && interval <= tempoIntervalMaxMs) {
        onsetIntervals.push(interval);
        if (onsetIntervals.length > tempoHistorySize) {
          onsetIntervals.shift();
        }
        const estimate = estimateBeatPeriod(onsetIntervals);
        tempoConfidence = estimate.confidence;
        if (estimate.periodMs > 0) {
          beatPeriodMs = beatPeriodMs > 0 ? beatPeriodMs + (estimate.periodMs - beatPeriodMs) * 0.35 : estimate.periodMs;
        }
      }
      if (tempoConfidence >= tempoLockConfidence) {
        const offset = beatPhase - Math.round(beatPhase);
        phaseCorrection = -offset;
      } else {
        beatPhase = 0;
      }
    };
    const beatAttackShape = (elapsedSeconds) => 0.5 - 0.5 * Math.cos(Math.PI * Math.min(1, elapsedSeconds / beatAttackSeconds));
    const triggerBeat = (strength) => {
      const current = drive.beat;
      beatPeak = Math.max(beatPeak, current, strength);
      const resume = beatPeak > 0 ? clampUnit(current / beatPeak) : 0;
      beatAttackElapsed = Math.acos(1 - 2 * resume) / Math.PI * beatAttackSeconds;
    };
    const ingest = (status, nowMs) => {
      if (status === lastStatus) {
        return false;
      }
      lastStatus = status;
      const priming = lastIngestAt === null;
      const elapsedMs = priming ? 33 : Math.max(1, Math.min(1e3, nowMs - lastIngestAt));
      lastIngestAt = nowMs;
      telemetryAgeMs = 0;
      const active = status?.state === "playing" || status?.state === "loading";
      if (!active) {
        targets.energy = 0;
        targets.bass = 0;
        targets.mid = 0;
        targets.treble = 0;
        previousSpectrum.fill(0);
        previousEnergy = 0;
        previousTransient = 0;
        resetTempo();
        return false;
      }
      const levels = status.audioLevels;
      const spectrum = Array.isArray(levels?.visualSpectrum) ? levels.visualSpectrum : null;
      const bucketCount = spectrum ? Math.min(spectrumBucketCount, spectrum.length) : 0;
      let bassSum = 0;
      let midSum = 0;
      let trebleSum = 0;
      let flux = 0;
      for (let index = 0; index < spectrumBucketCount; index += 1) {
        const value = index < bucketCount ? clampUnit(spectrum[index]) : 0;
        if (index < bassBucketEnd) {
          bassSum += value;
        } else if (index < midBucketEnd) {
          midSum += value;
        } else {
          trebleSum += value;
        }
        const delta = value - previousSpectrum[index];
        if (delta > 0 && index < midBucketEnd) {
          flux += delta * (index < bassBucketEnd ? 1 : midFluxWeight);
        }
        previousSpectrum[index] = value;
      }
      const bassRaw = bassSum / bassBucketEnd;
      const midRaw = midSum / (midBucketEnd - bassBucketEnd);
      const trebleRaw = trebleSum / (spectrumBucketCount - midBucketEnd);
      const hasSpectrum = bucketCount > 0 && bassSum + midSum + trebleSum > 1e-3;
      flux /= fluxNormalization;
      const energyRaw = typeof levels?.visualEnergy === "number" && Number.isFinite(levels.visualEnergy) ? clampUnit(levels.visualEnergy) : rmsDbToUnit(levels?.estimatedOutputRmsDb ?? levels?.inputRmsDb);
      const transient = clampUnit(levels?.visualTransient);
      if (hasSpectrum) {
        flux += Math.max(0, transient - previousTransient) * 0.25;
      } else {
        flux = Math.max(0, energyRaw - previousEnergy) * 1.6 + Math.max(0, transient - previousTransient) * 0.4;
      }
      previousEnergy = energyRaw;
      previousTransient = transient;
      trackBand(bands.energy, energyRaw, elapsedMs, levelFloor.energy);
      targets.energy = bands.energy.level * 0.55 + bands.energy.swing * 0.45;
      if (hasSpectrum) {
        trackBand(bands.bass, bassRaw, elapsedMs, levelFloor.bass);
        trackBand(bands.mid, midRaw, elapsedMs, levelFloor.mid);
        trackBand(bands.treble, trebleRaw, elapsedMs, levelFloor.treble);
        targets.bass = bands.bass.swing * 0.85 + bands.bass.level * 0.15;
        targets.mid = bands.mid.swing * 0.85 + bands.mid.level * 0.15;
        targets.treble = bands.treble.swing * 0.85 + bands.treble.level * 0.15;
      } else {
        targets.bass = targets.energy * 0.9;
        targets.mid = targets.energy * 0.6;
        targets.treble = targets.energy * 0.3;
      }
      const threshold = fluxMean + fluxDeviation * 1.7 + 0.01;
      let onset = false;
      if (!priming && flux > threshold && nowMs - lastOnsetAt >= onsetRefractoryMs && energyRaw > 0.02) {
        const relative = (flux - fluxMean) / (fluxMean + fluxDeviation * 2 + 0.02);
        const strength = clampUnit(0.3 + relative * 0.25) * (0.55 + 0.45 * bands.energy.level);
        if (strength > 0.1) {
          onset = true;
          trackTempo(nowMs);
          lastOnsetAt = nowMs;
          onsetAgeMs = 0;
          triggerBeat(strength);
          drive.activity = Math.min(1, drive.activity + 0.22 * strength);
        }
      }
      if (!priming) {
        const statisticsWeight = 1 - Math.exp(-elapsedMs / fluxStatisticsTimeConstantMs);
        fluxMean += (flux - fluxMean) * statisticsWeight;
        fluxDeviation += (Math.abs(flux - fluxMean) - fluxDeviation) * statisticsWeight;
      }
      return onset;
    };
    const advance = (elapsedMs) => {
      const frameMs = Math.max(0, Math.min(maxFrameElapsedMs, Number.isFinite(elapsedMs) ? elapsedMs : 0));
      const dt = frameMs / 1e3;
      telemetryAgeMs += frameMs;
      onsetAgeMs += frameMs;
      if (telemetryAgeMs > 750) {
        targets.energy = targets.bass = targets.mid = targets.treble = 0;
        resetTempo();
      }
      if (onsetAgeMs > Math.min(tempoGapResetMs, Math.max(1e3, beatPeriodMs * 2))) {
        resetTempo();
      }
      drive.energy = smoothChannel(drive.energy, targets.energy, frameMs, 35, 300);
      drive.bass = smoothChannel(drive.bass, targets.bass, frameMs, 25, 220);
      drive.mid = smoothChannel(drive.mid, targets.mid, frameMs, 35, 260);
      drive.treble = smoothChannel(drive.treble, targets.treble, frameMs, 25, 180);
      beatPeak *= Math.exp(-dt / beatReleaseSeconds);
      beatAttackElapsed = Math.min(beatAttackSeconds, beatAttackElapsed + dt);
      drive.beat = beatPeak * beatAttackShape(beatAttackElapsed);
      if (drive.beat < 1e-3) {
        drive.beat = 0;
        beatPeak = 0;
      }
      drive.activity *= Math.exp(-dt / activityDecaySeconds);
      const flowVelocityTarget = 0.1 + drive.energy * 0.22 + drive.activity * 0.12 + drive.beat * 0.5;
      flowVelocity = smoothChannel(flowVelocity, flowVelocityTarget, frameMs, flowVelocityEaseMs, flowVelocityEaseMs);
      drive.flow += dt * flowVelocity;
      const lockTarget = tempoConfidence >= tempoLockConfidence && beatPeriodMs > 0 ? 1 : 0;
      tempoLock = smoothChannel(tempoLock, lockTarget, frameMs, tempoLockFadeMs, tempoLockFadeMs);
      if (beatPeriodMs > 0) {
        const correction = phaseCorrection * (1 - Math.exp(-frameMs / 180));
        phaseCorrection -= correction;
        beatPhase += frameMs / beatPeriodMs + correction;
        beatPhase -= Math.floor(beatPhase);
      }
      const periodic = (0.5 + 0.5 * Math.cos(beatPhase * Math.PI * 2)) ** 2;
      const pulseTarget = tempoLock * periodic + (1 - tempoLock) * drive.beat;
      drive.pulse = smoothChannel(drive.pulse, pulseTarget, frameMs, 35, 90);
    };
    return { drive, ingest, advance };
  };

  // ../echo/src/renderer/components/lyrics/flowingLightEnvelope.ts
  var createFlowingLightEnvelope = () => {
    const visible = createFlowingLightDrive();
    const velocity = createFlowingLightDrive();
    let primed = false;
    const smooth = (key, target, dt) => {
      const omega = key === "beat" || key === "pulse" ? 40 : 24;
      const decay = Math.exp(-omega * dt);
      const offset = visible[key] - target;
      const step = (velocity[key] + omega * offset) * dt;
      const next = target + (offset + step) * decay;
      velocity[key] = (velocity[key] - omega * step) * decay;
      visible[key] = Math.max(0, Math.min(1, next));
      if (visible[key] !== next) velocity[key] = 0;
    };
    return (target, elapsedMs) => {
      if (!primed) {
        primed = true;
        Object.assign(visible, target);
        return visible;
      }
      const dt = Math.max(0, Math.min(250, Number.isFinite(elapsedMs) ? elapsedMs : 0)) / 1e3;
      smooth("energy", target.energy, dt);
      smooth("bass", target.bass, dt);
      smooth("mid", target.mid, dt);
      smooth("treble", target.treble, dt);
      smooth("beat", target.beat, dt);
      smooth("pulse", target.pulse, dt);
      smooth("activity", target.activity, dt);
      visible.flow = target.flow;
      return visible;
    };
  };

  // ../echo/src/renderer/components/lyrics/shader-scenes/coverSceneInteraction.ts
  var coverSceneRippleSlots = 4;
  var coverSceneTrailPoints = 12;
  var idleAfterSeconds = 4;
  var rippleLifeSeconds = 4;
  var trailLifeSeconds = 3;
  var trailMinStep = 4e-3;
  var trailMinIntervalSeconds = 0.03;
  var deadAge = 99;
  var clamp01 = (value) => Math.min(1, Math.max(0, value));
  var createCoverSceneInteraction = () => {
    const pointer2 = new Float32Array([0.5, 0.5]);
    const ripples = new Float32Array(coverSceneRippleSlots * 3);
    const trail = new Float32Array(coverSceneTrailPoints * 3);
    for (let slot = 0; slot < coverSceneRippleSlots; slot += 1) {
      ripples[slot * 3 + 2] = deadAge;
    }
    for (let point = 0; point < coverSceneTrailPoints; point += 1) {
      trail[point * 3] = -10;
      trail[point * 3 + 1] = -10;
      trail[point * 3 + 2] = deadAge;
    }
    let targetX = 0.5;
    let targetY = 0.5;
    let lastInputAt = Number.NEGATIVE_INFINITY;
    let now = 0;
    let lastPushAt = Number.NEGATIVE_INFINITY;
    let lastPushX = -10;
    let lastPushY = -10;
    return {
      pointer: pointer2,
      ripples,
      trail,
      move: (x, y) => {
        targetX = clamp01(x);
        targetY = clamp01(y);
        lastInputAt = now;
      },
      leave: () => {
        lastInputAt = Math.min(lastInputAt, now - idleAfterSeconds + 1);
      },
      press: (x, y) => {
        let oldest = 0;
        for (let slot = 1; slot < coverSceneRippleSlots; slot += 1) {
          if (ripples[slot * 3 + 2] > ripples[oldest * 3 + 2]) oldest = slot;
        }
        ripples[oldest * 3] = clamp01(x);
        ripples[oldest * 3 + 1] = clamp01(y);
        ripples[oldest * 3 + 2] = 0;
        lastInputAt = now;
      },
      step: (elapsedSeconds, timeSeconds) => {
        const dt = Math.max(0, Math.min(0.25, elapsedSeconds));
        now = timeSeconds;
        const idle = now - lastInputAt > idleAfterSeconds;
        if (idle) {
          targetX = 0.5 + 0.2 * Math.sin(now * 0.23) + 0.06 * Math.sin(now * 0.61 + 1.3);
          targetY = 0.5 + 0.13 * Math.cos(now * 0.31) + 0.05 * Math.cos(now * 0.83);
        }
        const follow = 1 - Math.exp(-dt * (idle ? 5 : 14));
        pointer2[0] += (targetX - pointer2[0]) * follow;
        pointer2[1] += (targetY - pointer2[1]) * follow;
        for (let slot = 0; slot < coverSceneRippleSlots; slot += 1) {
          const age = slot * 3 + 2;
          if (ripples[age] < deadAge) {
            ripples[age] = ripples[age] + dt > rippleLifeSeconds ? deadAge : ripples[age] + dt;
          }
        }
        for (let point = 0; point < coverSceneTrailPoints; point += 1) {
          const age = point * 3 + 2;
          if (trail[age] < deadAge) {
            trail[age] = trail[age] + dt > trailLifeSeconds ? deadAge : trail[age] + dt;
          }
        }
        const trailX = idle ? pointer2[0] : targetX;
        const trailY = idle ? pointer2[1] : targetY;
        const moved = Math.hypot(trailX - lastPushX, trailY - lastPushY);
        if (moved > trailMinStep && now - lastPushAt >= trailMinIntervalSeconds) {
          trail.copyWithin(3, 0, (coverSceneTrailPoints - 1) * 3);
          trail[0] = trailX;
          trail[1] = trailY;
          trail[2] = 0;
          lastPushAt = now;
          lastPushX = trailX;
          lastPushY = trailY;
        }
      }
    };
  };

  // native:resource-owner
  var registerLyricsResourceOwner = () => () => {
  };

  // ../echo/src/renderer/components/lyrics/shader-scenes/flatSceneHelpers.ts
  var flatSceneHelpers = `// Rotate a colour about the grey axis by an angle given as (sin, cos); the angles used are constants.
vec3 hueShiftSC(vec3 c, float s, float co) {
  vec3 k = vec3(0.57735027);
  return c * co + cross(k, c) * s + k * dot(k, c) * (1.0 - co);
}
vec3 pastel(vec3 c, float k) { return mix(c, vec3(1.0), k); }
mat2 rot(float a) { float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }

// Four-point sparkle, size r.
float sparkle(vec2 p, float r) {
  vec2 a = abs(p);
  float d = min(a.x * 4.5 + a.y, a.y * 4.5 + a.x);
  return smoothstep(r, 0.0, d);
}

// finish() without the light re-ink: this scene authors both themes itself.
vec3 finishFlat(vec3 color, vec2 fragCoord) {
  vec2 off = (fragCoord / u_resolution - 0.5) * vec2(1.0, 0.85);
  float edge = smoothstep(0.35, 0.95, length(off) * 1.35);
  color *= 1.0 - mix(0.4, 0.07, u_light) * edge;
  color += (hash21(fragCoord + vec2(fract(u_time * 0.37) * 131.0, 7.0)) - 0.5) / 255.0 * 2.0;
  return clamp(color, 0.0, 1.0);
}
`;

  // ../echo/src/renderer/components/lyrics/shader-scenes/bfsScene.ts
  var buildBfsSceneSource = (prelude2) => `${prelude2}
${flatSceneHelpers}
const int FONT[10] = int[10](31599, 11415, 29671, 29647, 23497, 31183, 31215, 29257, 31727, 31695);

// A tiny 3x5 pixel digit; lp is in glyph pixels with y running down.
float digitPx(vec2 lp, int n) {
  if (lp.x < 0.0 || lp.x >= 3.0 || lp.y < 0.0 || lp.y >= 5.0) return 0.0;
  int col = int(lp.x);
  int row = int(lp.y);
  return float((FONT[n] >> (14 - (row * 3 + col))) & 1);
}

// A smooth, trig-free wave in -1..1 with period 1; cheap enough to run for every node of every pixel.
float softWave(float x) {
  float f = abs(fract(x) * 2.0 - 1.0);
  return f * f * (3.0 - 2.0 * f) * 2.0 - 1.0;
}

// The mesh: one jittered, slowly drifting node per lattice cell.
vec2 nodePos(vec2 id, float t) {
  float h = hash21(id);
  vec2 r = vec2(fract(h * 37.17), fract(h * 91.73));
  return id + 0.5 + (r - 0.5) * 0.62 + 0.06 * vec2(softWave(t * 0.0637 + h * 6.366), softWave(t * 0.0525 + h * 3.66));
}

// Squared distance to a segment; the square root is only paid for segments that are actually near.
float segDist2(vec2 p, vec2 a, vec2 b, out float s) {
  vec2 pa = p - a;
  vec2 ba = b - a;
  s = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
  vec2 d = pa - ba * s;
  return dot(d, d);
}

// Night: light is added. Day: ink is laid on the paper, with its own (usually lighter) strength.
vec3 paint(vec3 c, vec3 darkTone, vec3 inkTone, float a, float aInk) {
  return mix(c + darkTone * a, mix(c, inkTone, clamp(aInk, 0.0, 1.0)), u_light);
}

const float PERIOD = 15.0;

// How strongly a wave lights something at distance d from its source: the bright front,
// plus a fading afterglow behind it.
float waveGlow(float rad, float d, float env, float w) {
  float rd = rad - d;
  if (rd < -2.7 * w) return 0.0;   // ahead of the front: nothing to light yet
  return env * (exp(-(rd * rd) / (w * w)) + 0.4 * smoothstep(0.0, 1.6, rd) * (0.3 + 0.7 * exp(-max(rd, 0.0) * 0.22)));
}

// ---- the mesh, as file-scope state so the 16 edge tests can be plain, fully unrolled calls
vec2 gQ;
vec2 gBase;
float gT;
float gLw;
vec2 gP[9];
float gDist[18];
float gRad[2];
float gEnv[2];
float gWid[2];
float gLineBase;
float gLineLit;
float gLineHalo;
float gLineAge;
float gLinePkt;
float gLineFringe;

// One mesh edge from node ia to node b (indices into the 3x3 neighbourhood).
void edgeAt(int ia, int b) {
  vec2 q = gQ;
  float lw = gLw;
  float t = gT;
  float s;
  float ds2 = segDist2(q, gP[ia], gP[b], s);
  if (ds2 > 0.0144) return;   // beyond ~0.12 cells even the soft halo has faded out
  float ds = sqrt(ds2);
  float glow = 0.0;
  float age = 0.0;
  float gBest = 0.0;
  float fdir = 1.0;
  for (int k = 0; k < 2; k++) {
    float dd = mix(gDist[ia * 2 + k], gDist[b * 2 + k], s);
    float gk = waveGlow(gRad[k], dd, gEnv[k], gWid[k]);
    glow += gk;
    if (gk > gBest) {
      gBest = gk;
      fdir = gDist[b * 2 + k] - gDist[ia * 2 + k];
    }
    age = max(age, (gRad[k] - dd) * gEnv[k]);
  }
  float m = 1.0 - smoothstep(lw * 0.5, lw * 1.5, ds);
  float lit = m * min(glow, 1.4);
  if (lit > gLineLit) {
    gLineLit = lit;
    gLineAge = age;
  }
  gLineBase = max(gLineBase, m);
  float fr = (ds - lw * 2.4) / lw;
  gLineFringe = max(gLineFringe, exp(-fr * fr * 0.7) * min(glow, 1.2));
  gLineHalo += exp(-ds / (lw * 4.0)) * (1.0 - smoothstep(0.06, 0.12, ds)) * min(glow, 1.4) * 0.2;
  // a packet of light runs outward along every edge the search has already walked
  float gate = smoothstep(0.12, 0.4, glow);
  if (gate > 0.0) {
    vec2 ida = gBase + vec2(float(ia % 3 - 1), float(ia / 3 - 1));
    float eh = hash21(ida + vec2(float(b - ia) * 0.173, 5.1));
    float fs = fdir > 0.0 ? s : 1.0 - s;
    float behind = fract(fract(t * (0.2 + 0.12 * eh) + eh * 7.0) - fs);
    float pk = exp(-behind * 13.0) * smoothstep(0.0, 0.03, behind);
    gLinePkt = max(gLinePkt, pk * gate * (1.0 - smoothstep(lw * 0.8, lw * 3.2, ds)));
  }
}

void main() {
  vec2 fragCoord = gl_FragCoord.xy;
  vec2 uv = (fragCoord - 0.5 * u_resolution) / u_resolution.y;
  float t = u_time;
  float L = u_light;
  const float S = 9.0;                       // lattice cells per screen height
  // a very slow drift of the whole sheet, so the mesh never sits still
  vec2 pan = vec2(softWave(t * 0.00796 + 0.25) * 1.4, softWave(t * 0.00589) * 0.7);
  vec2 q = uv * S + pan;
  float pxq = 1.4 / u_resolution.y * S;      // one pixel, in lattice units

  // ---- the two overlapping breadth-first waves
  vec2 src[2];
  float rad[2];
  float env[2];
  float wid[2];
  for (int k = 0; k < 2; k++) {
    float tk = t / PERIOD + 0.5 * float(k);
    float ep = floor(tk);
    float u = fract(tk);
    src[k] = floor(vec2((hash21(vec2(ep, 1.0 + 7.0 * float(k))) - 0.5) * S * 1.8, (hash21(vec2(ep, 3.0 + 5.0 * float(k))) - 0.5) * S * 0.9)) + 0.5;
    rad[k] = S * 2.0 * (1.0 - pow(1.0 - u, 1.35));   // eases out: the front slows as it runs out of graph
    wid[k] = 0.7 + 0.5 * u;                           // and softens, like diffusion
    env[k] = smoothstep(0.0, 0.06, u) * (1.0 - smoothstep(0.7, 1.0, u));
  }

  // ---- palette of the wave: white-hot at the front, cooling to the scene colour, then to a neighbouring hue
  vec3 hot = mix(u_colors[3], vec3(1.0), 0.55);
  vec3 warm = u_colors[3];
  vec3 cool = hueShiftSC(u_colors[2], 0.42, 0.91);
  vec3 lineTone = mix(u_colors[2], u_colors[3], 0.15);
  vec3 inkHot = mix(u_colors[2], u_colors[0], 0.35);
  vec3 inkCool = mix(hueShiftSC(u_colors[2], 0.42, 0.91), u_colors[0], 0.3);
  vec3 fringeOut = hueShiftSC(u_colors[3], 0.5, 0.87);
  vec3 inkLine = mix(u_colors[1], u_colors[2], 0.55) * 0.9;
  vec3 inkWash = pastel(u_colors[2], 0.62);

  // ---- paper / night, with the light of whatever has been explored
  float g = smoothstep(-0.6, 0.6, uv.y + 0.15 * uv.x);
  vec3 darkBg = mix(u_colors[1] * 0.5, u_colors[0] * 0.75, g);
  darkBg += u_colors[2] * 0.07 * exp(-length(uv - vec2(0.2, 0.05)) * 2.2);
  vec3 lightBg = mix(vec3(0.99, 0.975, 0.965), pastel(u_colors[2], 0.9), (1.0 - g) * 0.7);
  vec3 color = mix(darkBg, lightBg, L);

  // a broad, soft shaft of light that crosses the sheet once a minute
  {
    float sh = uv.x * 0.85 + uv.y * 0.5 - (fract(t / 60.0) * 3.6 - 1.8);
    float band = exp(-sh * sh / 0.1) * (0.7 + 0.3 * valueNoise(uv * 3.0 + vec2(0.0, t * 0.05)));
    color = paint(color, warm * 0.05, vec3(1.0), band, band * 0.16);
  }

  // two slow, wide washes of colour in opposite corners, like light leaking in at the edge of the frame
  {
    vec2 c1 = uv - vec2(-0.78, 0.42);
    vec2 c2 = uv - vec2(0.82, -0.44);
    float g1 = exp(-dot(c1, c1) * 2.4) * (0.75 + 0.25 * softWave(t * 0.021));
    float g2 = exp(-dot(c2, c2) * 2.4) * (0.75 + 0.25 * softWave(t * 0.017 + 0.5));
    color = paint(color, cool * 0.13, inkCool, g1, g1 * 0.14);
    color = paint(color, fringeOut * 0.1, inkHot, g2, g2 * 0.12);
  }

  // soft out-of-focus nodes far behind the mesh, which swell a little when a wave passes
  {
    const float S2 = 3.2;
    vec2 bq = uv * S2 + vec2(t * 0.01, 0.0) + 4.0;
    vec2 bc = floor(bq);
    vec2 bf = fract(bq);
    float bh = hash21(bc + 31.0);
    if (bh > 0.4) {
      vec2 bp = bf - (0.32 + 0.36 * vec2(fract(bh * 37.1), fract(bh * 91.7)));
      float bd = length(bp);
      if (bd < 0.3) {
      float wave = 0.0;
      for (int k = 0; k < 2; k++) {
        vec2 world = (bc + 0.5 - 4.0 - vec2(t * 0.01, 0.0)) / S2 * S;   // roughly where this orb sits, in lattice units
        wave += waveGlow(rad[k], length(world - src[k]), env[k], wid[k] + 0.6);
      }
      float orb = (1.0 - smoothstep(0.1, 0.3, bd)) * (0.55 + 0.45 * smoothstep(0.12, 0.3, bd));
      float breathe = 0.7 + 0.3 * sin(t * (0.3 + bh * 0.4) + bh * 30.0);
      color = paint(color, warm * 0.13, inkWash, orb * breathe * (0.25 + 0.9 * min(wave, 1.0)), orb * breathe * (0.35 + 0.6 * min(wave, 1.0)));
      }
    }
  }

  // A faint, drifting haze over what each wave has already explored. There is deliberately no drawn
  // front or ring: the wave is read from the nodes and edges it lights, not from a circle.
  for (int k = 0; k < 2; k++) {
    float dp = length(q - src[k]);
    if (rad[k] - dp < 0.0) continue;
    float inside = smoothstep(rad[k], rad[k] - 5.0, dp) * env[k];
    float mist = 0.55 + 0.9 * valueNoise(q * 0.42 + vec2(t * 0.06, -t * 0.04));
    color = paint(color, lineTone * 0.07, inkWash, inside * 0.5 * mist, inside * 0.25 * mist);
    // faint rays fanning out of the source, strongest near it and gone with the explored haze
    vec2 rdir = (q - src[k]) / max(dp, 0.001);
    float rn = valueNoise(rdir * 5.0 + vec2(float(k) * 7.0, t * 0.04));
    float rays = rn * rn * exp(-dp * 0.28) * smoothstep(0.3, 1.5, dp) * inside;
    color = paint(color, hot * 0.26, vec3(1.0), rays, rays * 0.4);
  }

  // faint registration marks on the lattice points, like graph paper
  {
    vec2 c = abs(fract(q + 0.5) - 0.5);
    float plus = (1.0 - smoothstep(0.0, pxq * 0.9, min(c.x, c.y))) * (1.0 - smoothstep(0.05, 0.09, max(c.x, c.y)));
    color = paint(color, lineTone * 0.1, inkLine, plus * 0.6, plus * 0.2);
  }

  // ---- nodes of the 3x3 neighbourhood and how far each wave has got
  vec2 base = floor(q);
  float diagFlip[9];
  for (int j = 0; j < 3; j++) {
    for (int i = 0; i < 3; i++) {
      int idx = j * 3 + i;
      gP[idx] = nodePos(base + vec2(float(i - 1), float(j - 1)), t);
      for (int k = 0; k < 2; k++) {
        gDist[idx * 2 + k] = length(gP[idx] - src[k]);
      }
      diagFlip[idx] = step(0.5, hash21(base + vec2(float(i - 1), float(j - 1)) + 13.0));
    }
  }

  // ---- edges: each cell is split by one diagonal, so the lattice is a triangle mesh
  gQ = q;
  gBase = base;
  gT = t;
  // a gentle tilt-shift: towards the screen edges the mesh softens, as if the focus sat in the middle
  float defocus = smoothstep(0.5, 1.05, length(uv * vec2(0.85, 1.35)));
  gLw = pxq * 0.7 * (1.0 + 1.4 * defocus);
  for (int k = 0; k < 2; k++) {
    gRad[k] = rad[k];
    gEnv[k] = env[k];
    gWid[k] = wid[k];
  }
  gLineBase = 0.0;
  gLineLit = 0.0;
  gLineHalo = 0.0;
  gLineAge = 0.0;
  gLinePkt = 0.0;
  gLineFringe = 0.0;
  // Nodes sit within 0.37 of their cell centre, so an edge lying wholly in the outer row or column
  // of the 3x3 block is at least 0.13 from any pixel of the middle cell: out of reach, never tested.
  edgeAt(3, 4); edgeAt(4, 5); edgeAt(1, 4); edgeAt(4, 7);
  if (diagFlip[0] > 0.5) edgeAt(0, 4); else edgeAt(1, 3);
  if (diagFlip[1] > 0.5) edgeAt(1, 5); else edgeAt(2, 4);
  if (diagFlip[3] > 0.5) edgeAt(3, 7); else edgeAt(4, 6);
  if (diagFlip[4] > 0.5) edgeAt(4, 8); else edgeAt(5, 7);
  float lineBase = gLineBase;
  float lineLit = gLineLit;
  float lineHalo = gLineHalo;
  float lineAge = gLineAge;
  float linePkt = gLinePkt;
  vec3 edgeTone = mix(hot, cool, smoothstep(0.0, 6.0, lineAge));
  vec3 edgeInk = mix(inkHot, inkCool, smoothstep(0.0, 6.0, lineAge));
  float focus = 1.0 - 0.4 * defocus;
  color = paint(color, lineTone * 0.24, inkLine, lineBase * 0.5 * focus, lineBase * 0.38 * focus);
  color = paint(color, edgeTone * 0.95, edgeInk, lineLit * 0.9, lineLit * 0.85);
  color = paint(color, edgeTone * 0.32, edgeInk, lineHalo * 0.6, lineHalo * 0.25);
  color = paint(color, hot * 0.9, inkHot, linePkt * 0.85, linePkt * 0.5);
  color = paint(color, fringeOut * 0.3, inkCool, gLineFringe * 0.4, gLineFringe * 0.06);

  // ---- nodes, sparkles on the ones the front is crossing, and the hop count each node reports
  for (int idx = 0; idx < 9; idx++) {
    vec2 d2 = q - gP[idx];
    float d = length(d2);
    if (d > 0.75) continue;
    float hN = hash21(base + vec2(float(idx % 3 - 1), float(idx / 3 - 1)));
    float nr = 0.05 + 0.025 * fract(hN * 7.7);
    // how strongly each wave is lighting this node, and how long ago it passed
    float lit = 0.0;
    float tr = 0.0;
    float nAge = 0.0;
    for (int k = 0; k < 2; k++) {
      float rd = rad[k] - gDist[idx * 2 + k];
      if (rd < -2.7 * wid[k]) continue;
      float l = env[k] * exp(-(rd * rd) / (wid[k] * wid[k]));
      if (l > 0.02 || rd > 0.0) nAge = max(nAge, rd * env[k]);
      lit += l;
      tr = max(tr, env[k] * smoothstep(0.0, 1.6, rd) * (0.3 + 0.7 * exp(-max(rd, 0.0) * 0.22)));
    }
    tr = min(tr, 1.0);
    vec3 tone = mix(hot, cool, smoothstep(0.0, 6.0, nAge));
    vec3 ink = mix(inkHot, inkCool, smoothstep(0.0, 6.0, nAge));
    float nrLit = nr * (1.0 + 0.9 * min(lit, 1.0));
    float dot0 = 1.0 - smoothstep(nrLit - pxq, nrLit + pxq, d);
    color = paint(color, lineTone * 0.8, inkLine, dot0 * 0.55, dot0 * 0.55);
    color = paint(color, tone * 1.4, ink, dot0 * min(lit + tr * 0.55, 1.5), dot0 * min(lit + tr * 0.55, 1.0));
    color = paint(color, tone * 0.5, ink, exp(-d / 0.17) * (lit * 0.7 + tr * 0.1), exp(-d / 0.17) * lit * 0.12);
    // each node sends a ripple outward as the front passes it, which fades as it grows
    for (int k = 0; k < 2; k++) {
      float rd = rad[k] - gDist[idx * 2 + k];
      if (rd < -0.15) continue;
      float rip = env[k] * smoothstep(-0.15, 0.25, rd) * exp(-max(rd, 0.0) * 0.8);
      float rr = 0.1 + 0.4 * (1.0 - exp(-max(rd, 0.0) * 0.8));
      float ringM = (1.0 - smoothstep(0.0, pxq * 1.3, abs(d - rr))) * rip;
      color = paint(color, tone * 0.5, ink, ringM * 0.55, ringM * 0.4);
    }
    // idle nodes blink now and then, out of step with each other
    float tw = pow(max(0.0, sin(t * (0.35 + 0.3 * fract(hN * 5.3)) + hN * 60.0)), 40.0);
    color = paint(color, warm * 0.9, inkHot, dot0 * tw * 0.7 + exp(-d / 0.2) * tw * 0.22, dot0 * tw * 0.4);
    // soft bloom around the nodes the front is crossing
    color = paint(color, tone * 0.35, ink, exp(-d / 0.45) * smoothstep(0.75, 0.35, d) * lit * 0.3, exp(-d / 0.45) * lit * 0.08);
    // what a wave has already explored keeps a soft glow around its nodes, so the lit region reads as faceted light
    float cloud = exp(-d / 0.55) * smoothstep(0.75, 0.25, d) * tr;
    color = paint(color, tone * 0.3, ink, cloud * 0.34, cloud * 0.1);
    // a four-point sparkle flares as the front crosses
    float flare = smoothstep(0.55, 1.0, lit);
    if (flare > 0.0) {
      float sp = sparkle(d2, 0.07 + 0.38 * flare) * flare;
      color = paint(color, hot * 0.9, inkHot, sp, sp * 0.6);
      // and a thin anamorphic streak
      float streak = exp(-abs(d2.y) / 0.012) * smoothstep(0.7, 0.0, abs(d2.x)) * flare;
      color = paint(color, fringeOut * 0.5, inkCool, streak * 0.45, streak * 0.1);
    }
    if (hN > 0.35) {
      float dpx = 0.034;
      vec2 lp = vec2((d2.x - 0.13) / dpx, (0.3 - d2.y) / dpx);
      if (lp.x > -0.5 && lp.x < 3.5 && lp.y > -0.5 && lp.y < 5.5) {
        float best = 0.0;
        int num = 0;
        for (int k = 0; k < 2; k++) {
          float dk = gDist[idx * 2 + k];
          float reached = env[k] * smoothstep(0.0, 1.2, rad[k] - dk) * (0.12 + 0.88 * exp(-max(rad[k] - dk, 0.0) * 0.45));
          if (reached > best) {
            best = reached;
            num = int(mod(floor(dk + 0.5), 10.0));
          }
        }
        float inkPx = digitPx(floor(lp), num);
        color = paint(color, tone * 0.85, ink, inkPx * best * 0.85, inkPx * best * 0.8);
      }
    }
  }

  // a few large, soft discs drifting in front of the mesh, out of focus
  {
    vec2 fq = uv * 2.6 + vec2(t * 0.012, -t * 0.02) + 7.0;
    vec2 fc = floor(fq);
    float fh = hash21(fc + 71.0);
    if (fh > 0.72) {
      vec2 fp = fract(fq) - (0.3 + 0.4 * vec2(fract(fh * 37.1), fract(fh * 91.7)));
      float fd = length(fp);
      float fr = 0.07 + 0.09 * fract(fh * 13.3);
      float disc = (1.0 - smoothstep(fr * 0.6, fr, fd)) * (0.55 + 0.45 * smoothstep(fr * 0.5, fr, fd));
      float tw = 0.6 + 0.4 * sin(t * (0.4 + fh) + fh * 50.0);
      color = paint(color, warm * 0.5, vec3(1.0), disc * tw * 0.3, disc * tw * 0.4);
    }
  }

  // ---- beacons at the two sources
  for (int k = 0; k < 2; k++) {
    vec2 sd = q - src[k];
    float dd = length(sd);
    if (dd < 4.0) {
      float e = env[k];
      color = paint(color, hot * 0.1, inkHot, exp(-dd / 1.1) * e * 0.4, exp(-dd / 1.1) * e * 0.08);
    }
    if (dd < 1.4) {
      float e = env[k];
      float core = exp(-dd / 0.2) * e;
      float diamond = abs(sd.x) + abs(sd.y);
      float brk = (1.0 - smoothstep(0.0, pxq * 1.3, abs(diamond - (0.34 + 0.05 * sin(t * 2.0 + float(k) * 3.0))))) * e;
      float spin = sparkle(rot(t * 0.3) * sd, 0.12) * e;
      color = paint(color, hot * 0.6, inkHot, core * 0.9 + spin * 0.8, core * 0.4 + spin * 0.5);
      color = paint(color, hot * 0.5, inkHot, brk * 0.8, brk * 0.7);
    }
  }

  outputColor = vec4(finishFlat(color, fragCoord), 1.0);
}
`;

  // ../echo/src/renderer/components/lyrics/shader-scenes/coverScenes.ts
  var common = `
uniform sampler2D u_coverA;
uniform sampler2D u_coverB;
uniform float u_coverMix;
uniform float u_hasCover;
uniform vec2 u_pointer;
uniform vec3 u_rip[4];
uniform vec3 u_trail[12];

// A small procedural material for missing artwork. Keep it independent of the pointer and time:
// the selected scene owns motion, refraction and interaction, just as it does with a real cover.
// This needs no extra texture, image decode or retained canvas.
vec3 paletteArtwork(vec2 uv, float lod) {
  float body = smoothstep(0.05, 0.95, uv.x * 0.65 + uv.y * 0.35);
  vec3 col = mix(u_colors[1], u_colors[2], body);
  float disc = 1.0 - smoothstep(0.20, 0.23 + lod * 0.012, length(uv - vec2(0.65, 0.60)));
  float bands = 0.5 + 0.5 * sin((uv.x + uv.y * 0.4) * 36.0);
  bands = mix(0.5, bands, 1.0 / (1.0 + lod * lod));
  col = mix(col, u_colors[0], bands * 0.55);
  return mix(col, u_colors[3], disc * 0.75);
}

// Current cover, cross-faded from the previous one while a track change settles.
vec3 coverSoft(vec2 uv, float lod) {
  uv = clamp(uv, 0.002, 0.998);
  if (u_hasCover < 0.5) return paletteArtwork(uv, lod);
  vec3 a = textureLod(u_coverA, uv, lod).rgb;
  if (u_coverMix >= 0.999) return a;
  return mix(textureLod(u_coverB, uv, lod).rgb, a, u_coverMix);
}

vec3 coverTex(vec2 uv) {
  return coverSoft(uv, 0.0);
}

// Window uv -> cover uv: the cover fills the width at zoom z and is cropped vertically.
vec2 coverUv(vec2 uv, float z) {
  return (uv - 0.5) * vec2(z, z * u_resolution.y / u_resolution.x) + 0.5;
}

// Press ripples: a displacement for the sampling point and a signed wave for a faint glint.
void ripples(vec2 uv, vec2 asp, out vec2 rip, out float wave) {
  rip = vec2(0.0);
  wave = 0.0;
  for (int i = 0; i < 4; i++) {
    vec3 r = u_rip[i];
    if (r.z > 5.0) continue;
    vec2 dd = (uv - r.xy) * asp;
    float rd = length(dd);
    float front = r.z * 0.55;
    float env = exp(-abs(rd - front) * 10.0) * exp(-r.z * 1.6);
    float w = sin((rd - front) * 34.0) * env;
    rip += normalize(dd + vec2(0.0001)) * w * 0.03;
    wave += w;
  }
}

`;
  var liquid = `
vec3 scene(vec2 uv, vec2 asp, float t, vec2 rip) {
  vec2 p = uv * asp * 2.2;
  vec2 w = vec2(fbm3(p + vec2(t * 0.10, 0.0)), fbm3(p + vec2(3.1, t * 0.12))) - 0.5;
  vec2 dm = (uv - u_pointer) * asp;
  vec2 push = dm * exp(-dot(dm, dm) * 7.0) * 0.20;
  vec2 suv = coverUv(uv, 0.62) + w * 0.30 + push * 0.5 + rip;
  suv = clamp(suv, 0.002, 0.998);
  vec3 col;
  col.r = coverSoft(suv + w * 0.02, 2.5).r;
  col.g = coverSoft(suv, 2.5).g;
  col.b = coverSoft(suv - w * 0.02, 2.5).b;
  float sheen = valueNoise(p * 1.3 + w * 2.0 + vec2(t * 0.05));
  float hl = sheen * sheen * sheen * 0.35;
  col += hl * mix(u_colors[3], vec3(1.0), 0.5);
  return col * 1.08;
}
`;
  var prism = `
vec3 scene(vec2 uv, vec2 asp, float t, vec2 rip) {
  // Restore the original drifting irregular facets. Keep both neighbours so the
  // refracted artwork AND face lighting blend at a seam, not just its bright outline.
  vec2 p = uv * asp * 4.0;
  vec2 ip = floor(p);
  vec2 fp = fract(p);
  float d1 = 8.0;
  float d2 = 8.0;
  vec2 cid1 = vec2(0.0);
  vec2 cid2 = vec2(0.0);
  for (int j = -1; j <= 1; j++) {
    for (int i = -1; i <= 1; i++) {
      vec2 g = vec2(float(i), float(j));
      vec2 o = vec2(hash21(ip + g), hash21(ip + g + 17.3));
      o = 0.5 + 0.45 * sin(t * 0.3 + 6.2831853 * o);
      vec2 r = g + o - fp;
      float d = dot(r, r);
      if (d < d1) {
        d2 = d1; cid2 = cid1;
        d1 = d; cid1 = ip + g;
      } else if (d < d2) {
        d2 = d; cid2 = ip + g;
      }
    }
  }
  float gap = sqrt(d2) - sqrt(d1);
  // A pixel-sized floor also covers the derivative cusp where nearest faces swap.
  float aa = max(fwidth(gap) * 1.5, 6.0 / u_resolution.y);
  float neighbour = 0.5 * (1.0 - smoothstep(0.0, aa, gap));
  vec2 dm = (uv - u_pointer) * asp;
  float near = exp(-dot(dm, dm) * 5.0);
  vec2 base = coverUv(uv, 0.62) + rip;
  float strength = 0.05 + 0.10 * near;
  vec2 off1 = vec2(hash21(cid1), hash21(cid1 + 9.1)) - 0.5;
  vec2 off2 = vec2(hash21(cid2), hash21(cid2 + 9.1)) - 0.5;
  vec3 c1 = coverSoft(base + off1 * strength, 1.6) * (0.82 + 0.35 * hash21(cid1 + 3.3));
  vec3 c2 = coverSoft(base + off2 * strength, 1.6) * (0.82 + 0.35 * hash21(cid2 + 3.3));
  vec3 c = mix(c1, c2, neighbour);
  float glint = 1.0 - smoothstep(0.0, max(0.06, aa), gap);
  c = mix(c, mix(u_colors[3], vec3(1.0), 0.6), glint * 0.22);
  return c;
}
`;
  var frost = `
// How clear the glass is at uv: the union of the pointer's recent path, fading with its age.
float wipe(vec2 uv, vec2 asp) {
  float m = 0.0;
  for (int i = 0; i < 11; i++) {
    vec3 a = u_trail[i];
    vec3 b = u_trail[i + 1];
    if (a.z >= 3.0) break;
    if (b.z >= 3.0) b = a;
    // Most pixels are outside this segment: skip its projection and fade arithmetic.
    vec2 lo = min(a.xy, b.xy) - vec2(0.055) / asp;
    vec2 hi = max(a.xy, b.xy) + vec2(0.055) / asp;
    if (any(lessThan(uv, lo)) || any(greaterThan(uv, hi))) continue;
    vec2 pa = (uv - a.xy) * asp;
    vec2 ba = (b.xy - a.xy) * asp;
    float h = clamp(dot(pa, ba) / max(dot(ba, ba), 0.000001), 0.0, 1.0);
    vec2 delta = pa - ba * h;
    float d2 = dot(delta, delta);
    float life = clamp(1.0 - mix(a.z, b.z, h) / 3.0, 0.0, 1.0);
    float r = 0.055 * (0.6 + 0.4 * life);
    m = max(m, (1.0 - smoothstep(r * r * 0.25, r * r, d2)) * life);
  }
  return m;
}

vec3 scene(vec2 uv, vec2 asp, float t, vec2 rip) {
  vec2 suv = coverUv(uv, 0.72);
  float m = wipe(uv, asp);
  // Mip levels are built once per cover, instead of blurring the entire screen every frame.
  float clarity = smoothstep(0.02, 0.85, m);
  vec3 fog = mix(coverSoft(suv, 4.5), vec3(0.85), 0.18);
  float grain = hash21(floor(uv * vec2(900.0, 900.0 / asp.x))) - 0.5;
  fog += grain * 0.025;
  vec3 clear = coverTex(suv + rip) * 1.05;
  vec3 col = mix(fog, clear, clarity);
  float rim = smoothstep(0.15, 0.5, m) * (1.0 - smoothstep(0.5, 0.9, m));
  return col + rim * 0.18;
}
`;
  var pond = `
// Water surface as a height field: its analytic slope bends the artwork underneath, the pointer leaves a wake,
// and a press drops a ring. Specular glints and faint caustics ride on the same slope.
vec3 scene(vec2 uv, vec2 asp, float t, vec2 rip) {
  vec2 p = uv * asp * 5.0;
  vec3 a = fbmDOctaves(p + vec2(t * 0.20, -t * 0.15), 3);
  vec3 b = fbmDOctaves(p * 2.1 - vec2(t * 0.10, t * 0.25), 2);
  vec2 g = a.yz + 1.05 * b.yz;
  vec2 dm = (uv - u_pointer) * asp;
  float d = length(dm);
  float wake = exp(-d * 7.0) * sin(d * 46.0 - t * 5.0);
  vec2 wd = dm / max(d, 0.0001) * wake * 0.012;
  vec2 suv = clamp(coverUv(uv, 0.66) + g * 0.010 + rip * 1.8 + wd, 0.002, 0.998);
  vec3 col = coverSoft(suv, 1.0);
  vec3 n = normalize(vec3(-(g * 0.30 + rip * 14.0 + wd * 18.0), 1.0));
  float spec = pow(max(dot(n, normalize(vec3(-0.4, 0.6, 0.7))), 0.0), 48.0);
  float caus = pow(clamp(length(g) * 0.9, 0.0, 1.0), 2.0);
  col = mix(col, col * mix(mix(u_colors[1], u_colors[2], 0.5), vec3(1.0), 0.5), 0.35);
  col += caus * 0.10 * mix(u_colors[3], vec3(1.0), 0.6) + spec * 0.55;
  return col * 0.95;
}
`;
  var lumaHelpers = `
// The shared mip chain stands in for a blur: radius in uv -> mip level of the 512px cover.
vec3 blurTex(vec2 q, float r) {
  return coverSoft(clamp(q, 0.002, 0.998), log2(max(r * 512.0, 1.0)));
}
float lumAt(vec2 q) {
  return dot(coverSoft(clamp(q, 0.002, 0.998), 0.8), vec3(0.299, 0.587, 0.114));
}
`;
  var holo = `
vec3 scene(vec2 uv, vec2 asp, float t, vec2 rip){
  vec2 b=coverUv(uv,0.70)+rip;
  float e=0.005;
  vec2 gr=vec2(lumAt(b+vec2(e,0.0))-lumAt(b-vec2(e,0.0)), lumAt(b+vec2(0.0,e))-lumAt(b-vec2(0.0,e)))/(2.0*e);
  vec3 n=normalize(vec3(-gr*0.05,1.0));
  vec3 v=normalize(vec3((u_pointer-uv)*asp*0.9,1.0));
  float h=lumAt(b);
  float ph=dot(n,v)*9.0+h*3.0+t*0.08+uv.x*1.5;
  vec3 rb=0.5+0.5*cos(6.2831853*(ph+vec3(0.0,0.33,0.67)));
  float edge=clamp(length(gr)*0.06,0.0,1.0);
  vec3 c=blurTex(clamp(b,0.002,0.998),0.003);
  float tw=0.7+0.3*valueNoise(uv*asp*140.0+t*0.3);
  vec3 col=c*0.58+rb*(0.20+0.45*edge)*tw;
  col+=pow(max(dot(n,normalize(v+vec3(0.0,0.0,0.6))),0.0),24.0)*0.35;
  return col;
}
`;
  var vinyl = `
vec2 rot2(vec2 p, float a) { float c = cos(a); float s = sin(a); return vec2(c * p.x - s * p.y, s * p.x + c * p.y); }
vec3 scene(vec2 uv, vec2 asp, float t, vec2 rip){
  vec2 p=(uv-0.5)*asp;
  float r=length(p);
  float th=atan(p.y,p.x);
  vec2 lm=(u_pointer-0.5)*asp+vec2(0.0001,0.0);
  float phi=atan(lm.y,lm.x);
  float R=0.92;
  vec3 bg=blurTex(clamp(coverUv(uv,0.9),0.002,0.998),0.04)*0.26;
  vec3 col=bg*(1.0-0.5*step(R,r)*exp(-(r-R)*8.0));
  if(r<R){
    float gi=floor(r*420.0);
    float gh=hash21(vec2(gi,3.7));
    float gr=0.5+0.5*sin(r*420.0*6.2831853);
    vec3 base=vec3(0.030)+vec3(0.018)*gr;
    float g1=(r-0.50)/0.007; float g2=(r-0.66)/0.007; float g3=(r-0.79)/0.007;
    float gap=exp(-g1*g1)+exp(-g2*g2)+exp(-g3*g3);
    float w=pow(abs(cos(th-phi)),14.0);
    float body=smoothstep(0.31,0.52,r)*(1.0-smoothstep(0.84,0.92,r));
    float sheen=pow(abs(cos(th-phi)),3.0)*0.05;
    vec3 tint=mix(vec3(1.0),u_colors[3],0.30);
    vec3 spec=tint*(w*(0.45+0.55*gh)*(1.0-0.55*gap)+sheen+gap*0.07)*body;
    float dust=step(0.9965,hash21(vec2(floor((th-t*0.2)*320.0),floor(r*60.0))));
    col=base+spec*0.75+dust*0.10*(0.3+w);
    col+=smoothstep(R-0.014,R,r)*0.10;
    float lr=0.31;
    vec2 q=rot2(p,t*0.07)/lr;
    vec3 lab=coverTex(clamp(0.5+q*0.43,0.002,0.998))*0.98;
    lab*=1.0-0.45*smoothstep(0.26,0.31,r);
    lab+=pow(abs(cos(th-phi)),2.0)*0.05*(1.0-min(r/lr,1.0));
    float hole=1.0-smoothstep(0.011,0.013,r);
    lab=mix(lab,vec3(0.02),hole);
    float hr=(r-0.016)/0.003;
    lab+=exp(-hr*hr)*0.2;
    float inL=1.0-smoothstep(lr-0.004,lr,r);
    float lead=1.0-smoothstep(0.31,0.335,r);
    col=mix(col,vec3(0.015),lead*(1.0-inL));
    col=mix(col,lab,inL);
  }
  return col;
}
`;
  var neon = `
float lumB(vec2 q){ return dot(blurTex(clamp(q,0.002,0.998),0.004),vec3(0.299,0.587,0.114)); }
vec3 scene(vec2 uv, vec2 asp, float t, vec2 rip){
  vec2 b=coverUv(uv,0.78)+rip;
  float e=0.0035;
  vec2 g=vec2(lumB(b+vec2(e,0.0))-lumB(b-vec2(e,0.0)), lumB(b+vec2(0.0,e))-lumB(b-vec2(0.0,e)))/(2.0*e);
  float line=smoothstep(0.30,0.85,1.0-exp(-length(g)*0.045));
  float e2=0.013;
  vec2 g2=vec2(lumAt(b+vec2(e2,0.0))-lumAt(b-vec2(e2,0.0)), lumAt(b+vec2(0.0,e2))-lumAt(b-vec2(0.0,e2)))/(2.0*e2);
  float glow=1.0-exp(-length(g2)*0.05);
  vec3 c=coverTex(clamp(b,0.002,0.998));
  float mx=max(c.r,max(c.g,c.b));
  vec3 hue=clamp(c/max(mx,0.06),0.0,1.0);
  hue=clamp(mix(vec3(dot(hue,vec3(0.3333))),hue,1.5),0.0,1.0);
  hue=mix(hue,vec3(1.0),line*0.5);
  float md=length((uv-u_pointer)*asp);
  float power=0.40+0.85*exp(-md*md*2.6);
  float buzz=step(0.965,hash21(vec2(floor(t*7.0),3.1)));
  float fl=1.0-0.16*buzz-0.04*(0.5+0.5*sin(t*1.9+b.x*3.0));
  vec3 bg=blurTex(clamp(b,0.002,0.998),0.03)*0.07;
  return bg+hue*(line*1.15+glow*0.45)*power*fl;
}
`;
  var main = `
void main() {
  vec2 fragCoord = gl_FragCoord.xy;
  vec2 uv = fragCoord / u_resolution;
  vec2 asp = vec2(u_resolution.x / u_resolution.y, 1.0);
  vec2 rip;
  float wave;
  ripples(uv, asp, rip, wave);
  vec3 col = scene(uv, asp, u_time, rip);
  col += wave * 0.10 * mix(u_colors[3], vec3(1.0), 0.4);
  // Dark only: the artwork is dimmed a little so the lyrics stay readable on top of it.
  col *= 0.86;
  outputColor = vec4(finishFlat(col, fragCoord), 1.0);
}
`;
  var scenes = { coverLiquid: liquid, coverPrism: prism, coverFrost: frost, coverPond: pond, coverHolo: holo, coverVinyl: vinyl, coverNeon: neon };
  var needsLumaHelpers = (mode2) => mode2 === "coverHolo" || mode2 === "coverVinyl" || mode2 === "coverNeon";
  var buildCoverSceneSource = (prelude2, mode2) => `${prelude2}
${flatSceneHelpers}
${common}
${needsLumaHelpers(mode2) ? lumaHelpers : ""}
${scenes[mode2]}
${main}`;

  // ../echo/src/renderer/components/lyrics/shader-scenes/sakuraScene.ts
  var buildSakuraSceneSource = (prelude2) => `${prelude2}
${flatSceneHelpers}


// Petal body: egg-shaped, widest near the tip, with the small notch cherry petals have.
// "shape" hands the shading what the distance already worked out: x = rim ratio (0 centre .. 1 rim),
// y = distance to the notch.
float petalSDF(vec2 p, out vec2 shape) {
  float w = 0.4 + 0.1 * smoothstep(-0.5, 0.5, p.y);
  float e = length(p / vec2(w, 0.5));
  float nd = length(p - vec2(0.0, 0.52));
  shape = vec2(e, nd);
  return max((e - 1.0) * w, 0.1 - nd);
}

// Shade one petal. sp = petal-local coordinates, flip = cos of its turn about the long axis.
// Everything varies continuously with flip, so a turning petal never snaps between looks.
// Kept lean: it runs for every pixel a petal covers.
vec3 petalColor(vec2 sp, vec2 shape, float flip, float r, float L, float rim, vec2 sunL) {
  float edge = min(shape.x, 1.0);                                   // 0 centre .. 1 rim
  // Per-petal hue wander as a first-order rotation about the grey axis (no trig needed for +-0.15 rad).
  vec3 body = u_colors[2];
  vec3 tip = mix(body + ((r - 0.5) * 0.3) * cross(vec3(0.57735027), body), vec3(1.0), 0.26 + 0.14 * r);
  vec3 baseC = mix(u_colors[3], vec3(1.0), 0.84);
  // White at the base, pink toward the notched tip, like a real petal.
  vec3 col = mix(baseC, tip, smoothstep(-0.32, 0.5, sp.y) * (0.72 + 0.28 * r));
  // Paler, translucent edge.
  col = mix(col, baseC, smoothstep(0.78, 1.02, edge) * 0.3);
  // Fine veins fanning from the base.
  float fan = sp.x / max(sp.y + 0.62, 0.12);
  float vein = smoothstep(0.035, 0.0, abs(fan)) + 0.55 * smoothstep(0.035, 0.0, abs(abs(fan) - 0.45));
  col = mix(col, tip * 0.88, vein * 0.2 * smoothstep(0.6, -0.1, sp.y) * (1.0 - edge * 0.6));
  // Seen from behind a petal is slightly deeper: a gentle blend over a wide range of flip, never a switch.
  float face = smoothstep(-0.5, 0.5, flip);
  col = mix(mix(col, tip * 0.97, 0.22), col, face);
  // Satin sheen that slides along the petal as it tilts.
  float sx = (sp.y - 0.05 - 0.3 * flip) * 3.2;
  float sh = max(1.0 - sx * sx, 0.0) * (1.0 - edge * edge);
  col += vec3(1.0, 0.96, 0.94) * sh * 0.1 * abs(flip);
  // Backlit: the rim facing the sun glows warm, as thin petals do when light comes through them.
  float facing = max(dot(sp, sunL) * inversesqrt(dot(sp, sp) + 1e-6), 0.0);
  float lit = facing * facing * smoothstep(0.5, 1.0, edge) * rim;
  col += mix(vec3(1.0, 0.92, 0.82), baseC, 0.25) * lit * 0.3 * (0.55 + 0.45 * abs(flip));
  // A deeper blush where the notch splits the tip.
  col = mix(col, tip * 0.9, smoothstep(0.12, 0.0, shape.y) * 0.35);
  col += (1.0 - L) * body * 0.1 + baseC * rim * smoothstep(0.85, 1.02, edge) * (1.0 - L) * 0.12;
  return col;
}

void main() {
  vec2 fragCoord = gl_FragCoord.xy;
  vec2 uv = (fragCoord - 0.5 * u_resolution) / u_resolution.y;
  float t = u_time;
  float px = 1.4 / u_resolution.y;
  float L = u_light;

  // ---- sky: cool high up, warm and pink toward the horizon
  float g = smoothstep(-0.7, 0.7, uv.y + 0.2 * uv.x);
  vec3 coolTint = hueShiftSC(u_colors[2], 0.74571, -0.66628);
  vec3 skyDark = mix(u_colors[0] * 1.2, u_colors[1] * 1.05, g);
  skyDark = mix(skyDark, coolTint * 0.35, 0.25 * smoothstep(0.0, 0.8, uv.y));
  skyDark += u_colors[2] * 0.2 * smoothstep(0.9, 0.0, length(uv - vec2(0.5, 0.35)));
  vec3 skyLight = mix(pastel(u_colors[2], 0.72), pastel(hueShiftSC(u_colors[3], 0.29552, 0.95534), 0.62), g);
  skyLight = mix(skyLight, pastel(coolTint, 0.88), 0.38 * smoothstep(0.05, 0.75, uv.y));
  skyLight += vec3(1.0, 0.97, 0.9) * 0.09 * smoothstep(1.0, 0.0, length(uv - vec2(0.5, 0.35)));
  vec3 color = mix(skyDark, skyLight, L);

  // ---- out-of-focus cherry-tree crown, upper right: a soft cloud of blossoms
  {
    float crown = smoothstep(1.0, 0.1, length((uv - vec2(0.6, 0.38)) * vec2(0.8, 1.2)));
    if (crown > 0.004) {
    vec2 cq = uv * 3.4 + vec2(t * 0.01, 0.0);
    float n = fbm3(cq) + 0.35 * fbm3(cq * 2.3 + 5.0);
    float massA = smoothstep(0.55, 0.95, n * 0.9 + crown * 0.55);
    vec3 pinkMass = pastel(u_colors[2], 0.28);
    vec3 whiteMass = pastel(u_colors[3], 0.8);
    float tone = fbm3(uv * 8.0 + 3.0);
    vec3 mass = mix(pinkMass, whiteMass, smoothstep(0.35, 0.75, tone));
    vec3 massDark = mix(u_colors[2] * 0.62, u_colors[3] * 0.5, smoothstep(0.35, 0.8, tone));
    mass = mix(massDark, mass, L);
    color = mix(color, mass, massA * crown * mix(0.55, 0.75, L));
    // sunlit speckles inside the crown
    float inCrown = massA * crown;
    if (inCrown > 0.02) {
      float spk = smoothstep(0.62, 0.8, fbm3(uv * 22.0 + 11.0)) * inCrown;
      color += whiteMass * spk * mix(0.1, 0.12, L);
    }
    }
  }

  // Soft light shafts from the upper right.
  vec2 rp = uv - vec2(0.8, 0.65);
  float ang = atan(rp.y, rp.x);
  float shafts = pow(0.5 + 0.5 * sin(ang * 9.0 + sin(ang * 3.0 + t * 0.08) * 2.0 + t * 0.05), 3.0);
  shafts *= smoothstep(1.7, 0.0, length(rp)) * smoothstep(-2.7, -1.5, ang) * (1.0 - smoothstep(-0.7, -0.3, ang));
  color += mix(vec3(1.0, 0.85, 0.75), u_colors[3], 0.4) * shafts * mix(0.1, 0.035, L);

  // A warm sun glow behind the blossoms.
  color += vec3(1.0, 0.92, 0.82) * (0.16 * exp(-length(uv - vec2(0.62, 0.42)) * 2.3) + 0.1 * exp(-length(uv - vec2(0.62, 0.42)) * 7.0)) * mix(0.8, 0.45, L);

  // ---- out-of-focus bokeh discs drifting behind the petals
  {
    vec2 bq = uv * 2.8 + vec2(-t * 0.012, t * 0.008);
    vec2 bc = floor(bq);
    vec2 bf = fract(bq);
    float bh = hash21(bc + 71.0);
    if (bh > 0.5) {
      float br = 0.08 + 0.1 * fract(bh * 13.3);
      vec2 bp = bf - (0.3 + 0.4 * vec2(fract(bh * 37.1), fract(bh * 91.7)));
      float bl = length(bp);
      float disc = (1.0 - smoothstep(br - 0.03, br, bl)) * (0.6 + 0.4 * smoothstep(br - 0.08, br, bl));
      float breathe = 0.65 + 0.35 * sin(t * (0.3 + bh * 0.5) + bh * 40.0);
      float where = 0.4 + 0.6 * smoothstep(-0.4, 0.5, uv.x + 0.5 * uv.y);
      vec3 bokeh = mix(vec3(1.0, 0.95, 0.9), pastel(u_colors[2], 0.7), 0.35);
      color = mix(color, bokeh, disc * breathe * where * mix(0.24, 0.42, L));
    }
  }

  // ---- petals, far to near
  vec3 haloColor = mix(pastel(u_colors[2], 0.45), pastel(u_colors[3], 0.8), 0.5);
  vec3 bloomColor = pastel(u_colors[2], 0.5);
  vec2 wind = vec2(-0.34, 0.2);   // features drift to the lower right, the way petals fall
  for (int layer = 0; layer < 4; layer++) {
    float fl = float(layer);
    float scale = layer == 0 ? 6.0 : (layer == 1 ? 4.5 : (layer == 2 ? 2.5 : 1.25));
    float spd = 0.1 + 0.07 * fl;
    vec2 q = uv * scale;
    // Gusts: a slow travelling wave pushes the whole field so petals flock into streams.
    q.x += 0.55 * sin(uv.y * 1.8 + t * 0.22 + fl) * (1.0 + fl * 0.3);
    q.y += 0.3 * sin(uv.x * 1.3 - t * 0.17);
    q += wind * t * spd * scale * 0.9 + vec2(fl * 7.7, fl * 3.1);
    vec2 cell = floor(q);
    vec2 f = fract(q);
    // Far and ultra-near petals live inside one cell; the middle layers use the nearest neighbour per axis.
    bool single = layer == 0 || layer == 3;
    vec2 side = single ? vec2(0.0) : vec2(f.x < 0.5 ? -1.0 : 0.0, f.y < 0.5 ? -1.0 : 0.0);
    int span = single ? 1 : 2;
    float soft = layer == 3 ? 0.075 : (layer == 2 ? 0.03 : (layer == 0 ? 0.008 : 0.004));
    float alpha = layer == 0 ? 0.72 : (layer == 3 ? 0.7 : (layer == 2 ? 0.92 : 1.0));
    for (int j = 0; j < span; j++) {
      for (int i = 0; i < span; i++) {
        vec2 off = side + vec2(float(i), float(j));
        vec2 g2 = cell + off;
        float h = hash21(g2 + fl * 31.0);
        if (h < (layer == 3 ? 0.78 : 0.32)) continue;
        vec2 r2 = vec2(fract(h * 37.17), fract(h * 91.73));
        // Keep the petal (sway and blur included) inside what this 2x2 / 1x1 neighbourhood can see.
        vec2 pos = off + (single ? 0.38 + 0.24 * r2 : 0.25 + 0.5 * r2);
        float sz = (0.34 + 0.2 * fract(h * 13.31)) * (layer == 3 ? 0.9 : (layer == 2 ? 1.1 : (layer == 0 ? 0.58 : 1.0)));
        float swayMax = single ? 0.04 : 0.12;
        float reach = 0.5 * sz + (px + soft + 0.04) * scale;
        vec2 p = f - pos;
        // Cheap rejects first: most pixels are nowhere near a petal and must not pay for any trig.
        float wide = reach + swayMax;
        if (dot(p, p) > wide * wide) continue;
        p.x -= swayMax * sin(t * (0.5 + h) + h * 20.0);
        // The petal never reaches further than 0.5 * sz from its centre, whatever its turn.
        if (dot(p, p) > reach * reach) continue;
        float spin = t * (0.25 + 0.4 * h) * (h > 0.72 ? 1.0 : -1.0) + h * 9.0;
        float flip = cos(t * (0.55 + 0.5 * h) + h * 13.0);
        mat2 turn = rot(spin);
        vec2 sp = (turn * p) / sz;
        sp.x /= 0.55 + 0.45 * flip * flip;
        vec2 shape;
        float d = petalSDF(sp, shape) * sz / scale;
        if (d > px + soft + 0.06) continue;
        float cover = (1.0 - smoothstep(-px - soft, px + soft, d)) * alpha;
        // Soft glow at night and a faint rose bloom by day, both just outside the edge.
        float halo = exp(-max(d, 0.0) * 60.0) * alpha * (layer == 0 ? 0.0 : 1.0);
        color += haloColor * halo * 0.06 * (1.0 - L * 0.6);
        color *= 1.0 - 0.035 * (1.0 - L) * halo;
        color = mix(color, bloomColor, 0.07 * L * halo);
        if (cover > 0.002) {
          vec3 col = petalColor(sp, shape, flip, r2.x, L, layer == 3 ? 0.0 : 1.0, turn * vec2(0.55, 0.83));
          color = mix(color, col, cover);
        }
      }
    }
  }

  // ---- floating pollen / dust, soft glints, mist
  vec2 dg = uv * 16.0 + vec2(t * 0.15, t * 0.4);
  vec2 dc = floor(dg);
  float dh = hash21(dc + 9.0);
  vec2 dp = fract(dg) - (vec2(fract(dh * 31.7), fract(dh * 57.3)) * 0.6 + 0.2);
  float dust = step(0.9, dh) * (1.0 - smoothstep(0.0, 0.1, length(dp))) * (0.5 + 0.5 * sin(t * (0.8 + dh * 3.0) + dh * 40.0));
  color += vec3(1.0, 0.95, 0.85) * dust * mix(0.5, 0.35, L);

  color += mix(u_colors[3], vec3(1.0), 0.5) * 0.12 * smoothstep(-0.15, -0.55, uv.y) * (1.0 - 0.3 * L);
  vec2 sg = uv * 8.0 + vec2(2.0, 3.0);
  vec2 sc = floor(sg);
  float sr = hash21(sc + 50.0);
  float tw = 0.5 + 0.5 * sin(t * (1.0 + 1.5 * sr) + sr * 30.0);
  color = mix(color, vec3(1.0), step(0.9, sr) * sparkle(fract(sg) - (vec2(hash21(sc + 1.0), hash21(sc + 2.0)) * 0.6 + 0.2), 0.08) * tw * 0.85);
  outputColor = vec4(finishFlat(color, fragCoord), 1.0);
}
`;

  // ../echo/src/renderer/components/lyrics/shader-scenes/sceneShaders.ts
  var shaderSceneVertexSource = `#version 300 es
precision highp float;

void main() {
  vec2 position = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
  gl_Position = vec4(position * 2.0 - 1.0, 0.0, 1.0);
}
`;
  var prelude = `#version 300 es
precision highp float;

uniform vec2 u_resolution;
uniform float u_time;
uniform float u_flow;
uniform float u_light;
uniform vec3 u_colors[4];
uniform vec4 u_audio;
uniform vec4 u_beat;

out vec4 outputColor;

float hash21(vec2 point) {
  vec3 p3 = fract(vec3(point.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

float valueNoise(vec2 point) {
  vec2 cell = floor(point);
  vec2 local = fract(point);
  vec2 weight = local * local * (3.0 - 2.0 * local);
  float a = hash21(cell);
  float b = hash21(cell + vec2(1.0, 0.0));
  float c = hash21(cell + vec2(0.0, 1.0));
  float d = hash21(cell + vec2(1.0, 1.0));
  return mix(mix(a, b, weight.x), mix(c, d, weight.x), weight.y);
}

float fbm(vec2 point) {
  float value = 0.0;
  float amplitude = 0.5;
  const mat2 twist = mat2(1.6, 1.2, -1.2, 1.6);
  for (int octave = 0; octave < 4; octave++) {
    value += amplitude * valueNoise(point);
    point = twist * point;
    amplitude *= 0.5;
  }
  return value;
}

float fbm3(vec2 point) {
  float value = 0.0;
  float amplitude = 0.5;
  const mat2 twist = mat2(1.6, 1.2, -1.2, 1.6);
  for (int octave = 0; octave < 3; octave++) {
    value += amplitude * valueNoise(point);
    point = twist * point;
    amplitude *= 0.5;
  }
  return value;
}

// fbm with its analytic gradient (x = value, yz = d/dx, d/dy). Lighting a
// height field needs a normal; this gets it from one evaluation instead of three.
vec3 fbmDOctaves(vec2 point, int octaves) {
  float value = 0.0;
  float amplitude = 0.5;
  vec2 gradient = vec2(0.0);
  const mat2 twist = mat2(1.6, 1.2, -1.2, 1.6);
  mat2 chain = mat2(1.0);
  for (int octave = 0; octave < octaves; octave++) {
    vec2 cell = floor(point);
    vec2 local = fract(point);
    vec2 weight = local * local * (3.0 - 2.0 * local);
    vec2 slope = 6.0 * local * (1.0 - local);
    float a = hash21(cell);
    float b = hash21(cell + vec2(1.0, 0.0));
    float c = hash21(cell + vec2(0.0, 1.0));
    float d = hash21(cell + vec2(1.0, 1.0));
    float bottom = mix(a, b, weight.x);
    float top = mix(c, d, weight.x);
    value += amplitude * mix(bottom, top, weight.y);
    vec2 local_gradient = vec2(slope.x * mix(b - a, d - c, weight.y), slope.y * (top - bottom));
    gradient += amplitude * (local_gradient * chain);
    point = twist * point;
    chain = twist * chain;
    amplitude *= 0.5;
  }
  return vec3(value, gradient);
}

vec3 fbmD(vec2 point) {
  return fbmDOctaves(point, 4);
}

vec3 ramp(float t) {
  t = clamp(t, 0.0, 1.0);
  vec3 color = mix(u_colors[0], u_colors[1], smoothstep(0.0, 0.35, t));
  color = mix(color, u_colors[2], smoothstep(0.3, 0.7, t));
  return mix(color, u_colors[3], smoothstep(0.65, 1.0, t));
}

// Light theme re-inks the same structure: dark areas become pale paper tinted by
// the cover, bright areas become pigment. Dither hides 8-bit banding in soft fields.
vec3 finish(vec3 color, vec2 fragCoord) {
  if (u_light > 0.001) {
    float lum = dot(color, vec3(0.3333));
    float ink = smoothstep(0.04, 0.85, lum);
    vec3 paper = mix(u_colors[3], vec3(1.0), 0.8);
    vec3 pigment = mix(u_colors[0], u_colors[2], 0.45);
    color = mix(color, mix(paper, mix(pigment, color * 0.7, 0.3), ink), u_light);
  }
  vec2 offset = (fragCoord / u_resolution - 0.5) * vec2(1.0, 0.85);
  float edge = smoothstep(0.35, 0.95, length(offset) * 1.35);
  color *= 1.0 - mix(0.45, 0.1, u_light) * edge;
  color += (hash21(fragCoord + vec2(fract(u_time * 0.37) * 131.0, 7.0)) - 0.5) / 255.0 * 2.0;
  return clamp(color, 0.0, 1.0);
}
`;
  var liquidLight = `${prelude}
void main() {
  vec2 fragCoord = gl_FragCoord.xy;
  vec2 uv = (fragCoord - 0.5 * u_resolution) / u_resolution.y;
  float energy = u_audio.x;
  float bass = u_audio.y;
  float treble = u_audio.w;
  float beat = u_beat.x;
  float drive = bass * 0.55 + beat * 0.25;
  float t = u_time * 0.03 + u_flow * 0.05;
  vec2 p = uv * 1.5;
  vec2 q = vec2(fbm(p + vec2(0.0, t)), fbm(p + vec2(5.2, 1.3) - t));
  vec2 r = vec2(fbm(p + 2.0 * q + vec2(1.7, 9.2) + t * 1.3), fbm(p + 2.0 * q + vec2(8.3, 2.8) - t * 1.1));
  // Geometry never depends on audio: a kick must not shove the pattern around.
  vec2 warp = (q + r - 1.0) * 1.1;
  vec2 pw = p + 1.6 * warp + 0.3;
  vec3 surface = fbmD(pw);
  // A finer layer of ripples riding on the large folds: crisp glints without busier colour.
  vec3 ripples = fbmDOctaves(pw * 3.3 + vec2(t * 1.6, -t), 2);
  float h = surface.x;
  vec3 normal = normalize(vec3(-(surface.y + 0.2 * ripples.y), -(surface.z + 0.2 * ripples.z), 2.2));
  float swing = u_time * 0.15 + u_flow * 0.2;
  vec3 lightDir = normalize(vec3(cos(swing) * 0.6, 0.55 + 0.2 * sin(u_time * 0.11), 0.75));
  vec3 halfDir = normalize(lightDir + vec3(0.0, 0.0, 1.0));
  // Treble tightens the highlight so hats read as glints on the surface.
  float spec = pow(max(dot(normal, halfDir), 0.0), 70.0 - treble * 28.0);
  float diffuse = clamp(dot(normal, lightDir), 0.0, 1.0);
  float rim = pow(max(1.0 - normal.z, 0.0), 2.0);
  float k = smoothstep(0.15, 0.85, h);
  vec3 color = ramp(k * 0.76 + 0.02 + 0.16 * (q.x - 0.5));
  color *= 0.26 + 0.78 * diffuse;
  color += u_colors[3] * spec * (0.55 + drive * 1.1);
  // A broad satin sheen under the glints, and a rim light that hugs the folds.
  color += mix(u_colors[2], u_colors[3], 0.4) * pow(max(dot(normal, halfDir), 0.0), 11.0) * 0.16;
  color += u_colors[2] * rim * 1.3;
  color *= 1.0 + beat * 0.12 + energy * 0.04;
  color = color / (1.0 + 0.1 * color) * 1.06;
  outputColor = vec4(finish(color, fragCoord), 1.0);
}
`;
  var pulseRidge = `${prelude}
uniform vec3 u_hist[24];

const float maxHeight = 0.26;
// Row baselines, mix(0.60, 0.05, pow(row / 23, 1.3)), tabulated.
const float rowBase[24] = float[24](0.60000, 0.59066, 0.57701, 0.56106, 0.54340, 0.52436, 0.50412, 0.48285, 0.46064, 0.43758, 0.41374, 0.38917, 0.36392, 0.33804, 0.31154, 0.28447, 0.25686, 0.22872, 0.20008, 0.17096, 0.14138, 0.11135, 0.08088, 0.05000);

// Value noise with its analytic x-derivative: the ridge line needs a slope for a
// constant on-screen thickness, and this gets it without a second noise lookup.
vec2 valueNoiseD(vec2 point) {
  vec2 cell = floor(point);
  vec2 local = fract(point);
  vec2 weight = local * local * (3.0 - 2.0 * local);
  float a = hash21(cell);
  float b = hash21(cell + vec2(1.0, 0.0));
  float c = hash21(cell + vec2(0.0, 1.0));
  float d = hash21(cell + vec2(1.0, 1.0));
  float value = mix(mix(a, b, weight.x), mix(c, d, weight.x), weight.y);
  float slope = 6.0 * local.x * (1.0 - local.x) * mix(b - a, d - c, weight.y);
  return vec2(value, slope);
}

// x = shape, y = d shape / d u
vec2 rowShape(float u, float rowIndex) {
  vec2 coarse = valueNoiseD(vec2(u * 8.0 + rowIndex * 1.9, rowIndex * 0.37 + u_time * 0.12 + u_flow * 0.1));
  vec2 fine = valueNoiseD(vec2(u * 21.0 + rowIndex * 3.3, u_time * 0.35 + rowIndex));
  float n = max(coarse.x * 0.65 + fine.x * 0.35, 0.0001);
  float dn = coarse.y * 0.65 * 8.0 + fine.y * 0.35 * 21.0;
  float power = pow(n, 1.4);
  return vec2(power * n * 2.6, 2.4 * power * 2.6 * dn);
}

void main() {
  vec2 fragCoord = gl_FragCoord.xy;
  vec2 uv = fragCoord / u_resolution;
  float aspect = u_resolution.x / u_resolution.y;
  float px = 1.0 / u_resolution.y;
  float pixelScale = max(1.0, u_resolution.y / 720.0);
  float bass = u_audio.y;
  float beat = u_beat.x;
  vec3 background = mix(u_colors[0] * 0.45, u_colors[0] * 0.95, uv.y);
  background += u_colors[1] * 0.55 * exp(-length((uv - vec2(0.5, 0.62)) * vec2(aspect * 0.55, 1.0)) * 3.2) * (0.55 + 0.6 * bass);
  // Haze on the horizon and faint shafts of light rising off the ridges.
  float horizon = exp(-abs(uv.y - 0.6) * 9.0) * exp(-pow(abs(uv.x - 0.5) * 1.5, 2.0) * 2.2);
  background += mix(u_colors[1], u_colors[2], 0.3) * horizon * 0.16;
  float shafts = valueNoise(vec2((uv.x - 0.5) * aspect * 26.0, u_time * 0.05 + u_flow * 0.03)) * 0.6
    + valueNoise(vec2((uv.x - 0.5) * aspect * 9.0 + 4.0, u_time * 0.03)) * 0.4;
  background += u_colors[1] * pow(shafts, 2.2) * smoothstep(0.2, 0.62, uv.y) * exp(-(uv.y - 0.6) * 2.4) * exp(-pow((uv.x - 0.5) * 1.6, 2.0) * 2.0) * 0.34;
  vec3 color = background;
  float x = (uv.x - 0.5) * aspect;
  // Rows are ordered back to front with falling baselines. Every row whose
  // baseline lies above this pixel paints background over it, so they all
  // collapse into the background colour and the loop starts after the last of
  // them; rows whose lowest possible peak is still below the pixel end it.
  float reachY = uv.y + 6.0 * px;
  int firstRow = 0;
  if (reachY < rowBase[0]) {
    float fraction = pow(clamp((0.60 - reachY) / 0.55, 0.0, 1.0), 0.76923);
    firstRow = clamp(int(ceil(fraction * 23.0)), 0, 24);
    if (firstRow < 24 && rowBase[firstRow] > reachY) { firstRow++; }
    if (firstRow > 0 && rowBase[firstRow - 1] <= reachY) { firstRow--; }
  }
  for (int row = firstRow; row < 24; row++) {
    float rowIndex = float(row);
    float k = rowIndex / 23.0;
    float baseY = rowBase[row];
    if (uv.y > baseY + maxHeight + 4.0 * px) {
      break;
    }
    float halfWidth = 0.5 * aspect * mix(0.55, 1.02, k);
    float u = x / halfWidth;
    float inside = step(abs(u), 1.0);
    vec3 band = u_hist[row];
    float edgeDistance = abs(u);
    float centerBand = u * 2.2;
    float shoulderBand = (edgeDistance - 0.5) * 3.2;
    float edgeBand = (edgeDistance - 0.88) * 4.5;
    float shape = band.x * exp(-centerBand * centerBand)
      + band.y * 0.9 * exp(-shoulderBand * shoulderBand)
      + band.z * 0.8 * exp(-edgeBand * edgeBand);
    float amplitude = (shape * 0.85 + 0.06) * mix(0.07, 0.22, k);
    vec2 shapeAndSlope = rowShape(u, rowIndex);
    float rawHeight = shapeAndSlope.x * amplitude;
    float height = min(rawHeight, maxHeight);
    float lineY = baseY + height;
    float slope = rawHeight < maxHeight ? shapeAndSlope.y * amplitude / halfWidth : 0.0;
    float distanceToLine = abs(uv.y - lineY) / sqrt(1.0 + slope * slope);
    float fill = (1.0 - smoothstep(-1.2 * px, 1.2 * px, uv.y - lineY)) * inside;
    color = mix(color, background, fill);
    // Line and glow are invisible beyond ~30 px, so most pixels skip them.
    if (distanceToLine > 56.0 * px * pixelScale) {
      continue;
    }
    float lineWidth = mix(0.9, 1.5, k) * px * pixelScale;
    float line = exp(-distanceToLine * distanceToLine / (lineWidth * lineWidth)) * inside;
    float glow = (exp(-distanceToLine / (7.0 * px * pixelScale)) * 0.06 + exp(-distanceToLine / (22.0 * px * pixelScale)) * 0.04) * inside;
    float fade = 1.0 - smoothstep(0.82, 1.0, edgeDistance);
    float level = (band.x + band.y + band.z) * 0.3333;
    vec3 lineColor = mix(u_colors[2], u_colors[3], clamp(k * 0.5 + height * 5.0, 0.0, 1.0));
    color += lineColor * (line * (0.45 + 1.1 * k) * (0.75 + level + beat * 0.3 * k) + glow * (0.5 + level)) * fade;
  }
  color = 1.0 - exp(-color * 1.2);
  outputColor = vec4(finish(color, fragCoord), 1.0);
}
`;
  var halftone = `${prelude}
// A silk sheet lit from the side, printed the way a three-plate screen press
// would print it: body ink where the cloth catches light, shade ink in the folds
// that fall away, and a fine highlight plate on the crests. Each plate has its own
// dot pitch and angle, so where they overlap they weave small rosettes. Dot size
// follows tone (threshold screen), so dots swell and shrink as the cloth moves.
// x = ink coverage, y = how deep inside the dot this pixel sits (0 at the rim,
// 1 at the centre), used to give each dot a soft bead of light. Dots are round in
// the light tones and square up as they swell and touch, the way a real screen
// chains; each cell takes a touch more or less ink, like uneven paper.
vec2 screenDots(vec2 fragCoord, float angle, float cells, float tone, float seed) {
  float c = cos(angle);
  float s = sin(angle);
  vec2 grid = mat2(c, -s, s, c) * ((fragCoord - 0.5 * u_resolution) / u_resolution.y) * cells;
  vec2 local = fract(grid) - 0.5;
  float ink = clamp(tone * (0.93 + 0.14 * hash21(floor(grid) + seed)), 0.0, 1.0);
  vec2 reach = abs(local);
  // Round to square: blend the circular distance toward the chessboard distance.
  float distanceToCenter = mix(length(local), max(reach.x, reach.y), 0.55 * smoothstep(0.4, 0.95, ink));
  float radius = 0.5 * sqrt(ink) * 1.12;
  float edge = max(fwidth(distanceToCenter), 0.0001) * 0.85;
  float coverage = 1.0 - smoothstep(radius - edge, radius + edge, distanceToCenter);
  float depth = clamp(1.0 - distanceToCenter / max(radius, 0.0001), 0.0, 1.0);
  return vec2(coverage, depth);
}

// The cloth: crossing sets of broad folds, bent by a strong slow warp so they curl
// and swell like silk instead of running in parallel stripes. Returns the height in
// x and its slope in yz; the slope is exact (chain rule through the warp), which
// keeps the folds smooth and costs two noise evaluations.
vec3 clothHeight(vec2 p, float t) {
  vec3 warpX = fbmDOctaves(p * 0.7 + vec2(t, 0.0), 3);
  vec3 warpY = fbmDOctaves(p * 0.7 + vec2(5.2, 1.3) - t, 3);
  vec2 warp = vec2(warpX.x, warpY.x) - 0.44;
  vec2 gradX = warpX.yz * 0.7;
  vec2 gradY = warpY.yz * 0.7;
  vec2 q = p + 1.7 * warp;
  vec2 dirA = vec2(0.62, 1.05);
  vec2 dirB = vec2(-0.9, 0.45);
  vec2 slopeQx = vec2(1.0, 0.0) + 1.7 * gradX;
  vec2 slopeQy = vec2(0.0, 1.0) + 1.7 * gradY;
  float phaseA = dot(q, dirA) * 4.6 + warp.x * 3.0 - t * 6.0;
  float phaseB = dot(q, dirB) * 2.8 + warp.y * 2.0 + t * 3.4;
  vec2 slopeA = 4.6 * (dirA.x * slopeQx + dirA.y * slopeQy) + 3.0 * gradX;
  vec2 slopeB = 2.8 * (dirB.x * slopeQx + dirB.y * slopeQy) + 2.0 * gradY;
  float height = 0.5 + 0.5 * (0.64 * sin(phaseA) + 0.36 * sin(phaseB));
  vec2 slope = 0.5 * (0.64 * cos(phaseA) * slopeA + 0.36 * cos(phaseB) * slopeB);
  return vec3(height, slope);
}

void main() {
  vec2 fragCoord = gl_FragCoord.xy;
  vec2 uv = (fragCoord - 0.5 * u_resolution) / u_resolution.y;
  float t = u_time * 0.028 + u_flow * 0.04;

  // Lit from a slowly circling side light.
  vec2 p = uv * 0.8;
  vec3 cloth = clothHeight(p, t);
  float height = cloth.x;
  vec3 normal = normalize(vec3(-cloth.yz * 0.42, 1.0));
  float swing = u_time * 0.07 + u_flow * 0.1;
  vec3 lightDir = normalize(vec3(cos(swing) * 0.7, 0.5 + 0.2 * sin(swing * 0.7), 0.65));
  float diffuse = clamp(dot(normal, lightDir) * 0.5 + 0.5, 0.0, 1.0);
  float spec = pow(max(dot(normal, normalize(lightDir + vec3(0.0, 0.0, 1.0))), 0.0), 36.0);
  float rim = pow(clamp(1.0 - normal.z, 0.0, 1.0), 1.5);

  float lit = smoothstep(0.56, 1.0, diffuse) * (0.25 + 0.95 * smoothstep(0.3, 0.8, height));
  float shaded = smoothstep(0.52, 0.18, diffuse) * smoothstep(0.38, 0.72, height) * 0.8;
  float crest = clamp(spec * 1.5 + rim * 0.9, 0.0, 1.0);

  // Plates sit a hair off register, as on a real press.
  float registration = max(1.0, u_resolution.y / 720.0);
  vec2 body = screenDots(fragCoord + vec2(0.9, 0.5) * registration, 0.2618, 46.0, lit * 0.88, 3.0);
  vec2 shade = screenDots(fragCoord - vec2(1.1, 0.7) * registration, 1.309, 29.0, shaded * 0.95, 17.0);
  vec2 glint = screenDots(fragCoord + vec2(-0.6, 1.0) * registration, 0.7854, 74.0, crest, 41.0);

  // Faint ink-wash under the plates so the dots sit on light, not on flat black.
  vec3 wash = ramp(0.2 + 0.4 * diffuse * (0.4 + height)) * 0.2 * (0.35 + lit);
  vec3 color = u_colors[0] * 0.75 + wash;
  // Each dot is a little brighter at its heart than at its rim.
  vec3 shadeInk = u_colors[1] * (0.82 + 0.34 * shade.y) * shade.x * 0.9;
  vec3 bodyInk = mix(u_colors[2], u_colors[3], 0.18 + 0.1 * body.y) * (0.84 + 0.3 * body.y) * body.x * 0.92;
  color = 1.0 - (1.0 - color) * (1.0 - shadeInk);
  color = 1.0 - (1.0 - color) * (1.0 - bodyInk);
  color = 1.0 - (1.0 - color) * (1.0 - u_colors[3] * glint.x);
  // Lit cloth glows softly between the dots.
  color += mix(u_colors[2], u_colors[3], 0.5) * smoothstep(0.5, 1.0, lit) * 0.09;
  outputColor = vec4(finish(color, fragCoord), 1.0);
}
`;
  var contour = `${prelude}
float field(vec2 p) {
  float t = u_time * 0.03 + u_flow * 0.035;
  vec2 q = vec2(fbm(p * 0.8 + vec2(t, 0.0)), fbm(p * 0.8 + vec2(5.2, 1.3) - t));
  return fbm(p * 0.9 + 1.7 * q + vec2(0.0, t * 0.6));
}

void main() {
  vec2 fragCoord = gl_FragCoord.xy;
  vec2 uv = (fragCoord - 0.5 * u_resolution) / u_resolution.y;
  float energy = u_audio.x;
  float bass = u_audio.y;
  float treble = u_audio.w;
  float beat = u_beat.x;
  float f = field(uv * 2.0);
  float gradient = length(vec2(dFdx(f), dFdy(f))) + 1e-5;
  float lines = 16.0;
  float s = f * lines - (u_time * 0.1 + u_flow * 0.08);
  // Distance to the nearest iso-line in 720p-equivalent pixels, so the look holds at any buffer size.
  float pixels = abs(fract(s - 0.5) - 0.5) / (lines * gradient) * (720.0 / u_resolution.y);
  float major = 1.0 - step(0.5, mod(floor(s + 0.5), 5.0));
  float line = exp(-pixels * pixels / 2.6);
  float glow = exp(-pixels / 8.0) * 0.17;
  vec3 color = u_colors[0] * 0.7 + u_colors[1] * 0.3 * f;
  // Relief under the lines: terraces that alternate between contours, and a side
  // light that catches the slopes so the map reads as land rather than as a pattern.
  float terrace = 0.5 + 0.5 * cos(6.2831853 * s);
  float steepness = clamp(lines * gradient * 6.0, 0.0, 1.0);
  vec2 downhill = vec2(dFdx(f), dFdy(f)) / gradient;
  float hillshade = dot(downhill, normalize(vec2(-0.55, 0.83))) * steepness;
  color += mix(u_colors[1], u_colors[2], 0.35) * (0.1 * terrace * (0.4 + f) + 0.26 * max(hillshade, 0.0));
  color -= u_colors[0] * 0.3 * max(-hillshade, 0.0);
  vec3 lineColor = ramp(f * 1.7 - 0.2);
  color += lineColor * (line * (0.7 + 1.1 * major) + glow) * (0.85 + 0.15 * energy);
  color = 1.0 - exp(-color * 1.3);
  outputColor = vec4(finish(color, fragCoord), 1.0);
}
`;
  var shaderSceneFragmentSources = {
    liquidLight,
    pulseRidge,
    halftone,
    contour,
    sakura: buildSakuraSceneSource(prelude),
    bfs: buildBfsSceneSource(prelude),
    coverLiquid: buildCoverSceneSource(prelude, "coverLiquid"),
    coverPrism: buildCoverSceneSource(prelude, "coverPrism"),
    coverFrost: buildCoverSceneSource(prelude, "coverFrost"),
    coverPond: buildCoverSceneSource(prelude, "coverPond"),
    coverHolo: buildCoverSceneSource(prelude, "coverHolo"),
    coverVinyl: buildCoverSceneSource(prelude, "coverVinyl"),
    coverNeon: buildCoverSceneSource(prelude, "coverNeon")
  };

  // ../echo/src/renderer/components/lyrics/shader-scenes/shaderSceneAmbience.ts
  var band = (seconds, rateA, rateB, phase) => 0.5 + 0.5 * (0.6 * Math.sin(seconds * rateA) + 0.4 * Math.sin(seconds * rateB + phase));
  var sampleShaderSceneAmbience = (seconds) => ({
    bass: 0.12 + 0.36 * band(seconds, 0.9, 0.37, 1.3),
    mid: 0.08 + 0.26 * band(seconds, 1.3, 0.53, 4.1),
    treble: 0.04 + 0.16 * band(seconds, 2.1, 0.71, 2.2)
  });
  var shaderSceneAmbientFlowRate = 0.6;
  var shaderSceneAmbientEnergy = 0.5;
  var shaderSceneAmbientActivity = 0.2;
  var shaderSceneAmbientRowSpacingSeconds = 0.255;
  var fillShaderSceneAmbientHistory = (values, seconds) => {
    const rows = Math.floor(values.length / 3);
    for (let row = 0; row < rows; row += 1) {
      const sample = sampleShaderSceneAmbience(seconds - (rows - 1 - row) * shaderSceneAmbientRowSpacingSeconds);
      values[row * 3] = sample.bass;
      values[row * 3 + 1] = sample.mid;
      values[row * 3 + 2] = sample.treble;
    }
  };

  // ../echo/src/renderer/components/lyrics/shader-scenes/shaderSceneRenderer.ts
  var shaderScenePixelBudgets = {
    smooth: { liquidLight: 1024 * 576, contour: 1280 * 720, pulseRidge: 1280 * 720, halftone: 1280 * 720, sakura: 960 * 540, bfs: 960 * 540, coverLiquid: 1024 * 576, coverPrism: 1024 * 576, coverFrost: 1024 * 576, coverPond: 1024 * 576, coverHolo: 1024 * 576, coverVinyl: 1024 * 576, coverNeon: 1024 * 576 },
    balanced: { liquidLight: 1280 * 720, contour: 1600 * 900, pulseRidge: 1600 * 900, halftone: 1600 * 900, sakura: 1280 * 720, bfs: 1152 * 648, coverLiquid: 1280 * 720, coverPrism: 1280 * 720, coverFrost: 1280 * 720, coverPond: 1280 * 720, coverHolo: 1280 * 720, coverVinyl: 1280 * 720, coverNeon: 1280 * 720 },
    sharp: { liquidLight: 1600 * 900, contour: 1920 * 1080, pulseRidge: 1920 * 1080, halftone: 1920 * 1080, sakura: 1600 * 900, bfs: 1600 * 900, coverLiquid: 1600 * 900, coverPrism: 1600 * 900, coverFrost: 1600 * 900, coverPond: 1600 * 900, coverHolo: 1600 * 900, coverVinyl: 1600 * 900, coverNeon: 1600 * 900 }
  };
  var shaderSceneQualityScales = [1, 0.82, 0.68];
  var resolveShaderSceneCanvasSize = (mode2, cssWidth, cssHeight, devicePixelRatio, qualityScale = 1, quality = "balanced") => {
    const safeWidth = Math.max(1, cssWidth);
    const safeHeight = Math.max(1, cssHeight);
    const safePixelRatio = Math.max(0.5, Number.isFinite(devicePixelRatio) ? devicePixelRatio : 1);
    const effectiveQuality = isLyricsCoverShaderSceneMode(mode2) ? defaultLyricsShaderSceneQuality : quality;
    const scale = Math.min(safePixelRatio, Math.sqrt(shaderScenePixelBudgets[effectiveQuality][mode2] / (safeWidth * safeHeight))) * Math.min(1, Math.max(0.25, qualityScale));
    return { width: Math.max(1, Math.floor(safeWidth * scale)), height: Math.max(1, Math.floor(safeHeight * scale)) };
  };
  var createShaderSceneColors = (palette) => {
    const out = new Float32Array(12);
    for (let index = 0; index < 12; index += 1) {
      const value = palette.ramp[index];
      out[index] = Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0;
    }
    return out;
  };
  var shaderSceneHistoryRows = 24;
  var historyIntervalMs = 85;
  var createShaderSceneHistory = () => {
    const values = new Float32Array(shaderSceneHistoryRows * 3);
    let carryMs = 0;
    return {
      values,
      advance: (elapsedMs, bass, mid, treble) => {
        carryMs += Math.max(0, Math.min(250, elapsedMs));
        while (carryMs >= historyIntervalMs) {
          carryMs -= historyIntervalMs;
          values.copyWithin(0, 3);
          const last = values.length - 3;
          values[last] = bass;
          values[last + 1] = mid;
          values[last + 2] = treble;
        }
      }
    };
  };
  var compileShader = (gl, type, source) => {
    const shader = gl.createShader(type);
    if (!shader) {
      throw new Error("Unable to allocate shader.");
    }
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      gl.deleteShader(shader);
      throw new Error("Unable to compile lyrics scene shader.");
    }
    return shader;
  };
  var createShaderSceneRenderer = (canvas2, initialMode, initialPalette) => {
    const gl = canvas2.getContext("webgl2", {
      alpha: false,
      antialias: false,
      depth: false,
      failIfMajorPerformanceCaveat: true,
      powerPreference: "low-power",
      premultipliedAlpha: false,
      preserveDrawingBuffer: false,
      stencil: false
    });
    if (!gl) {
      return null;
    }
    let vertexShader = null;
    let vertexArray = null;
    let current = null;
    let mode2 = initialMode;
    let disposed = false;
    const deleteScene = (scene) => {
      if (!scene) return;
      gl.deleteProgram(scene.program);
      gl.deleteShader(scene.fragmentShader);
    };
    const buildScene = (nextMode) => {
      if (!vertexShader) {
        throw new Error("Missing vertex shader.");
      }
      const fragmentShader = compileShader(gl, gl.FRAGMENT_SHADER, shaderSceneFragmentSources[nextMode]);
      const program = gl.createProgram();
      if (!program) {
        gl.deleteShader(fragmentShader);
        throw new Error("Unable to allocate lyrics scene program.");
      }
      gl.attachShader(program, vertexShader);
      gl.attachShader(program, fragmentShader);
      gl.linkProgram(program);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        gl.deleteProgram(program);
        gl.deleteShader(fragmentShader);
        throw new Error("Unable to link lyrics scene program.");
      }
      const locate = (name) => gl.getUniformLocation(program, name);
      return {
        program,
        fragmentShader,
        resolution: locate("u_resolution"),
        time: locate("u_time"),
        flow: locate("u_flow"),
        light: locate("u_light"),
        colors: locate("u_colors[0]"),
        audio: locate("u_audio"),
        beat: locate("u_beat"),
        history: locate("u_hist[0]"),
        cover: isLyricsCoverShaderSceneMode(nextMode),
        pointer: locate("u_pointer"),
        ripples: locate("u_rip[0]"),
        trail: locate("u_trail[0]"),
        coverA: locate("u_coverA"),
        coverB: locate("u_coverB"),
        coverMix: locate("u_coverMix"),
        hasCover: locate("u_hasCover")
      };
    };
    try {
      vertexShader = compileShader(gl, gl.VERTEX_SHADER, shaderSceneVertexSource);
      vertexArray = gl.createVertexArray();
      if (!vertexArray) {
        throw new Error("Unable to allocate lyrics scene vertex array.");
      }
      current = buildScene(mode2);
      const currentColors = createShaderSceneColors(initialPalette);
      const targetColors = createShaderSceneColors(initialPalette);
      let light2 = initialPalette.lightTheme ? 1 : 0;
      let targetLight = light2;
      let sceneTimeSeconds = 0;
      let cssWidth = 0;
      let cssHeight = 0;
      let pixelRatio = 1;
      let qualityLevel = 0;
      let qualityTier = "balanced";
      let targetFrameIntervalMs = null;
      let previousTimestamp = null;
      let lateness = 1;
      let framesSinceAdjust = 0;
      let displayPeriodMs = 1e3 / 60;
      const advanceEnvelope = createFlowingLightEnvelope();
      const history = createShaderSceneHistory();
      const interaction = createCoverSceneInteraction();
      const coverTextures = [null, null];
      let frontCover = 0;
      let hasCover = false;
      let coverMix = 1;
      let currentCoverKey = null;
      const releaseCovers = () => {
        for (let index = 0; index < coverTextures.length; index += 1) {
          if (coverTextures[index]) gl.deleteTexture(coverTextures[index]);
          coverTextures[index] = null;
        }
        hasCover = false;
        coverMix = 1;
        currentCoverKey = null;
      };
      gl.bindVertexArray(vertexArray);
      gl.disable(gl.BLEND);
      gl.disable(gl.DEPTH_TEST);
      const applySize = () => {
        if (cssWidth <= 0 || cssHeight <= 0) return;
        const size = resolveShaderSceneCanvasSize(
          mode2,
          cssWidth,
          cssHeight,
          pixelRatio,
          shaderSceneQualityScales[qualityLevel],
          qualityTier
        );
        if (canvas2.width !== size.width || canvas2.height !== size.height) {
          canvas2.width = size.width;
          canvas2.height = size.height;
          gl.viewport(0, 0, size.width, size.height);
        }
      };
      const renderer2 = {
        draw: (timestamp, elapsedMs, drive) => {
          if (disposed || gl.isContextLost() || !current) {
            return;
          }
          if (targetFrameIntervalMs !== null && previousTimestamp !== null) {
            const gap = timestamp - previousTimestamp;
            if (gap > 0 && gap < 1e3) {
              displayPeriodMs = Math.max(2, Math.min(displayPeriodMs, gap));
              const expectedGapMs = Math.max(targetFrameIntervalMs, displayPeriodMs);
              lateness += (Math.min(4, gap / expectedGapMs) - lateness) * 0.08;
              framesSinceAdjust += 1;
              const lateThreshold = current.cover ? 1.35 : 1.9;
              const sampleFrames = current.cover ? 30 : 45;
              if (lateness > lateThreshold && framesSinceAdjust > sampleFrames && qualityLevel < shaderSceneQualityScales.length - 1) {
                qualityLevel += 1;
                framesSinceAdjust = 0;
                lateness = 1;
                applySize();
              }
            }
          }
          previousTimestamp = timestamp;
          sceneTimeSeconds += Math.max(0, Math.min(250, elapsedMs)) / 1e3;
          const weight = 1 - Math.exp(-Math.max(0, elapsedMs) / 300);
          for (let index = 0; index < currentColors.length; index += 1) {
            currentColors[index] += (targetColors[index] - currentColors[index]) * weight;
          }
          light2 += (targetLight - light2) * weight;
          gl.useProgram(current.program);
          gl.bindVertexArray(vertexArray);
          gl.uniform2f(current.resolution, canvas2.width, canvas2.height);
          gl.uniform1f(current.time, sceneTimeSeconds);
          gl.uniform1f(current.flow, drive ? drive.flow : sceneTimeSeconds * shaderSceneAmbientFlowRate);
          gl.uniform1f(current.light, light2);
          gl.uniform3fv(current.colors, currentColors);
          if (!current.cover) {
            const visible = drive ? advanceEnvelope(drive, elapsedMs) : (() => {
              const ambient = sampleShaderSceneAmbience(sceneTimeSeconds);
              return {
                energy: shaderSceneAmbientEnergy,
                bass: ambient.bass,
                mid: ambient.mid,
                treble: ambient.treble,
                beat: 0,
                pulse: 0,
                activity: shaderSceneAmbientActivity
              };
            })();
            gl.uniform4f(current.audio, visible.energy, visible.bass, visible.mid, visible.treble);
            gl.uniform4f(current.beat, visible.beat, visible.pulse, visible.activity, 0);
            if (current.history) {
              if (drive) {
                history.advance(elapsedMs, visible.bass, visible.mid, visible.treble);
              } else {
                fillShaderSceneAmbientHistory(history.values, sceneTimeSeconds);
              }
              gl.uniform3fv(current.history, history.values);
            }
          }
          if (current.cover) {
            const seconds = Math.max(0, Math.min(250, elapsedMs)) / 1e3;
            interaction.step(seconds, sceneTimeSeconds);
            if (coverMix < 1) {
              coverMix = Math.min(1, coverMix + seconds / 0.7);
            }
            const front = coverTextures[frontCover];
            const back = coverMix < 1 ? coverTextures[frontCover ^ 1] : front;
            const textured = hasCover && front !== null && back !== null;
            gl.uniform2f(current.pointer, interaction.pointer[0], interaction.pointer[1]);
            gl.uniform3fv(current.ripples, interaction.ripples);
            gl.uniform3fv(current.trail, interaction.trail);
            gl.uniform1f(current.hasCover, textured ? 1 : 0);
            gl.uniform1f(current.coverMix, coverMix);
            gl.uniform1i(current.coverA, 0);
            gl.uniform1i(current.coverB, 1);
            if (textured) {
              gl.activeTexture(gl.TEXTURE1);
              gl.bindTexture(gl.TEXTURE_2D, back);
              gl.activeTexture(gl.TEXTURE0);
              gl.bindTexture(gl.TEXTURE_2D, front);
            }
          }
          gl.drawArrays(gl.TRIANGLES, 0, 3);
        },
        resize: (nextCssWidth, nextCssHeight, devicePixelRatio) => {
          if (disposed || gl.isContextLost()) {
            return;
          }
          cssWidth = nextCssWidth;
          cssHeight = nextCssHeight;
          pixelRatio = devicePixelRatio;
          applySize();
        },
        setQuality: (quality) => {
          if (quality === qualityTier) {
            return;
          }
          qualityTier = quality;
          if (current?.cover) return;
          qualityLevel = 0;
          lateness = 1;
          framesSinceAdjust = 0;
          applySize();
        },
        setTargetFrameInterval: (ms) => {
          displayPeriodMs = 1e3 / 60;
          targetFrameIntervalMs = ms !== null && Number.isFinite(ms) && ms > 0 ? ms : null;
          previousTimestamp = null;
          lateness = 1;
          framesSinceAdjust = 0;
        },
        setPalette: (palette, immediate = false) => {
          targetColors.set(createShaderSceneColors(palette));
          targetLight = palette.lightTheme ? 1 : 0;
          if (immediate) {
            currentColors.set(targetColors);
            light2 = targetLight;
          }
        },
        setScene: (nextMode) => {
          if (disposed || gl.isContextLost()) {
            return false;
          }
          if (nextMode === mode2 && current) {
            return true;
          }
          try {
            const next = buildScene(nextMode);
            deleteScene(current);
            current = next;
            mode2 = nextMode;
            if (!current.cover) releaseCovers();
            qualityLevel = 0;
            lateness = 1;
            framesSinceAdjust = 0;
            previousTimestamp = null;
            applySize();
            return true;
          } catch {
            return false;
          }
        },
        setCover: (source, key = null) => {
          if (disposed || gl.isContextLost()) {
            return false;
          }
          if (!source) {
            releaseCovers();
            return true;
          }
          if (hasCover && key !== null && key === currentCoverKey) return true;
          try {
            const target = hasCover ? frontCover ^ 1 : frontCover;
            let texture = coverTextures[target];
            if (!texture) {
              texture = gl.createTexture();
              if (!texture) {
                throw new Error("Unable to allocate cover texture.");
              }
              gl.activeTexture(gl.TEXTURE0);
              gl.bindTexture(gl.TEXTURE_2D, texture);
              gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
              gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
              gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
              gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
              coverTextures[target] = texture;
            }
            gl.activeTexture(gl.TEXTURE0);
            gl.bindTexture(gl.TEXTURE_2D, texture);
            gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, true);
            try {
              gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, source);
              gl.generateMipmap(gl.TEXTURE_2D);
            } finally {
              gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, false);
            }
            coverMix = hasCover ? 0 : 1;
            frontCover = target;
            hasCover = true;
            currentCoverKey = key;
            return true;
          } catch {
            releaseCovers();
            return false;
          }
        },
        coverKey: () => currentCoverKey,
        pointerMove: (x, y) => interaction.move(x, y),
        pointerLeave: () => interaction.leave(),
        pointerPress: (x, y) => interaction.press(x, y),
        isContextLost: () => disposed || gl.isContextLost(),
        park: () => {
          if (disposed || gl.isContextLost()) {
            return;
          }
          qualityLevel = 0;
          lateness = 1;
          framesSinceAdjust = 0;
          previousTimestamp = null;
          if (canvas2.width !== 1 || canvas2.height !== 1) {
            canvas2.width = 1;
            canvas2.height = 1;
            gl.viewport(0, 0, 1, 1);
          }
          gl.clearColor(0, 0, 0, 1);
          gl.clear(gl.COLOR_BUFFER_BIT);
          gl.flush();
        },
        dispose: () => {
          if (disposed) {
            return;
          }
          disposed = true;
          gl.bindVertexArray(null);
          gl.useProgram(null);
          deleteScene(current);
          current = null;
          releaseCovers();
          if (vertexArray) gl.deleteVertexArray(vertexArray);
          if (vertexShader) gl.deleteShader(vertexShader);
          gl.getExtension("WEBGL_lose_context")?.loseContext();
        }
      };
      return renderer2;
    } catch {
      deleteScene(current);
      if (vertexArray) gl.deleteVertexArray(vertexArray);
      if (vertexShader) gl.deleteShader(vertexShader);
      gl.getExtension("WEBGL_lose_context")?.loseContext();
      return null;
    }
  };
  var sharedSurface = null;
  var releaseTimer = null;
  var cancelScheduledSurfaceRelease = () => {
    if (releaseTimer !== null) {
      clearTimeout(releaseTimer);
      releaseTimer = null;
    }
  };
  var discardShaderSceneSurface = () => {
    cancelScheduledSurfaceRelease();
    if (!sharedSurface) {
      return;
    }
    const surface = sharedSurface;
    sharedSurface = null;
    surface.renderer.dispose();
    surface.canvas.remove();
  };
  registerLyricsResourceOwner({
    id: "shader-scene-surface",
    releaseOnPressure: true,
    snapshot: () => ({
      kind: "webgl",
      state: !sharedSurface ? "empty" : sharedSurface.canvas.parentElement ? "active" : "parked",
      backingBytes: sharedSurface ? sharedSurface.canvas.width * sharedSurface.canvas.height * 4 : 0
    }),
    releaseUnused: discardShaderSceneSurface
  });

  // ../echo/src/renderer/components/lyrics/shader-scenes/shaderScenePalettes.ts
  var shaderScenePalettePresets = {
    sakura: ["#1b0e1d", "#5b2a63", "#e4577f", "#ffd6c8"],
    ocean: ["#04101c", "#0c3b5e", "#2192b8", "#b4f2ff"],
    amber: ["#150a05", "#5e2610", "#ec8630", "#ffe6b0"],
    mint: ["#06130f", "#0e4b3d", "#38c79c", "#dbffeb"],
    violet: ["#0b0719", "#3a1f8c", "#8e5dff", "#e9dcff"],
    mono: ["#0b0c10", "#3a3d47", "#b8bcc8", "#ffffff"]
  };
  var clamp012 = (value) => Math.min(1, Math.max(0, value));
  var hexToRgb = (hex) => {
    const value = Number.parseInt(hex.slice(1), 16);
    return [(value >> 16 & 255) / 255, (value >> 8 & 255) / 255, (value & 255) / 255];
  };
  var rgbToHsl = ([r, g, b]) => {
    const max = Math.max(r, g, b);
    const min = Math.min(r, g, b);
    const lightness = (max + min) / 2;
    const delta = max - min;
    if (delta < 1e-6) {
      return [0, 0, lightness];
    }
    const saturation = delta / (1 - Math.abs(2 * lightness - 1));
    let hue;
    if (max === r) {
      hue = (g - b) / delta % 6;
    } else if (max === g) {
      hue = (b - r) / delta + 2;
    } else {
      hue = (r - g) / delta + 4;
    }
    return [(hue * 60 + 360) % 360, saturation, lightness];
  };
  var hslToRgb = (hue, saturation, lightness) => {
    const h = (hue % 360 + 360) % 360 / 60;
    const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
    const x = chroma * (1 - Math.abs(h % 2 - 1));
    const m = lightness - chroma / 2;
    const sector = Math.floor(h);
    const [r, g, b] = sector === 0 ? [chroma, x, 0] : sector === 1 ? [x, chroma, 0] : sector === 2 ? [0, chroma, x] : sector === 3 ? [0, x, chroma] : sector === 4 ? [x, 0, chroma] : [chroma, 0, x];
    return [clamp012(r + m), clamp012(g + m), clamp012(b + m)];
  };
  var driftHue = (hue, target, amount) => {
    const delta = (target - hue + 540) % 360 - 180;
    return hue + delta * amount;
  };
  var flatten = (stops) => stops.flatMap((stop) => [...stop]);
  var createShaderSceneRampFromColor = (rgb) => {
    const [hue, saturation, lightness] = rgbToHsl(rgb);
    if (saturation < 0.12) {
      const body = Math.min(0.85, Math.max(0.5, lightness));
      return flatten([
        hslToRgb(0, 0, 0.05),
        hslToRgb(0, 0, 0.24),
        hslToRgb(0, 0, body),
        hslToRgb(0, 0, 0.97)
      ]);
    }
    const bodySaturation = Math.min(0.92, Math.max(0.55, saturation));
    const shadeHue = driftHue(hue, 268, 0.3);
    return flatten([
      hslToRgb(shadeHue, Math.min(0.55, bodySaturation * 0.6), 0.075),
      hslToRgb(shadeHue, Math.min(0.7, bodySaturation * 0.85), 0.27),
      hslToRgb(hue, bodySaturation, Math.min(0.64, Math.max(0.52, lightness))),
      hslToRgb(driftHue(hue, 40, 0.18), 0.85, 0.88)
    ]);
  };
  var createCustomShaderScenePalette = (hex, lightTheme) => ({
    ramp: createShaderSceneRampFromColor(hexToRgb(normalizeShaderSceneCustomColor(hex))),
    lightTheme
  });
  var resolveShaderScenePalette = ({ palette, customColor, coverPalette, theme = "auto", mode: mode2 }) => {
    const lightTheme = isLyricsCoverShaderSceneMode(mode2) ? false : theme === "auto" ? coverPalette.lightTheme : theme === "light";
    if (isLyricsCoverShaderSceneMode(mode2) || palette === "cover") {
      const [r, g, b] = coverPalette.colors;
      return { ramp: createShaderSceneRampFromColor([r, g, b]), lightTheme };
    }
    if (palette === "custom") {
      return createCustomShaderScenePalette(customColor, lightTheme);
    }
    return {
      ramp: flatten(shaderScenePalettePresets[palette].map(hexToRgb)),
      lightTheme
    };
  };

  // ../echo/src/renderer/components/lyrics/flowingLightRenderer.ts
  var maximumCanvasPixels = 960 * 540 / 2;
  var vertexShaderSource = `#version 300 es
precision highp float;

void main() {
  vec2 position = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
  gl_Position = vec4(position * 2.0 - 1.0, 0.0, 1.0);
}
`;
  var fragmentShaderSource = `#version 300 es
precision highp float;

uniform vec2 u_resolution;
uniform float u_time;
uniform float u_flow;
uniform float u_seed;
uniform float u_light_theme;
uniform vec3 u_colors[4];
uniform vec4 u_audio;
uniform vec4 u_beat;

out vec4 outputColor;

float hash21(vec2 point) {
  vec3 p3 = fract(vec3(point.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

float valueNoise(vec2 point) {
  vec2 cell = floor(point);
  vec2 local = fract(point);
  vec2 weight = local * local * (3.0 - 2.0 * local);
  float a = hash21(cell);
  float b = hash21(cell + vec2(1.0, 0.0));
  float c = hash21(cell + vec2(0.0, 1.0));
  float d = hash21(cell + vec2(1.0, 1.0));
  return mix(mix(a, b, weight.x), mix(c, d, weight.x), weight.y);
}

float fbm(vec2 point) {
  float value = 0.0;
  float amplitude = 0.5;
  const mat2 twist = mat2(0.8, 0.6, -0.6, 0.8);
  for (int octave = 0; octave < 3; octave++) {
    value += amplitude * valueNoise(point);
    point = twist * point * 2.03 + 1.7;
    amplitude *= 0.5;
  }
  return value;
}

mat2 rotatePoint(float angle) {
  float sine = sin(angle);
  float cosine = cos(angle);
  return mat2(cosine, -sine, sine, cosine);
}

// Rotates hue around the grey axis; cheap analogous accents from one cover color.
vec3 hueShift(vec3 color, float angle) {
  const vec3 axis = vec3(0.57735);
  float cosine = cos(angle);
  float sine = sin(angle);
  return color * cosine + cross(axis, color) * sine + axis * dot(axis, color) * (1.0 - cosine);
}

// Soft band of light around sin(phase) == 1; lower softness = wider band.
float curtain(float phase, float softness) {
  return pow(0.5 + 0.5 * sin(phase), softness);
}

float blob(vec2 point, vec2 center, float radius) {
  vec2 delta = (point - center) / radius;
  return exp(-dot(delta, delta) * 1.6);
}

void main() {
  vec2 uv = gl_FragCoord.xy / max(u_resolution, vec2(1.0));
  float aspect = u_resolution.x / max(u_resolution.y, 1.0);
  vec2 point = (uv - 0.5) * vec2(aspect, 1.0);

  float energy = u_audio.x;
  float bass = u_audio.y;
  float mid = u_audio.z;
  float treble = u_audio.w;
  float beat = u_beat.x;
  float pulse = u_beat.y;
  float activity = u_beat.z;

  float idle = u_time * 0.04;
  float flow = u_flow;
  // The ribbon axis bends smoothly across the screen (and drifts over time),
  // so bands curve like aurora curtains instead of running as parallel stripes.
  float tilt = -0.62 + u_seed * 0.5;
  float bend = (fbm(point * 0.55 + vec2(idle * 0.6, -idle * 0.35) + u_seed * 7.0) - 0.5) * 1.1;
  vec2 axis = rotatePoint(tilt + bend) * point;

  vec3 primary = u_colors[0];
  vec3 soft = u_colors[1];
  vec3 deep = u_colors[2];
  vec3 glow = u_colors[3];
  // Analogous hues drift slowly over the track so the palette never sits still.
  // The warm accent leans on the lighter "soft" swatch so it never turns muddy.
  vec3 accentWarm = mix(hueShift(primary, 0.3 + u_seed * 0.2 + 0.12 * sin(idle * 0.9)), soft, 0.6);
  vec3 accentCool = hueShift(glow, -0.55 + 0.15 * cos(idle * 0.7 + 1.0));
  const vec3 white = vec3(1.0);

  // --- Night base: a deep vertical gradient so the light has darkness to sit in.
  vec3 color = deep * mix(0.22, 0.5, smoothstep(-0.55, 0.55, point.y));

  // --- Back layer: two large colour fields orbiting slowly at 0.3x parallax.
  // They only tint the darkness; the luminance lives in the curtains.
  float drift = flow * 0.3;
  vec2 blobA = vec2(cos(idle * 1.3 + drift), sin(idle * 0.9 + drift * 0.6)) * vec2(0.45 * aspect, 0.3);
  vec2 blobB = vec2(sin(idle * 0.8 - drift * 0.7 + 2.0), cos(idle * 1.1 - drift * 0.4 + 1.0)) * vec2(0.5 * aspect, 0.34);
  float breathe = 1.0 + pulse * 0.14 + bass * 0.06;
  color += primary * blob(point, blobA, 0.55 * breathe) * (0.22 + energy * 0.12);
  color += accentCool * blob(point, blobB, 0.5 * breathe) * (0.2 + mid * 0.1);

  // --- Curtains: domain-warped bands of light. Displacement responds to the
  // bass only gently; the smoothed pulse carries the brightness and width
  // response so a new onset cannot yank the whole curtain sideways.
  vec2 warpField = vec2(
    fbm(axis * vec2(0.9, 1.6) + vec2(-flow * 0.55, idle) + u_seed * 10.0),
    fbm(axis * vec2(0.9, 1.6) + vec2(-flow * 0.35, -idle * 0.7) + 4.7)
  ) - 0.5;
  float warpAmount = 0.3 + bass * 0.035;
  vec2 warped = axis + warpField * warpAmount;
  float field = fbm(warped * vec2(1.1, 2.0) + vec2(-flow * 0.7, idle * 0.5) + u_seed * 3.0);

  // Aurora rays: brightness varies along each band, so a curtain has bright
  // stretches and dim stretches instead of glowing evenly end to end.
  float raysA = 0.45 + 0.55 * fbm(vec2(warped.x * 1.6 - flow * 0.45, warped.y * 0.35 + idle));
  float raysB = 0.4 + 0.6 * fbm(vec2(warped.x * 2.2 + flow * 0.3 + 5.0, warped.y * 0.4 - idle * 0.6));

  float widen = 1.0 / (1.0 + pulse * 0.25 + bass * 0.2);
  float phaseA = warped.y * 5.0 + field * 4.0 - flow * 1.9;
  float phaseB = warped.y * 8.5 - field * 6.0 + flow * 1.3 + 2.1 + warped.x * 0.8;
  float phaseC = warped.y * 3.0 + field * 2.5 - flow * 1.1 + 4.0;
  float haloA = curtain(phaseA, 6.0 * widen) * raysA;
  float coreA = curtain(phaseA, 22.0 * widen) * raysA;
  float haloB = curtain(phaseB, 7.0 * widen) * raysB;
  float coreB = curtain(phaseB, 30.0) * raysB;
  float haloC = curtain(phaseC, 3.0) * (0.6 + 0.4 * raysA);

  // Loudness sets the overall exposure; the beat envelope and tempo pulse
  // modulate it on top, which is the most legible rhythm cue at any frame rate.
  float lightGain = 0.5 + energy * 0.45 + beat * 0.2 + pulse * 0.35 + activity * 0.05;
  color += primary * haloA * (0.85 * lightGain);
  color += mix(soft, white, 0.35) * coreA * (0.7 * lightGain) * (0.55 + bass * 0.5 + pulse * 0.25);
  color += accentCool * haloB * (0.55 * lightGain) * (0.5 + mid * 0.8);
  color += mix(glow, white, 0.5) * coreB * (0.4 * lightGain) * (0.25 + treble * 0.9);
  color += accentWarm * haloC * 0.16 * lightGain * (0.7 + pulse * 0.5);

  // Soft tone-map keeps ribbons saturated instead of clipping to white.
  color = 1.0 - exp(-color * 1.3);
  float luma = dot(color, vec3(0.299, 0.587, 0.114));
  color = mix(vec3(luma), color, 1.2);

  // Light theme: darkness becomes paper and the curtains become vivid tints
  // on it, so the same structure reads as coloured light rather than grey fog.
  const vec3 paper = vec3(0.965, 0.97, 0.98);
  vec3 vivid = mix(vec3(luma), color, 1.6);
  // Dark or neutral covers still need visible structure in light mode.
  // A zero tint weight erased almost the whole field into white paper.
  float tintWeight = 0.24 + 0.62 * smoothstep(0.01, 0.32, luma);
  vec3 pastel = mix(paper, mix(vivid, paper, 0.18), tintWeight);
  color = mix(color, pastel, u_light_theme);

  // Fixed dithering breaks color bands without sparkling on every frame.
  float grain = hash21(gl_FragCoord.xy) - 0.5;
  color += grain * 0.012;

  float edgeFalloff = smoothstep(0.3, 1.1, length((uv - 0.5) * vec2(0.95, 1.05)));
  color *= mix(1.0, mix(0.7, 0.92, u_light_theme), edgeFalloff);

  outputColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}
`;
  var resolveFlowingLightCanvasSize = (cssWidth, cssHeight, devicePixelRatio) => {
    const safeWidth = Math.max(1, cssWidth);
    const safeHeight = Math.max(1, cssHeight);
    const safePixelRatio = Math.max(0.5, Number.isFinite(devicePixelRatio) ? devicePixelRatio : 1);
    const cappedScale = Math.min(safePixelRatio, Math.sqrt(maximumCanvasPixels / (safeWidth * safeHeight)));
    return {
      width: Math.max(1, Math.floor(safeWidth * cappedScale)),
      height: Math.max(1, Math.floor(safeHeight * cappedScale))
    };
  };
  var compileShader2 = (gl, type, source) => {
    const shader = gl.createShader(type);
    if (!shader) {
      throw new Error("Unable to allocate flowing-light shader.");
    }
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      gl.deleteShader(shader);
      throw new Error("Unable to compile flowing-light shader.");
    }
    return shader;
  };
  var requireUniform = (gl, program, name) => {
    const location = gl.getUniformLocation(program, name);
    if (!location) {
      throw new Error(`Missing flowing-light uniform: ${name}`);
    }
    return location;
  };
  var createFlowingLightRenderer = (canvas2, initialPalette, initialTrackKey) => {
    const gl = canvas2.getContext("webgl2", {
      alpha: false,
      antialias: false,
      depth: false,
      failIfMajorPerformanceCaveat: true,
      powerPreference: "low-power",
      premultipliedAlpha: false,
      preserveDrawingBuffer: false,
      stencil: false
    });
    if (!gl) {
      return null;
    }
    let vertexShader = null;
    let fragmentShader = null;
    let program = null;
    let vertexArray = null;
    let disposed = false;
    try {
      vertexShader = compileShader2(gl, gl.VERTEX_SHADER, vertexShaderSource);
      fragmentShader = compileShader2(gl, gl.FRAGMENT_SHADER, fragmentShaderSource);
      program = gl.createProgram();
      if (!program) {
        throw new Error("Unable to allocate flowing-light program.");
      }
      gl.attachShader(program, vertexShader);
      gl.attachShader(program, fragmentShader);
      gl.linkProgram(program);
      if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
        throw new Error("Unable to link flowing-light program.");
      }
      vertexArray = gl.createVertexArray();
      if (!vertexArray) {
        throw new Error("Unable to allocate flowing-light vertex array.");
      }
      const resolutionUniform = requireUniform(gl, program, "u_resolution");
      const timeUniform = requireUniform(gl, program, "u_time");
      const flowUniform = requireUniform(gl, program, "u_flow");
      const seedUniform = requireUniform(gl, program, "u_seed");
      const lightThemeUniform = requireUniform(gl, program, "u_light_theme");
      const colorsUniform = requireUniform(gl, program, "u_colors[0]");
      const audioUniform = requireUniform(gl, program, "u_audio");
      const beatUniform = requireUniform(gl, program, "u_beat");
      const currentPalette = new Float32Array(initialPalette.colors);
      const targetPalette = new Float32Array(initialPalette.colors);
      let lightTheme = initialPalette.lightTheme ? 1 : 0;
      let targetLightTheme = lightTheme;
      let trackSeed = hashFlowingLightTrackSeed(initialTrackKey);
      let sceneTimeSeconds = 0;
      const advanceEnvelope = createFlowingLightEnvelope();
      gl.useProgram(program);
      gl.bindVertexArray(vertexArray);
      gl.disable(gl.BLEND);
      gl.disable(gl.DEPTH_TEST);
      const renderer2 = {
        draw: (_timestamp, elapsedMs, drive) => {
          if (disposed || gl.isContextLost()) {
            return;
          }
          sceneTimeSeconds += Math.max(0, Math.min(250, elapsedMs)) / 1e3;
          const visible = advanceEnvelope(drive, elapsedMs);
          const paletteWeight = 1 - Math.exp(-Math.max(0, elapsedMs) / 300);
          for (let index = 0; index < currentPalette.length; index += 1) {
            currentPalette[index] += (targetPalette[index] - currentPalette[index]) * paletteWeight;
          }
          lightTheme += (targetLightTheme - lightTheme) * paletteWeight;
          gl.useProgram(program);
          gl.bindVertexArray(vertexArray);
          gl.uniform2f(resolutionUniform, canvas2.width, canvas2.height);
          gl.uniform1f(timeUniform, sceneTimeSeconds);
          gl.uniform1f(flowUniform, drive.flow);
          gl.uniform1f(seedUniform, trackSeed);
          gl.uniform1f(lightThemeUniform, lightTheme);
          gl.uniform3fv(colorsUniform, currentPalette);
          gl.uniform4f(audioUniform, visible.energy, visible.bass, visible.mid, visible.treble);
          gl.uniform4f(beatUniform, visible.beat, visible.pulse, visible.activity, 0);
          gl.drawArrays(gl.TRIANGLES, 0, 3);
        },
        resize: (cssWidth, cssHeight, devicePixelRatio) => {
          if (disposed || gl.isContextLost()) {
            return;
          }
          const size = resolveFlowingLightCanvasSize(cssWidth, cssHeight, devicePixelRatio);
          if (canvas2.width !== size.width || canvas2.height !== size.height) {
            canvas2.width = size.width;
            canvas2.height = size.height;
            gl.viewport(0, 0, size.width, size.height);
          }
        },
        setPalette: (palette, immediate = false) => {
          targetPalette.set(palette.colors);
          targetLightTheme = palette.lightTheme ? 1 : 0;
          if (immediate) {
            currentPalette.set(palette.colors);
            lightTheme = targetLightTheme;
          }
        },
        setTrackKey: (trackKey) => {
          trackSeed = hashFlowingLightTrackSeed(trackKey);
        },
        isContextLost: () => disposed || gl.isContextLost(),
        park: () => {
          if (disposed || gl.isContextLost()) {
            return;
          }
          if (canvas2.width !== 1 || canvas2.height !== 1) {
            canvas2.width = 1;
            canvas2.height = 1;
            gl.viewport(0, 0, 1, 1);
          }
          gl.clearColor(0, 0, 0, 1);
          gl.clear(gl.COLOR_BUFFER_BIT);
          gl.flush();
        },
        dispose: () => {
          if (disposed) {
            return;
          }
          disposed = true;
          gl.bindVertexArray(null);
          gl.useProgram(null);
          if (vertexArray) gl.deleteVertexArray(vertexArray);
          if (program) gl.deleteProgram(program);
          if (vertexShader) gl.deleteShader(vertexShader);
          if (fragmentShader) gl.deleteShader(fragmentShader);
          gl.getExtension("WEBGL_lose_context")?.loseContext();
        }
      };
      return renderer2;
    } catch {
      if (vertexArray) gl.deleteVertexArray(vertexArray);
      if (program) gl.deleteProgram(program);
      if (vertexShader) gl.deleteShader(vertexShader);
      if (fragmentShader) gl.deleteShader(fragmentShader);
      gl.getExtension("WEBGL_lose_context")?.loseContext();
      return null;
    }
  };
  var sharedSurface2 = null;
  var discardFlowingLightSurface = () => {
    if (!sharedSurface2) {
      return;
    }
    const surface = sharedSurface2;
    sharedSurface2 = null;
    surface.renderer.dispose();
    surface.canvas.remove();
  };
  registerLyricsResourceOwner({
    id: "flowing-light-surface",
    releaseOnExit: false,
    releaseOnPressure: true,
    snapshot: () => ({
      kind: "webgl",
      state: !sharedSurface2 ? "empty" : sharedSurface2.canvas.parentElement ? "active" : "parked",
      backingBytes: sharedSurface2 ? sharedSurface2.canvas.width * sharedSurface2.canvas.height * 4 : 0,
      retainedFor: "avoid-context-churn"
    }),
    releaseUnused: discardFlowingLightSurface
  });

  // ../echo/src/renderer/components/lyrics/lyricsBackdropPalette.ts
  var clampChannel = (value) => Math.round(Math.max(0, Math.min(255, value)));
  var clampUnit2 = (value) => Math.max(0, Math.min(1, value));
  var mixRgb = (from, to, amount) => {
    const weight = clampUnit2(amount);
    return {
      r: from.r + (to.r - from.r) * weight,
      g: from.g + (to.g - from.g) * weight,
      b: from.b + (to.b - from.b) * weight
    };
  };
  var rgbToCssChannels = (rgb) => `${clampChannel(rgb.r)} ${clampChannel(rgb.g)} ${clampChannel(rgb.b)}`;
  var rgbToUnitChannels = (rgb) => [
    clampUnit2(rgb.r / 255),
    clampUnit2(rgb.g / 255),
    clampUnit2(rgb.b / 255)
  ];
  var fallbackFlowingLightPalette = (themeMode) => ({
    colors: themeMode === "light" ? [0.34, 0.55, 0.76, 0.74, 0.69, 0.86, 0.24, 0.34, 0.52, 0.57, 0.78, 0.91] : [0.18, 0.38, 0.62, 0.38, 0.24, 0.56, 0.025, 0.045, 0.09, 0.28, 0.58, 0.74],
    lightTheme: themeMode === "light"
  });
  var createLyricsBackdropPalette = (sample, themeMode) => {
    const resolvedThemeMode = themeMode ?? "dark";
    if (!sample) {
      return {
        coverColorCssVars: null,
        flowingLight: fallbackFlowingLightPalette(resolvedThemeMode)
      };
    }
    const primary = sample.dominantRgb ?? sample.averageRgb;
    const averageMix = mixRgb(primary, sample.averageRgb, 0.28);
    const textTint = mixRgb(averageMix, { r: 255, g: 255, b: 255 }, 0.86);
    const lightAnchor = themeMode === "dark" ? { r: 42, g: 49, b: 64 } : { r: 246, g: 248, b: 251 };
    const darkAnchor = themeMode === "dark" ? { r: 8, g: 12, b: 18 } : { r: 52, g: 58, b: 68 };
    const soft = themeMode === null ? mixRgb(averageMix, { r: 255, g: 255, b: 255 }, 0.28) : mixRgb(averageMix, lightAnchor, themeMode === "dark" ? 0.24 : 0.42);
    const deep = themeMode === null ? mixRgb(averageMix, { r: 8, g: 10, b: 14 }, 0.58) : mixRgb(primary, darkAnchor, themeMode === "dark" ? 0.56 : 0.24);
    const glow = themeMode === null ? mixRgb(primary, sample.averageRgb, 0.36) : mixRgb(primary, lightAnchor, themeMode === "dark" ? 0.12 : 0.1);
    const primaryUnit = rgbToUnitChannels(primary);
    const softUnit = rgbToUnitChannels(soft);
    const deepUnit = rgbToUnitChannels(deep);
    const glowUnit = rgbToUnitChannels(glow);
    return {
      coverColorCssVars: {
        "--lyrics-cover-color-rgb": rgbToCssChannels(primary),
        "--lyrics-cover-color-soft-rgb": rgbToCssChannels(soft),
        "--lyrics-cover-color-deep-rgb": rgbToCssChannels(deep),
        "--lyrics-cover-color-glow-rgb": rgbToCssChannels(glow),
        "--lyrics-cover-text-rgb": rgbToCssChannels(textTint)
      },
      flowingLight: {
        colors: [
          ...primaryUnit,
          ...softUnit,
          ...deepUnit,
          ...glowUnit
        ],
        lightTheme: themeMode === "light"
      }
    };
  };

  // ../echo/src/renderer/components/lyrics/cover-flow/coverFlowColor.ts
  var clampUnit3 = (value) => Math.max(0, Math.min(1, value));
  var linear = (v) => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  var rgbToLab = ([r, g, b]) => {
    r = linear(r);
    g = linear(g);
    b = linear(b);
    const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
    const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
    const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
    return [
      0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s,
      1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
      0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s
    ];
  };
  var colorDistance = (a, b) => (a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2;
  var colorChroma = (lab) => Math.hypot(lab[1], lab[2]);
  var toneColor = (rgb, lightness) => {
    const [, a, b] = rgbToLab(rgb);
    const chroma = Math.hypot(a, b);
    let saturation = chroma < 1e-3 ? 0 : Math.min(0.92, 0.16 / chroma);
    for (let attempt = 0; attempt < 9; attempt++) {
      const l = (lightness + 0.3963377774 * a * saturation + 0.2158037573 * b * saturation) ** 3;
      const m = (lightness - 0.1055613458 * a * saturation - 0.0638541728 * b * saturation) ** 3;
      const s = (lightness - 0.0894841775 * a * saturation - 1.291485548 * b * saturation) ** 3;
      const channels = [
        4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s
      ];
      if (channels.every((v) => v >= 0 && v <= 1) || attempt === 8) {
        return channels.map((v) => clampUnit3(v <= 31308e-7 ? v * 12.92 : 1.055 * Math.max(0, v) ** (1 / 2.4) - 0.055));
      }
      saturation *= 0.75;
    }
    return rgb;
  };
  var mixColor = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

  // ../echo/src/renderer/components/lyrics/cover-flow/coverFlowShader.ts
  var coverFlowVertexShader = `
attribute vec2 a_position;
varying vec2 v_uv;
void main() { v_uv = a_position * .5 + .5; gl_Position = vec4(a_position, 0., 1.); }
`;
  var coverFlowFragmentShader = `
precision highp float;
varying vec2 v_uv;
uniform vec3 u_colors[5];
uniform float u_time;
uniform float u_aspect;
uniform float u_light;

vec3 toSrgb(vec3 lab) {
  vec3 lms = vec3(lab.x + .3963377774 * lab.y + .2158037573 * lab.z,
    lab.x - .1055613458 * lab.y - .0638541728 * lab.z,
    lab.x - .0894841775 * lab.y - 1.2914855480 * lab.z);
  lms = lms * lms * lms;
  vec3 rgb = max(vec3(0.), vec3(
    4.0767416621 * lms.x - 3.3077115913 * lms.y + .2309699292 * lms.z,
    -1.2684380046 * lms.x + 2.6097574011 * lms.y - .3413193965 * lms.z,
    -.0041960863 * lms.x - .7034186147 * lms.y + 1.7076147010 * lms.z));
  vec3 encoded = mix(rgb * 12.92, 1.055 * pow(rgb, vec3(1. / 2.4)) - .055,
    step(vec3(.0031308), rgb));
  return clamp(encoded, 0., 1.);
}
float field(vec2 p, vec2 center, vec2 radius, float angle) {
  vec2 d = p - center;
  d.x *= clamp(u_aspect / 1.7778, .55, 1.6);
  float c = cos(angle), s = sin(angle);
  d = mat2(c, -s, s, c) * d / radius;
  return exp(-4.2 * dot(d, d));
}
void main() {
  // Short viewing intervals must show translation as well as changing colour.
  float t = .9 + (u_time - 12.) * .68;
  vec2 p = vec2(v_uv.x, 1. - v_uv.y);
  vec2 flowCenter = .5 + .19 * vec2(sin(t * .61), cos(t * .73));
  vec2 flowDelta = p - flowCenter;
  float swirl = .24 * sin(t * .93 + dot(flowDelta, flowDelta) * 3.6)
    * exp(-dot(flowDelta, flowDelta) * 1.7);
  p = flowCenter + mat2(cos(swirl), -sin(swirl), sin(swirl), cos(swirl)) * flowDelta;
  p += vec2(sin(p.y * 4.2 + t * .71), cos(p.x * 3.8 - t * .63)) * .08;
  p += vec2(sin(p.x * 2.7 + p.y * 2.1 - t * .37),
    cos(p.y * 3.1 - p.x * 1.8 + t * .41)) * .04;
  vec2 c1 = vec2(.18 + .24 * sin(t * .83), .26 + .27 * cos(t * .67));
  vec2 c2 = vec2(.79 + .24 * cos(t * .61 + 2.), .66 + .28 * sin(t * .77));
  vec2 c3 = vec2(.22 + .38 * sin(t * .53 - 1.3), .27 + .35 * cos(t * .59 + .4));
  vec2 c4 = vec2(.50 + .40 * sin(t * .71 + 2.2), .55 + .30 * sin(t * .57 + .4));
  vec2 c5 = vec2(.62 + .32 * cos(t * .43), .33 + .24 * cos(t * .69 + .8));
  float w0 = .18 + field(p, c1, vec2(.83, .82), t * .23) * .65;
  float w1 = field(p, c2, vec2(.79, .89), -t * .19) * .8;
  vec2 accentRadius = vec2(.70 * (1. + .13 * sin(t * .91 + .3)),
    .76 * (1. + .10 * cos(t * .79)));
  vec2 lightRadius = vec2(.56 * (1. + .06 * sin(t * 1.07)),
    .62 * (1. + .06 * cos(t * .89)));
  float w2 = field(p, c3, accentRadius, t * .17) * 2.8;
  // Reuse the core field for a broad, low-energy halo; no extra texture or pass.
  float lightCore = field(p, c4, lightRadius, t * .31);
  float w3 = lightCore * 3.4 + sqrt(lightCore) * .55
    + field(p, c5, vec2(.63, .58), -t * .27) * .4;
  float shadowCore = field(p, vec2(.64, .40) + vec2(.32 * cos(t * .67), .28 * sin(t * .7)),
    vec2(.65, .72), t * .13);
  float w4 = shadowCore * 2.6;
  vec3 lab = (u_colors[0] * w0 + u_colors[1] * w1 + u_colors[2] * w2
    + u_colors[3] * w3 + u_colors[4] * w4) / (w0 + w1 + w2 + w3 + w4);
  // A gentle veil protects foreground contrast without crushing the cover's hues.
  lab.x = mix(lab.x, .70 + .20 * clamp((lab.x - .28) / .44, 0., 1.), u_light);
  vec3 rgb = toSrgb(lab);
  float noise = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
  rgb += (noise - .5) / 255.;
  gl_FragColor = vec4(rgb, 1.);
}
`;

  // ../echo/src/renderer/components/lyrics/cover-flow/coverFlowRenderer.ts
  var coverFlowMaxPixels = 196608;
  var coverFlowRenderSize = (width, height) => {
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) return { width: 1, height: 1 };
    const ratio = Math.min(1, 768 / Math.max(width, height), Math.sqrt(coverFlowMaxPixels / (width * height)));
    return { width: Math.max(1, Math.floor(width * ratio)), height: Math.max(1, Math.floor(height * ratio)) };
  };
  var createCoverFlowRenderer = (canvas2, palette) => {
    let gl;
    try {
      gl = canvas2.getContext("webgl", {
        alpha: false,
        antialias: false,
        depth: false,
        stencil: false,
        preserveDrawingBuffer: false,
        powerPreference: "low-power"
      });
    } catch {
      return null;
    }
    if (!gl) return null;
    const context = gl;
    const program = context.createProgram(), buffer = context.createBuffer();
    const shaders = [];
    let disposed = false;
    const release = () => {
      if (disposed) return;
      disposed = true;
      shaders.forEach((shader) => context.deleteShader(shader));
      if (program) context.deleteProgram(program);
      if (buffer) context.deleteBuffer(buffer);
      canvas2.width = canvas2.height = 1;
      context.getExtension("WEBGL_lose_context")?.loseContext();
    };
    if (!program || !buffer) {
      release();
      return null;
    }
    try {
      for (const [type, source] of [
        [context.VERTEX_SHADER, coverFlowVertexShader],
        [context.FRAGMENT_SHADER, coverFlowFragmentShader]
      ]) {
        const shader = context.createShader(type);
        if (!shader) throw new Error("Allocation failed");
        shaders.push(shader);
        context.shaderSource(shader, source);
        context.compileShader(shader);
        if (!context.getShaderParameter(shader, context.COMPILE_STATUS)) throw new Error("Compilation failed");
        context.attachShader(program, shader);
      }
      context.linkProgram(program);
      if (!context.getProgramParameter(program, context.LINK_STATUS)) throw new Error("Link failed");
      context.useProgram(program);
      context.bindBuffer(context.ARRAY_BUFFER, buffer);
      context.bufferData(context.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), context.STATIC_DRAW);
      const position = context.getAttribLocation(program, "a_position");
      if (position < 0) throw new Error("Missing position");
      context.enableVertexAttribArray(position);
      context.vertexAttribPointer(position, 2, context.FLOAT, false, 0, 0);
      const colors = context.getUniformLocation(program, "u_colors[0]");
      const time = context.getUniformLocation(program, "u_time");
      const aspect = context.getUniformLocation(program, "u_aspect");
      const light2 = context.getUniformLocation(program, "u_light");
      const current = new Float32Array(15), from = new Float32Array(15), target = new Float32Array(15);
      let previousSeconds = 0, transitionAt = -Infinity;
      const setPalette = (next, immediate = false) => {
        from.set(current);
        next.colors.forEach((rgb, i) => target.set(rgbToLab(rgb), i * 3));
        transitionAt = previousSeconds;
        if (immediate) {
          current.set(target);
          transitionAt = -Infinity;
        }
      };
      setPalette(palette, true);
      return {
        resize(width, height) {
          if (disposed) return;
          const size = coverFlowRenderSize(width, height);
          if (canvas2.width !== size.width || canvas2.height !== size.height) {
            canvas2.width = size.width;
            canvas2.height = size.height;
          }
          context.viewport(0, 0, size.width, size.height);
          context.uniform1f(aspect, width > 0 && height > 0 ? width / height : 1);
        },
        setPalette,
        draw(seconds, lightTheme) {
          if (disposed || context.isContextLost()) return;
          previousSeconds = seconds;
          const progress = Math.max(0, Math.min(1, (seconds - transitionAt) / 1.2));
          const eased = progress * progress * (3 - 2 * progress);
          for (let i = 0; i < 15; i++) current[i] = from[i] + (target[i] - from[i]) * eased;
          context.uniform3fv(colors, current);
          context.uniform1f(time, seconds);
          context.uniform1f(light2, lightTheme ? 1 : 0);
          context.drawArrays(context.TRIANGLE_STRIP, 0, 4);
        },
        dispose: release
      };
    } catch {
      release();
      return null;
    }
  };

  // ../echo/src/renderer/components/lyrics/cover-flow/coverFlowPalette.ts
  var coverFlowMaxSamples = 4096;
  var defaultCoverFlowPalette = {
    colors: [[0.24, 0.32, 0.38], [0.23, 0.28, 0.34], [0.32, 0.39, 0.45], [0.54, 0.59, 0.62], [0.13, 0.19, 0.24]],
    swatches: []
  };
  var clusterPixels = (pixels) => {
    const bins = /* @__PURE__ */ new Map();
    const pixelCount = Math.floor(pixels.length / 4);
    const stride = Math.max(1, Math.ceil(pixelCount / coverFlowMaxSamples));
    let total = 0;
    for (let p = 0; p < pixelCount; p += stride) {
      const i = p * 4, alpha = pixels[i + 3] / 255;
      if (alpha < 0.5) continue;
      const key = (pixels[i] >> 5) * 64 + (pixels[i + 1] >> 5) * 8 + (pixels[i + 2] >> 5);
      const bin = bins.get(key) ?? { sum: [0, 0, 0], weight: 0 };
      for (let c = 0; c < 3; c++) bin.sum[c] += pixels[i + c] / 255 * alpha;
      bin.weight += alpha;
      total += alpha;
      bins.set(key, bin);
    }
    if (!total) return [];
    const points = Array.from(bins.values(), (bin) => {
      const rgb = bin.sum.map((v) => v / bin.weight);
      return { rgb, lab: rgbToLab(rgb), weight: bin.weight / total };
    }).sort((a, b) => b.weight - a.weight);
    const centers = [points[0].lab];
    while (centers.length < Math.min(8, points.length)) {
      const farthest = points.reduce((best, point) => {
        const score = Math.min(...centers.map((c) => colorDistance(point.lab, c))) * Math.sqrt(point.weight);
        return score > best.score ? { point, score } : best;
      }, { point: points[0], score: 0 });
      if (farthest.score < 2e-4) break;
      centers.push(farthest.point.lab);
    }
    let clusters = [];
    for (let iteration = 0; iteration < 8; iteration++) {
      const groups = centers.map(() => ({ sum: [0, 0, 0], weight: 0 }));
      for (const point of points) {
        let nearest = 0, distance = Infinity;
        centers.forEach((center, index) => {
          const d = colorDistance(point.lab, center);
          if (d < distance) {
            distance = d;
            nearest = index;
          }
        });
        const group = groups[nearest];
        group.weight += point.weight;
        for (let c = 0; c < 3; c++) group.sum[c] += point.rgb[c] * point.weight;
      }
      clusters = groups.filter((g) => g.weight > 0).map((g) => {
        const rgb = g.sum.map((v) => v / g.weight);
        return { rgb, lab: rgbToLab(rgb), weight: g.weight };
      });
      centers.splice(0, centers.length, ...clusters.map((c) => c.lab));
    }
    return clusters.sort((a, b) => b.weight - a.weight);
  };
  var extractCoverFlowPalette = (pixels) => {
    const swatches = clusterPixels(pixels);
    if (!swatches.length) return defaultCoverFlowPalette;
    const chromatic = swatches.filter((s) => colorChroma(s.lab) >= 0.035 && s.weight >= 0.025);
    const chromaticMass = chromatic.reduce((sum, s) => sum + s.weight, 0);
    const ranked = chromaticMass >= 0.12 ? chromatic : swatches;
    const dominant = ranked.reduce((a, b) => b.weight * (1 + Math.min(0.12, colorChroma(b.lab)) * 3) > a.weight * (1 + Math.min(0.12, colorChroma(a.lab)) * 3) ? b : a);
    const related = ranked.filter((s) => s !== dominant && s.weight >= 0.04);
    const secondary = related.find((s) => colorDistance(s.lab, dominant.lab) >= 6e-3) ?? dominant;
    const accent = chromatic.filter((s) => s.weight >= 0.025).reduce((a, b) => b.weight ** 0.2 * colorChroma(b.lab) > a.weight ** 0.2 * colorChroma(a.lab) ? b : a, dominant);
    const neutral = swatches.filter((s) => s.weight >= 0.04 && s.lab[0] > 0.65 && colorChroma(s.lab) < 0.055).sort((a, b) => b.lab[0] * Math.sqrt(b.weight) - a.lab[0] * Math.sqrt(a.weight))[0];
    const highlight = neutral ? mixColor(neutral.rgb, dominant.rgb, 0.24) : mixColor(dominant.rgb, [0.7, 0.7, 0.7], 0.25);
    const shadow = swatches.filter((s) => s.weight >= 0.04).reduce((a, b) => b.lab[0] < a.lab[0] ? b : a, dominant);
    const baseLightness = 0.44 + 0.1 * clampUnit3((dominant.lab[0] - 0.2) / 0.65);
    const secondaryLightness = baseLightness - 0.035 + 0.18 * Math.max(-0.18, Math.min(0.18, secondary.lab[0] - dominant.lab[0]));
    return {
      colors: [
        toneColor(dominant.rgb, baseLightness),
        toneColor(secondary.rgb, secondaryLightness),
        toneColor(accent.rgb, baseLightness + 0.045),
        toneColor(highlight, baseLightness + 0.145),
        toneColor(mixColor(shadow.rgb, dominant.rgb, 0.15), baseLightness - 0.13)
      ],
      swatches
    };
  };

  // ../echo/src/shared/constants/geometricAmbienceSettings.ts
  var defaultGeometricAmbienceSettings = {
    lyricsGeometricCoverColorsEnabled: false,
    lyricsGeometricSpeedPercent: 100,
    lyricsGeometricIntensityPercent: 100,
    lyricsGeometricMusicResponsePercent: 100
  };

  // image:../../assets/geometric-ambience/ambient.png
  var ambient_default = "ambient.png";

  // image:../../assets/geometric-ambience/geometry.png
  var geometry_default = "geometry.png";

  // ../echo/src/renderer/components/lyrics/geometricAmbienceLayout.ts
  var sourceWidth = 1672;
  var sourceHeight = 941;
  var sprites = [[79, 223, 298, 291], [488, 225, 279, 284], [872, 232, 309, 271], [80, 719, 296, 291], [488, 724, 278, 280], [874, 730, 312, 275]];
  var shapes = [
    [0, 343, 404, 516, 0, 0.045, 0.2],
    [2, 330, 423, 354, -0.12, 0.075, 1.5],
    [4, 224, 689, 410, 0.36, 0.052, 2.1],
    [4, 351, 818, 137, -0.36, 0.055, 3.6],
    [3, 679, 791, 302, 0, 0.07, 2.8],
    [0, 803, 715, 264, 0, 0.115, 4.2],
    [3, 910, 69, 223, 0, 0.048, 1.2],
    [4, 783, 233, 56, 0.78, 0.048, 0.8],
    [1, 1240, 232, 133, -0.21, 0.085, 5.2],
    [5, 1484, 275, 238, 3.28, 0.035, 2.4],
    [3, 1299, 835, 477, 0, 0.065, 3.2],
    [2, 1531, 705, 329, 1.44, 0.1, 4.9],
    // Smaller overlapping groups bridge the open center without a uniform grid.
    [0, 565, 270, 182, 0, 0.105, 2.6],
    [1, 604, 321, 104, 0.32, 0.09, 4.4],
    [2, 710, 458, 151, -0.42, 0.1, 1.8],
    [3, 663, 508, 78, 0, 0.055, 5.7],
    [1, 974, 404, 194, 0.24, 0.09, 3.8],
    [0, 1095, 475, 128, 0, 0.115, 0.5],
    [5, 1158, 573, 116, 0.65, 0.042, 4.6],
    [4, 936, 649, 87, -0.52, 0.05, 2.9],
    [0, 289, 142, 108, 0, 0.09, 5.9],
    [2, 1360, 478, 89, 0.2, 0.095, 1.1]
  ];
  var dust = [
    [0.197, 0.027, 10, 4],
    [0.418, 0.167, 12, 3],
    [0.795, 0.072, 13, 3],
    [0.985, 0.143, 12, 4],
    [0.032, 0.351, 12, 3],
    [0.295, 0.412, 12, 3],
    [0.385, 0.482, 12, 4],
    [0.887, 0.49, 11, 4],
    [0.727, 0.518, 12, 3],
    [0.519, 0.602, 13, 3],
    [0.095, 0.68, 11, 4],
    [0.594, 0.791, 12, 4],
    [0.731, 0.909, 12, 3],
    [0.315, 0.92, 11, 4],
    [0.113, 0.207, 7, 3],
    [0.255, 0.287, 8, 4],
    [0.347, 0.098, 6, 3],
    [0.468, 0.343, 9, 4],
    [0.564, 0.229, 7, 3],
    [0.646, 0.116, 8, 4],
    [0.862, 0.298, 6, 3],
    [0.936, 0.659, 8, 4],
    [0.809, 0.753, 7, 3],
    [0.659, 0.689, 9, 4],
    [0.436, 0.746, 6, 3],
    [0.223, 0.571, 8, 4],
    [0.153, 0.861, 7, 3],
    [0.551, 0.941, 6, 4]
  ];

  // ../echo/src/renderer/components/lyrics/geometricAmbienceRenderer.ts
  var geometricCanvasSize = (width, height) => {
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) return [1, 1];
    const ratio = Math.min(1, Math.sqrt(921600 / (width * height)));
    return [Math.max(1, Math.floor(width * ratio)), Math.max(1, Math.floor(height * ratio))];
  };
  var createGeometricAmbienceRenderer = (canvas2, initialLightTheme) => {
    const context = canvas2.getContext("2d", { alpha: false });
    const plate = document.createElement("canvas");
    const ink = document.createElement("canvas");
    const plateContext = plate.getContext("2d", { alpha: false });
    const inkContext = ink.getContext("2d");
    if (!context || !plateContext || !inkContext) return null;
    let ambient = null;
    let atlas = null;
    let disposed = false;
    let lightTheme = initialLightTheme;
    let seconds = 0;
    let settings = defaultGeometricAmbienceSettings;
    let tint = 0;
    let targetTint = 0;
    const tintRgb = [128, 128, 128];
    const targetRgb = [128, 128, 128];
    let inkColor = "";
    let inkElapsed = 100;
    const updateTint = (delta, immediate = false) => {
      const amount = immediate ? 1 : 1 - Math.exp(-delta / 260);
      tint += (targetTint - tint) * amount;
      if (Math.abs(targetTint - tint) < 1e-3) tint = targetTint;
      for (let i = 0; i < 3; i += 1) {
        tintRgb[i] += (targetRgb[i] - tintRgb[i]) * amount;
        if (Math.abs(targetRgb[i] - tintRgb[i]) < 0.01) tintRgb[i] = targetRgb[i];
      }
    };
    ink.width = ink.height = 512;
    const pendingImages = /* @__PURE__ */ new Set();
    const load = async (url, width, height) => globalThis.__backgroundAsset(url, width, height);
    const preparePlate = () => {
      if (!ambient || disposed) return;
      plate.width = canvas2.width;
      plate.height = canvas2.height;
      const scale = Math.max(plate.width / ambient.width, plate.height / ambient.height);
      plateContext.filter = lightTheme ? "invert(1)" : "none";
      plateContext.drawImage(
        ambient,
        (plate.width - ambient.width * scale) / 2,
        (plate.height - ambient.height * scale) / 2,
        ambient.width * scale,
        ambient.height * scale
      );
      plateContext.filter = "none";
    };
    const prepareInk = () => {
      if (!atlas || disposed) return;
      inkContext.clearRect(0, 0, 512, 512);
      inkContext.drawImage(atlas, 0, 0);
      if (lightTheme || tint > 0) {
        inkContext.globalCompositeOperation = "source-in";
        inkContext.fillStyle = inkColor || "#263341";
        inkContext.fillRect(0, 0, 512, 512);
        inkContext.globalCompositeOperation = "source-over";
      }
    };
    const sprite = (kind, x, y, size, rotation, opacity) => {
      const rect = sprites[kind];
      const atlasScale = 512 / 1254;
      context.save();
      context.translate(x, y);
      context.rotate(rotation);
      context.globalAlpha = Math.min(1, Math.max(0, opacity));
      context.drawImage(
        ink,
        rect[0] * atlasScale,
        rect[1] * atlasScale,
        rect[2] * atlasScale,
        rect[3] * atlasScale,
        -size / 2,
        -size / 2,
        size,
        size
      );
      context.restore();
    };
    return {
      needsAnimationFrame: () => !disposed && (settings.lyricsGeometricSpeedPercent > 0 || settings.lyricsGeometricMusicResponsePercent > 0 || tint !== targetTint || targetTint > 0 && tintRgb.some((channel, i) => channel !== targetRgb[i])),
      ready: Promise.all([
        load(ambient_default, 1024, 576).then((bitmap) => {
          ambient = bitmap;
        }),
        load(geometry_default, 512, 512).then((bitmap) => {
          atlas = bitmap;
        })
      ]).then(() => {
        if (!disposed) {
          preparePlate();
          prepareInk();
        }
      }),
      resize(width, height) {
        if (disposed) return;
        const [w, h] = geometricCanvasSize(width, height);
        if (canvas2.width === w && canvas2.height === h) return;
        canvas2.width = w;
        canvas2.height = h;
        preparePlate();
      },
      setTheme(light2) {
        if (disposed || lightTheme === light2) return;
        lightTheme = light2;
        inkColor = "";
        inkElapsed = 100;
        preparePlate();
        prepareInk();
      },
      configure(nextSettings, palette, immediate) {
        if (disposed) return;
        settings = nextSettings;
        targetTint = settings.lyricsGeometricCoverColorsEnabled && palette ? 1 : 0;
        if (palette && targetTint) {
          for (let i = 0; i < 3; i += 1) targetRgb[i] = palette.colors[i] * 255;
        }
        if (immediate) updateTint(0, true);
        inkElapsed = 100;
      },
      draw(elapsedMs, sourceDrive) {
        if (disposed || !ambient || !atlas) return;
        const delta = Math.max(0, Math.min(100, elapsedMs));
        updateTint(delta);
        inkElapsed += delta;
        if (inkElapsed >= 100) {
          inkElapsed = 0;
          const base = lightTheme ? [38, 51, 65] : [255, 255, 255];
          const color = `rgb(${base.map((channel, i) => Math.round(channel + ((lightTheme ? tintRgb[i] * 0.48 : tintRgb[i] * 0.45 + 140) - channel) * tint)).join(",")})`;
          if (color !== inkColor) {
            inkColor = color;
            prepareInk();
          }
        }
        const response = settings.lyricsGeometricMusicResponsePercent / 100;
        const energy = sourceDrive.energy * response;
        const beat = sourceDrive.beat * response;
        seconds += delta / 1e3 * settings.lyricsGeometricSpeedPercent / 100 * (1 + energy * 0.9 + beat * 0.8);
        context.globalAlpha = 1;
        context.drawImage(plate, 0, 0);
        if (tint > 0) {
          context.globalAlpha = tint * (lightTheme ? 0.22 : 0.28);
          context.fillStyle = `rgb(${tintRgb.map(Math.round).join(",")})`;
          context.fillRect(0, 0, canvas2.width, canvas2.height);
          context.globalAlpha = 1;
        }
        const scale = Math.min(canvas2.width / sourceWidth, canvas2.height / sourceHeight);
        for (let i = 0; i < shapes.length; i += 1) {
          const [kind, cx, cy, size, angle, opacity, phase] = shapes[i];
          const time = seconds * Math.PI * 2 / (38 + i * 2.1);
          const dx = (Math.sin(time + phase) - Math.sin(phase)) * (29 + i % 3 * 11);
          const dy = (Math.cos(time * 0.81 + phase) - Math.cos(phase)) * (24 + i % 4 * 8);
          const band2 = (kind === 0 || kind === 3 ? sourceDrive.bass : kind === 1 || kind === 4 ? sourceDrive.mid : sourceDrive.treble) * response;
          const pulse = beat * (kind === 0 || kind === 3 ? 1 : 0.65);
          sprite(
            kind,
            cx / sourceWidth * canvas2.width + dx * scale,
            cy / sourceHeight * canvas2.height + dy * scale,
            size * (1 + band2 * 0.16 + pulse * 0.12) * scale,
            angle + seconds * Math.PI * 2 / (85 + i * 6) * (i % 2 ? -1 : 1),
            opacity * (kind < 3 ? 0.4 : 0.75) * (0.96 + 0.04 * Math.cos(seconds / 8 + phase)) * (1 + energy * 0.65 + band2 * 0.8 + pulse * 0.9) * settings.lyricsGeometricIntensityPercent / 100
          );
        }
        for (let i = 0; i < dust.length; i += 1) {
          const [cx, cy, size, kind] = dust[i];
          const progress = ((cy - seconds / (68 + i * 3)) % 1 + 1) % 1;
          const fade = Math.min(1, progress / 0.07, (1 - progress) / 0.07);
          sprite(
            kind,
            cx * canvas2.width + Math.sin(seconds / 11 + i) * 12 * scale,
            progress * canvas2.height,
            size * scale,
            0,
            fade * (0.15 + 0.025 * Math.sin(seconds / 8 + i)) * settings.lyricsGeometricIntensityPercent / 100
          );
        }
      },
      dispose() {
        disposed = true;
        for (const image of pendingImages) image.src = "";
        pendingImages.clear();
        ambient?.close();
        atlas?.close();
        ambient = atlas = null;
        canvas2.width = canvas2.height = plate.width = plate.height = ink.width = ink.height = 1;
      }
    };
  };

  // tools/SourceLyrics/background-entry.ts
  var renderer;
  var canvas;
  var mode;
  var light;
  var driver;
  var elapsed = 0;
  var coverRevision = 0;
  var lastTelemetry = -1;
  var lastPlaying = false;
  function build(input, w, h) {
    mode = input.mode;
    light = !input.dark;
    canvas = document.createElement("canvas");
    driver = createFlowingLightDriver();
    elapsed = 0;
    lastTelemetry = -1;
    const palette = createLyricsBackdropPalette(input.sample || null, input.dark ? "dark" : "light").flowingLight;
    if (mode === "coverFlow") {
      renderer = createCoverFlowRenderer(canvas, defaultCoverFlowPalette);
      renderer?.resize(w, h);
    } else if (mode === "flowingLight") {
      renderer = createFlowingLightRenderer(canvas, palette, input.trackKey || "");
      renderer?.resize(w, h, 1);
    } else if (mode === "geometricAmbience") {
      renderer = createGeometricAmbienceRenderer(canvas, light);
      renderer?.resize(w, h);
      renderer?.configure(defaultGeometricAmbienceSettings, palette, true);
    } else {
      renderer = createShaderSceneRenderer(canvas, mode, resolveShaderScenePalette({ palette: "cover", customColor: "#7c9cff", coverPalette: palette, mode }));
      renderer?.resize(w, h, 1);
    }
    renderer?.setTargetFrameInterval?.(1e3 / 30);
    if (!renderer) throw Error("Original background renderer unavailable: " + mode);
    return { mode };
  }
  function update(input, w, h) {
    light = !input.dark;
    const palette = createLyricsBackdropPalette(input.sample || null, input.dark ? "dark" : "light").flowingLight;
    if (mode === "coverFlow") {
      renderer.resize(w, h);
    } else if (mode === "flowingLight") {
      renderer.resize(w, h, 1);
      renderer.setPalette(palette);
      renderer.setTrackKey(input.trackKey || "");
    } else if (mode === "geometricAmbience") {
      renderer.resize(w, h);
      renderer.setTheme(light);
      renderer.configure(defaultGeometricAmbienceSettings, palette, false);
    } else {
      if (!renderer.setScene(input.mode)) throw Error("Original background scene switch failed");
      renderer.resize(w, h, 1);
      renderer.setPalette(resolveShaderScenePalette({ palette: "cover", customColor: "#7c9cff", coverPalette: palette, mode: input.mode }));
    }
    mode = input.mode;
  }
  function frameRate(fps) {
    renderer.setTargetFrameInterval?.(1e3 / fps);
  }
  function clearCover() {
    if (mode === "coverFlow") renderer.setPalette(defaultCoverFlowPalette);
    else renderer.setCover?.(null);
  }
  function draw(ms, dt, spectrum, playing, telemetryTimestamp) {
    const energy = spectrum.length ? spectrum.reduce((a, b) => a + b, 0) / spectrum.length : 0;
    if (lastTelemetry !== telemetryTimestamp || lastPlaying !== playing) {
      driver.ingest({ state: playing ? "playing" : "paused", audioLevels: { visualEnergy: energy, visualSpectrum: spectrum } }, ms);
      lastTelemetry = telemetryTimestamp;
      lastPlaying = playing;
    }
    driver.advance(dt);
    elapsed += Math.min(100, Math.max(0, dt));
    if (mode === "coverFlow") renderer.draw(elapsed / 1e3, light);
    else if (mode === "geometricAmbience") renderer.draw(dt, driver.drive);
    else renderer.draw(ms, dt, mode === "flowingLight" ? driver.drive : null);
  }
  function cover(c) {
    if (mode === "coverFlow") {
      const probe = document.createElement("canvas");
      probe.width = 64;
      probe.height = 64;
      const g = probe.getContext("2d");
      g.drawImage(c, 0, 0, 64, 64);
      const p = extractCoverFlowPalette(g.getImageData(0, 0, 64, 64).data);
      renderer.setPalette(p, coverRevision === 0);
      coverRevision++;
      probe.dispose();
    } else renderer.setCover?.(c, "cover-" + ++coverRevision);
  }
  function pointer(x, y, press) {
    renderer.pointerMove?.(x, 1 - y);
    if (press) renderer.pointerPress?.(x, 1 - y);
  }
  function pointerLeave() {
    renderer.pointerLeave?.();
  }
  function canvasId() {
    return canvas.nativeId;
  }
  function dispose() {
    renderer?.dispose();
    renderer = null;
    canvas?.dispose();
    canvas = null;
  }
  return __toCommonJS(background_entry_exports);
})();
