/* ============================================================
   Synergos CMS — Reading experience (Ola 230, ADR 0100)
   ============================================================
   Post-scoped progressive enhancement. Carga sólo en PostPage.
   Tres comportamientos, todos no-op si su contenedor no existe:
     (a) Barra de progreso de lectura (.syn-reading-progress__bar)
         que se llena con el % de scroll del artículo.
     (b) Tabla de contenidos auto-construida desde los h2/h3 de
         .syn-post__body, inyectada en <nav.syn-post__toc>, con
         scroll-spy que resalta la sección activa.
     (c) Toggle de los forms de "responder" del hilo de comentarios.
   Respeta prefers-reduced-motion (smooth-scroll opt-out).
   IIFE, sin dependencias, sin globals.
   ------------------------------------------------------------ */
(function () {
  'use strict';

  function ready(fn) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', fn, { once: true });
    } else {
      fn();
    }
  }

  var reduceMotion = window.matchMedia &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ── slug helper (es-CO friendly): NFKD + strip diacritics ── */
  function slugify(text) {
    // NFKD descompone los acentos (á → a + marca combinante); el strip de
    // [^a-z0-9\s-] elimina las marcas combinantes (U+0300–036F) y demás
    // símbolos, así que no hace falta una pasada explícita de diacríticos.
    return (text || '')
      .toString()
      .normalize('NFKD')
      .toLowerCase()
      .trim()
      .replace(/[^a-z0-9\s-]/g, '')
      .replace(/\s+/g, '-')
      .replace(/-+/g, '-')
      .replace(/^-|-$/g, '') || 'section';
  }

  /* ── (a) Reading progress bar ──────────────────────────────── */
  function initProgress(article) {
    var bar = document.querySelector('.syn-reading-progress__bar');
    if (!bar || !article) { return; }

    var ticking = false;
    function update() {
      ticking = false;
      var rect = article.getBoundingClientRect();
      var vh = window.innerHeight || document.documentElement.clientHeight;
      var total = rect.height - vh;
      // % leído del artículo: 0 antes de entrar, 100 al final.
      var scrolled = total > 0 ? (-rect.top) / total : 1;
      var pct = Math.max(0, Math.min(1, scrolled)) * 100;
      bar.style.width = pct.toFixed(2) + '%';
      bar.setAttribute('aria-valuenow', Math.round(pct).toString());
    }
    function onScroll() {
      if (!ticking) {
        ticking = true;
        window.requestAnimationFrame(update);
      }
    }
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll, { passive: true });
    update();
  }

  /* ── (b) Table of contents + scroll-spy ────────────────────── */
  function initToc(article) {
    var nav = document.querySelector('.syn-post__toc');
    if (!nav || !article) { return; }

    var headings = Array.prototype.slice.call(
      article.querySelectorAll('h2, h3'));
    if (headings.length === 0) {
      nav.hidden = true;
      return;
    }

    var used = {};
    var list = document.createElement('ol');
    list.className = 'syn-post__toc-list';

    var links = [];
    headings.forEach(function (h) {
      if (!h.id) {
        var base = slugify(h.textContent);
        var id = base;
        var n = 2;
        while (used[id] || document.getElementById(id)) {
          id = base + '-' + n++;
        }
        h.id = id;
      }
      used[h.id] = true;

      var li = document.createElement('li');
      li.className = 'syn-post__toc-item syn-post__toc-item--' +
        h.tagName.toLowerCase();
      var a = document.createElement('a');
      a.className = 'syn-post__toc-link';
      a.href = '#' + h.id;
      a.textContent = h.textContent;
      a.addEventListener('click', function (e) {
        e.preventDefault();
        h.scrollIntoView({
          behavior: reduceMotion ? 'auto' : 'smooth',
          block: 'start',
        });
        history.replaceState(null, '', '#' + h.id);
      });
      li.appendChild(a);
      list.appendChild(li);
      links.push({ heading: h, link: a });
    });

    nav.appendChild(list);

    /* scroll-spy via IntersectionObserver (fallback: scroll handler). */
    function setActive(id) {
      links.forEach(function (entry) {
        var on = entry.heading.id === id;
        entry.link.classList.toggle('is-active', on);
        if (on) { entry.link.setAttribute('aria-current', 'true'); }
        else { entry.link.removeAttribute('aria-current'); }
      });
    }

    if ('IntersectionObserver' in window) {
      var visible = {};
      var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (en) {
          visible[en.target.id] = en.isIntersecting;
        });
        // El primer heading actualmente visible es la sección activa.
        for (var i = 0; i < links.length; i++) {
          if (visible[links[i].heading.id]) {
            setActive(links[i].heading.id);
            return;
          }
        }
      }, { rootMargin: '-10% 0px -70% 0px', threshold: 0 });
      headings.forEach(function (h) { observer.observe(h); });
    } else {
      var ticking = false;
      window.addEventListener('scroll', function () {
        if (ticking) { return; }
        ticking = true;
        window.requestAnimationFrame(function () {
          ticking = false;
          var current = headings[0];
          for (var i = 0; i < headings.length; i++) {
            if (headings[i].getBoundingClientRect().top <= 120) {
              current = headings[i];
            }
          }
          setActive(current.id);
        });
      }, { passive: true });
    }
    setActive(headings[0].id);
  }

  /* ── (c) Comment reply-form toggles ────────────────────────── */
  function initReplyToggles() {
    var toggles = document.querySelectorAll('.syn-comment-thread__reply-toggle');
    Array.prototype.forEach.call(toggles, function (btn) {
      btn.addEventListener('click', function () {
        var targetId = btn.getAttribute('data-reply-target');
        var form = targetId && document.getElementById(targetId);
        if (!form) { return; }
        var willOpen = form.hidden;
        form.hidden = !willOpen;
        btn.setAttribute('aria-expanded', willOpen ? 'true' : 'false');
        if (willOpen) {
          var ta = form.querySelector('textarea');
          if (ta) { ta.focus(); }
        }
      });
    });
  }

  ready(function () {
    var article = document.querySelector('.syn-post__body') ||
      document.querySelector('.syn-post');
    initProgress(document.querySelector('.syn-post') || article);
    initToc(article);
    initReplyToggles();
  });
})();
