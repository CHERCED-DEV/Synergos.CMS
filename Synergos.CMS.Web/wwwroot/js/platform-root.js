/* =============================================================================
   platform-root.js — realces del entry point "boot/launcher" (estilo PS3).
   Progressive enhancement: la página funciona sin esto. Sin dependencias.
   - Parallax: el glow/onda/ribbon se inclinan hacia el cursor (solo pointer fino,
     respeta prefers-reduced-motion).
   - Reloj estilo XMB (hora + fecha, locale es).
   - Sonido: chime de arranque en la 1ra interacción (autoplay policy) + tick
     suave al hover de cada dominio. Web Audio sintetizado (sin assets).
   ============================================================================= */
(function () {
  'use strict';

  var root = document.querySelector('.syn-platform-root');
  if (!root) { return; }

  var mq = window.matchMedia ? window.matchMedia.bind(window) : null;
  var reduceMotion = mq && mq('(prefers-reduced-motion: reduce)').matches;

  /* ── Parallax ──────────────────────────────────────────────────────────────
     Sin gate de pointer:fine — el listener mousemove es inerte en touch
     (nunca dispara), así que es seguro adjuntarlo siempre. Solo se omite si el
     usuario pidió reduce-motion. */
  if (!reduceMotion) {
    var targetX = 0, targetY = 0, curX = 0, curY = 0, raf = null;

    var tick = function () {
      curX += (targetX - curX) * 0.08;
      curY += (targetY - curY) * 0.08;
      root.style.setProperty('--syn-pr-mx', curX.toFixed(3));
      root.style.setProperty('--syn-pr-my', curY.toFixed(3));
      if (Math.abs(targetX - curX) > 0.001 || Math.abs(targetY - curY) > 0.001) {
        raf = requestAnimationFrame(tick);
      } else {
        raf = null;
      }
    };

    window.addEventListener('mousemove', function (e) {
      var w = window.innerWidth || 1, h = window.innerHeight || 1;
      targetX = (e.clientX / w) * 2 - 1;   // -1 .. 1
      targetY = (e.clientY / h) * 2 - 1;
      if (!raf) { raf = requestAnimationFrame(tick); }
    }, { passive: true });
  }

  /* ── Reloj XMB ─────────────────────────────────────────────────────────── */
  var clockTime = root.querySelector('.syn-platform-root__clock-time');
  var clockDate = root.querySelector('.syn-platform-root__clock-date');
  if (clockTime) {
    var pad = function (n) { return (n < 10 ? '0' : '') + n; };
    var updateClock = function () {
      var now = new Date();
      clockTime.textContent = pad(now.getHours()) + ':' + pad(now.getMinutes());
      if (clockDate) {
        try {
          clockDate.textContent = now.toLocaleDateString('es', {
            weekday: 'long', day: 'numeric', month: 'long'
          });
        } catch (e) {
          clockDate.textContent = now.toLocaleDateString();
        }
      }
    };
    updateClock();
    setInterval(updateClock, 15000);
  }

  /* ── Sonido (Web Audio sintetizado, solo tras interacción) ────────────────── */
  var AudioCtx = window.AudioContext || window.webkitAudioContext;
  if (AudioCtx) {
    var actx = null, started = false;

    var ensureCtx = function () {
      if (!actx) { actx = new AudioCtx(); }
      if (actx.state === 'suspended') { actx.resume(); }
      return actx;
    };

    var tone = function (freq, dur, when, gainPeak, type) {
      var ctx = ensureCtx();
      var t0 = ctx.currentTime + (when || 0);
      var osc = ctx.createOscillator();
      var g = ctx.createGain();
      osc.type = type || 'sine';
      osc.frequency.value = freq;
      g.gain.setValueAtTime(0.0001, t0);
      g.gain.exponentialRampToValueAtTime(gainPeak, t0 + 0.02);
      g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
      osc.connect(g); g.connect(ctx.destination);
      osc.start(t0); osc.stop(t0 + dur + 0.05);
    };

    var powerChime = function () {
      tone(523.25, 0.9, 0.00, 0.06, 'sine');  // C5
      tone(659.25, 0.9, 0.08, 0.05, 'sine');  // E5
      tone(783.99, 1.1, 0.16, 0.05, 'sine');  // G5
    };

    var hoverTick = function () { tone(880, 0.12, 0, 0.03, 'triangle'); };

    var onFirst = function () {
      if (started) { return; }
      started = true;
      ensureCtx();
      powerChime();
      window.removeEventListener('pointerdown', onFirst);
      window.removeEventListener('keydown', onFirst);
    };
    window.addEventListener('pointerdown', onFirst);
    window.addEventListener('keydown', onFirst);

    var domains = root.querySelectorAll('.syn-platform-root__domain');
    for (var i = 0; i < domains.length; i++) {
      domains[i].addEventListener('pointerenter', function () {
        if (started) { hoverTick(); }   // autoplay policy: solo tras la 1ra interacción
      }, { passive: true });
    }
  }
})();
