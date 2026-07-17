/* ============================================================
   Synergos CMS — Shop interactions (Fase 0)
   ============================================================
   Carrito usable en el front sin framework. Delegación de eventos
   en document para:
     - [data-action="add-to-cart"]  → POST /api/shop/cart/add
     - [data-action="cart-inc"]     → +1 en el quantity selector
     - [data-action="cart-dec"]     → -1 en el quantity selector
   Tras una mutación: actualiza el badge [data-cart-count] del header
   con el ItemCount de la respuesta y muestra un toast.

   Contrato del API (ShopController, [Route("api/shop/cart")]):
     POST /add    body { sku, quantity, variantSku? }  → Cart
     GET  /       → Cart  (Cart.itemCount = suma de cantidades)
   La respuesta es el record Cart serializado camelCase ⇒ cart.itemCount.
   ------------------------------------------------------------ */
(function () {
  "use strict";

  var API_BASE = "/api/shop/cart";
  var TOAST_MS = 2500;

  var prefersReducedMotion =
    window.matchMedia &&
    window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* Lee ItemCount tolerando camelCase (default ASP.NET) o PascalCase. */
  function itemCountOf(cart) {
    if (!cart) { return 0; }
    if (typeof cart.itemCount === "number") { return cart.itemCount; }
    if (typeof cart.ItemCount === "number") { return cart.ItemCount; }
    return 0;
  }

  /* ── Badge del header ──────────────────────────────────────── */
  function updateBadge(count) {
    var badges = document.querySelectorAll("[data-cart-count]");
    for (var i = 0; i < badges.length; i++) {
      badges[i].textContent = String(count);
      badges[i].classList.toggle("syn-cart-badge--empty", count === 0);
    }
    // Mantener el aria-label del botón en sync con la cantidad.
    var buttons = document.querySelectorAll("[data-cart-button]");
    for (var j = 0; j < buttons.length; j++) {
      buttons[j].setAttribute("aria-label", "Carrito (" + count + ")");
    }
  }

  /* ── Toast efímero (surface + slide-in, auto-dismiss) ──────── */
  function showToast(message) {
    var toast = document.createElement("div");
    toast.className = "syn-toast";
    toast.setAttribute("role", "status");
    toast.setAttribute("aria-live", "polite");
    toast.textContent = message;
    document.body.appendChild(toast);

    // Forzar reflow para que la transición de entrada corra.
    // (no-op read; el navegador aplica el estado inicial antes de la clase)
    void toast.offsetWidth;
    toast.classList.add("syn-toast--visible");

    var remove = function () {
      if (!toast.parentNode) { return; }
      toast.classList.remove("syn-toast--visible");
      if (prefersReducedMotion) {
        toast.parentNode.removeChild(toast);
      } else {
        var done = function () {
          if (toast.parentNode) { toast.parentNode.removeChild(toast); }
          toast.removeEventListener("transitionend", done);
        };
        toast.addEventListener("transitionend", done);
        // Fallback por si transitionend no dispara.
        setTimeout(done, 400);
      }
    };
    setTimeout(remove, TOAST_MS);
  }

  /* ── Fetch helper ──────────────────────────────────────────── */
  function postCart(path, body) {
    return fetch(API_BASE + path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "same-origin",
      body: JSON.stringify(body),
    }).then(function (res) {
      if (!res.ok) { throw new Error("cart request failed: " + res.status); }
      return res.json();
    });
  }

  /* ── Quantity selector (si el PDP lo tiene) ────────────────── */
  function qtyInputFor(el) {
    var scope = el.closest("[data-cart-qty], .syn-qty-selector, .syn-product");
    if (!scope) { return null; }
    return scope.querySelector(
      ".syn-qty-selector__input, input[data-cart-qty-input], input[type='number']"
    );
  }

  function readQuantity(trigger) {
    var input = trigger ? qtyInputFor(trigger) : null;
    if (input) {
      var n = parseInt(input.value, 10);
      if (!isNaN(n) && n > 0) { return n; }
    }
    return 1;
  }

  function clampQty(input, delta) {
    var n = parseInt(input.value, 10);
    if (isNaN(n)) { n = 1; }
    n = Math.max(1, n + delta);
    input.value = String(n);
    input.dispatchEvent(new Event("change", { bubbles: true }));
  }

  /* ── Delegación de eventos ─────────────────────────────────── */
  document.addEventListener("click", function (e) {
    var addBtn = e.target.closest('[data-action="add-to-cart"]');
    if (addBtn) {
      e.preventDefault();
      var sku = addBtn.getAttribute("data-sku");
      if (!sku) {
        // Fallback: el contenedor del producto lleva data-sku.
        var host = addBtn.closest("[data-sku]");
        sku = host ? host.getAttribute("data-sku") : null;
      }
      if (!sku) { return; }

      var variantSku = addBtn.getAttribute("data-variant-sku") || null;
      var quantity = readQuantity(addBtn);

      addBtn.setAttribute("aria-busy", "true");
      addBtn.disabled = true;

      postCart("/add", { sku: sku, quantity: quantity, variantSku: variantSku })
        .then(function (cart) {
          updateBadge(itemCountOf(cart));
          showToast("Agregado al carrito");
        })
        .catch(function () {
          showToast("No se pudo agregar. Intentá de nuevo.");
        })
        .then(function () {
          addBtn.removeAttribute("aria-busy");
          addBtn.disabled = false;
        });
      return;
    }

    // Quantity +/- (solo UI local; la cantidad se envía al add-to-cart).
    var incBtn = e.target.closest('[data-action="cart-inc"]');
    var decBtn = e.target.closest('[data-action="cart-dec"]');
    if (incBtn || decBtn) {
      var input = qtyInputFor(incBtn || decBtn);
      if (input) {
        e.preventDefault();
        clampQty(input, incBtn ? 1 : -1);
      }
      return;
    }

    // Galería PDP: click en un thumbnail [data-thumb] cambia el src de
    // la imagen principal [data-main-image] y marca el thumb activo.
    var thumb = e.target.closest("[data-thumb]");
    if (thumb) {
      var gallery = thumb.closest(".syn-product__gallery");
      if (!gallery) { return; }
      var main = gallery.querySelector("[data-main-image]");
      var src = thumb.getAttribute("data-thumb-src");
      if (!main || !src) { return; }
      e.preventDefault();
      main.setAttribute("src", src);
      var thumbs = gallery.querySelectorAll("[data-thumb]");
      for (var k = 0; k < thumbs.length; k++) {
        var isActive = thumbs[k] === thumb;
        thumbs[k].classList.toggle("syn-product__thumb--active", isActive);
        thumbs[k].setAttribute("aria-pressed", isActive ? "true" : "false");
      }
    }
  });
})();
