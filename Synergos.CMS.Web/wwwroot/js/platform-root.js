/* =============================================================================
   platform-root.js — realces del entry point "boot/launcher" (estilo PS3).
   Progressive enhancement: la página funciona sin esto. Sin dependencias.

   El chrome animado YA NO vive en el Razor — este script lo INYECTA en cualquier
   elemento .syn-launcher (onda, ribbon, partículas, reloj, power-on flash), de
   modo que aplicar la clase = launcher completo. Además: parallax con el mouse,
   reloj estilo XMB, y sonido (chime al primer gesto + tick al hover de cada card).
   Respeta prefers-reduced-motion.
   ============================================================================= */
(function () {
  'use strict';

  var launchers = document.querySelectorAll('.syn-launcher');
  if (!launchers.length) { return; }

  var mq = window.matchMedia ? window.matchMedia.bind(window) : null;
  var reduceMotion = mq && mq('(prefers-reduced-motion: reduce)').matches;
  var ribbonSeq = 0;

  var RIBBON_PATH = 'M0 120 C120 60 240 60 360 120 C480 180 600 180 720 120 C840 60 960 60 1080 120 C1200 180 1320 180 1440 120 C1560 60 1680 60 1800 120 C1920 180 2040 180 2160 120 C2280 60 2400 60 2520 120 C2640 180 2760 180 2880 120';

  function el(html) {
    var t = document.createElement('template');
    t.innerHTML = html.trim();
    return t.content.firstElementChild;
  }

  /* ── Chrome animado inyectado (no se puede producir solo con una clase) ──── */
  function injectChrome(root) {
    if (!document.querySelector('.syn-launcher__flash')) {
      document.body.appendChild(el('<div class="syn-launcher__flash" aria-hidden="true"></div>'));
    }
    root.insertBefore(el('<div class="syn-launcher__wave" aria-hidden="true"><span></span><span></span><span></span></div>'), root.firstChild);

    var id = 'synPrRibbon-' + (++ribbonSeq);
    var ribbon = el(
      '<div class="syn-launcher__ribbon" aria-hidden="true">' +
        '<svg viewBox="0 0 2880 240" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">' +
          '<defs><linearGradient id="' + id + '" x1="0" y1="0" x2="1" y2="0">' +
            '<stop class="syn-pr-ribbon-s0" offset="0"></stop>' +
            '<stop class="syn-pr-ribbon-s1" offset="0.32"></stop>' +
            '<stop class="syn-pr-ribbon-s2" offset="0.62"></stop>' +
            '<stop class="syn-pr-ribbon-s3" offset="1"></stop>' +
          '</linearGradient></defs>' +
          '<path d="' + RIBBON_PATH + '" fill="none" stroke="url(#' + id + ')" stroke-width="2.5" stroke-linecap="round"></path>' +
        '</svg>' +
      '</div>'
    );
    root.insertBefore(ribbon, root.firstChild);

    var stars = '<div class="syn-launcher__stars" aria-hidden="true">';
    for (var i = 0; i < 12; i++) { stars += '<span></span>'; }
    stars += '</div>';
    root.insertBefore(el(stars), root.firstChild);
  }

  /* ── Reloj estilo XMB ───────────────────────────────────────────────────── */
  function injectClock(root) {
    var clock = el('<div class="syn-launcher__clock" aria-hidden="true"><span class="syn-launcher__clock-time">--:--</span><span class="syn-launcher__clock-date"></span></div>');
    root.appendChild(clock);
    var timeEl = clock.querySelector('.syn-launcher__clock-time');
    var dateEl = clock.querySelector('.syn-launcher__clock-date');
    var pad = function (n) { return (n < 10 ? '0' : '') + n; };
    var update = function () {
      var now = new Date();
      timeEl.textContent = pad(now.getHours()) + ':' + pad(now.getMinutes());
      try { dateEl.textContent = now.toLocaleDateString('es', { weekday: 'long', day: 'numeric', month: 'long' }); }
      catch (e) { dateEl.textContent = now.toLocaleDateString(); }
    };
    update();
    setInterval(update, 15000);
  }

  /* ── Parallax: la onda/ribbon/estrellas se inclinan hacia el cursor ─────── */
  function initParallax(root) {
    var tx = 0, ty = 0, cx = 0, cy = 0, raf = null;
    var tick = function () {
      cx += (tx - cx) * 0.08; cy += (ty - cy) * 0.08;
      root.style.setProperty('--syn-pr-mx', cx.toFixed(3));
      root.style.setProperty('--syn-pr-my', cy.toFixed(3));
      if (Math.abs(tx - cx) > 0.001 || Math.abs(ty - cy) > 0.001) { raf = requestAnimationFrame(tick); } else { raf = null; }
    };
    window.addEventListener('mousemove', function (e) {
      var w = window.innerWidth || 1, h = window.innerHeight || 1;
      tx = (e.clientX / w) * 2 - 1; ty = (e.clientY / h) * 2 - 1;
      if (!raf) { raf = requestAnimationFrame(tick); }
    }, { passive: true });
  }

  /* ── Sonido Web Audio (solo tras interacción; sin assets) ────────────────── */
  function initAudio(root) {
    var AudioCtx = window.AudioContext || window.webkitAudioContext;
    if (!AudioCtx) { return; }
    var actx = null, started = false;
    var ensure = function () { if (!actx) { actx = new AudioCtx(); } if (actx.state === 'suspended') { actx.resume(); } return actx; };
    var tone = function (freq, dur, when, gain, type) {
      var ctx = ensure(); var t0 = ctx.currentTime + (when || 0);
      var osc = ctx.createOscillator(), g = ctx.createGain();
      osc.type = type || 'sine'; osc.frequency.value = freq;
      g.gain.setValueAtTime(0.0001, t0);
      g.gain.exponentialRampToValueAtTime(gain, t0 + 0.02);
      g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
      osc.connect(g); g.connect(ctx.destination); osc.start(t0); osc.stop(t0 + dur + 0.05);
    };
    var chime = function () { tone(523.25, 0.9, 0, 0.06, 'sine'); tone(659.25, 0.9, 0.08, 0.05, 'sine'); tone(783.99, 1.1, 0.16, 0.05, 'sine'); };
    var tick = function () { tone(880, 0.12, 0, 0.03, 'triangle'); };
    var first = function () {
      if (started) { return; } started = true; ensure(); chime();
      window.removeEventListener('pointerdown', first); window.removeEventListener('keydown', first);
    };
    window.addEventListener('pointerdown', first);
    window.addEventListener('keydown', first);
    var cards = root.querySelectorAll('.syn-launcher__card');
    for (var i = 0; i < cards.length; i++) {
      cards[i].addEventListener('pointerenter', function () { if (started) { tick(); } }, { passive: true });
    }
  }

  for (var i = 0; i < launchers.length; i++) {
    var root = launchers[i];
    if (!reduceMotion) { injectChrome(root); initParallax(root); }
    injectClock(root);
    initAudio(root);
  }
})();
