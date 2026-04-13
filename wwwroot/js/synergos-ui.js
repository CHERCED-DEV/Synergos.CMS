/**
 * Synergos UI — Runtime interactions
 * Mobile menu · Scroll shadow · Scroll-triggered animations · Dropdowns
 */

(function () {
  'use strict';

  // ── Constants ──────────────────────────────────────────────
  var SCROLL_THRESHOLD = 8;
  var MOBILE_BP = 768;

  // ── Mobile Nav ────────────────────────────────────────────
  var header   = document.querySelector('.layout-header');
  var toggle   = document.getElementById('nav-toggle');
  var mobileNav = document.getElementById('mobile-nav');
  var mobileOverlay = document.getElementById('mobile-nav-overlay');
  var mobileClose = document.getElementById('mobile-nav-close');

  function openMenu() {
    if (!mobileNav) return;
    mobileNav.classList.add('is-open');
    if (mobileOverlay) mobileOverlay.classList.add('is-visible');
    document.body.classList.add('nav-is-open');
    if (toggle) toggle.setAttribute('aria-expanded', 'true');
    mobileNav.querySelector('a, button') && mobileNav.querySelector('a, button').focus();
  }

  function closeMenu() {
    if (!mobileNav) return;
    mobileNav.classList.remove('is-open');
    if (mobileOverlay) mobileOverlay.classList.remove('is-visible');
    document.body.classList.remove('nav-is-open');
    if (toggle) toggle.setAttribute('aria-expanded', 'false');
    if (toggle) toggle.focus();
  }

  if (toggle)        toggle.addEventListener('click', openMenu);
  if (mobileClose)   mobileClose.addEventListener('click', closeMenu);
  if (mobileOverlay) mobileOverlay.addEventListener('click', closeMenu);

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') closeMenu();
  });

  // ── Scroll Shadow on Header ───────────────────────────────
  function updateScrollState() {
    if (!header) return;
    if (window.scrollY > SCROLL_THRESHOLD) {
      header.classList.add('is-scrolled');
    } else {
      header.classList.remove('is-scrolled');
    }
  }

  window.addEventListener('scroll', updateScrollState, { passive: true });
  updateScrollState();

  // ── Close mobile nav on resize to desktop ─────────────────
  window.addEventListener('resize', function () {
    if (window.innerWidth >= MOBILE_BP) closeMenu();
  });

  // ── Scroll-triggered Fade-in Animations ───────────────────
  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          io.unobserve(entry.target);
        }
      });
    }, { threshold: 0.12 });

    document.querySelectorAll('.sg-anim').forEach(function (el) {
      io.observe(el);
    });
  } else {
    // Fallback: show all without animation
    document.querySelectorAll('.sg-anim').forEach(function (el) {
      el.classList.add('is-visible');
    });
  }

  // ── Dropdown hover — accessible on focus as well ──────────
  document.querySelectorAll('.layout-nav__item--has-children').forEach(function (item) {
    var link = item.querySelector('.layout-nav__link');
    function setExpanded(isOpen) {
      if (link) link.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
    }

    if (link) {
      link.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          setExpanded(item.classList.toggle('is-open'));
        }
        if (e.key === 'Escape') {
          item.classList.remove('is-open');
          setExpanded(false);
        }
      });
    }
  });

  // ── Accordion (corporate block) ────────────────────────────
  document.addEventListener('click', function (e) {
    var trigger = e.target.closest('.sg-accordion__trigger');
    if (!trigger) return;
    var item = trigger.closest('.sg-accordion__item');
    if (!item) return;
    var isOpen = item.classList.contains('is-open');
    // close siblings
    item.closest('.sg-accordion').querySelectorAll('.sg-accordion__item.is-open').forEach(function (el) {
      el.classList.remove('is-open');
    });
    if (!isOpen) item.classList.add('is-open');
  });

  // ── Tab Group ──────────────────────────────────────────────
  document.querySelectorAll('.sg-tab-group').forEach(function (group) {
    var tabs   = group.querySelectorAll('.sg-tab-group__tab');
    var panels = group.querySelectorAll('.sg-tab-group__panel');

    tabs.forEach(function (tab, i) {
      tab.addEventListener('click', function () {
        tabs.forEach(function (t) { t.setAttribute('aria-selected', 'false'); t.classList.remove('is-active'); });
        panels.forEach(function (p) { p.classList.remove('is-active'); });
        tab.setAttribute('aria-selected', 'true');
        tab.classList.add('is-active');
        if (panels[i]) panels[i].classList.add('is-active');
      });
    });

    // Activate first tab by default if none selected
    if (tabs.length && !group.querySelector('[aria-selected="true"]')) {
      tabs[0].click();
    }
  });

  // ── Banner Slider ──────────────────────────────────────────
  document.querySelectorAll('.sg-banner-slider').forEach(function (slider) {
    var track  = slider.querySelector('.sg-banner-slider__track');
    var slides = slider.querySelectorAll('.sg-banner-slider__slide');
    var dots   = slider.querySelectorAll('.sg-banner-slider__dot');
    if (!track || slides.length < 2) return;

    var current = 0;

    function goTo(index) {
      current = (index + slides.length) % slides.length;
      track.style.transform = 'translateX(-' + (current * 100) + '%)';
      dots.forEach(function (d, i) { d.classList.toggle('is-active', i === current); });
    }

    dots.forEach(function (dot, i) { dot.addEventListener('click', function () { goTo(i); }); });

    // Auto-advance
    var timer = setInterval(function () { goTo(current + 1); }, 5000);
    slider.addEventListener('mouseenter', function () { clearInterval(timer); });
    slider.addEventListener('mouseleave', function () { timer = setInterval(function () { goTo(current + 1); }, 5000); });
  });

})();
