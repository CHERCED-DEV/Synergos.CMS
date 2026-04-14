(function () {
    "use strict";

    function stripHtml(value) {
        if (!value) {
            return "";
        }

        var source = String(value);
        if (source.indexOf("<") === -1 && source.indexOf("&") === -1) {
            return source.replace(/\s+/g, " ").trim();
        }

        var node = document.createElement("div");
        node.innerHTML = source;
        return (node.textContent || node.innerText || "").replace(/\s+/g, " ").trim();
    }

    function readText(value, depth, seen) {
        if (value == null || depth > 4) {
            return "";
        }

        var valueType = typeof value;
        if (valueType === "string") {
            return stripHtml(value);
        }

        if (valueType === "number" || valueType === "boolean") {
            return String(value);
        }

        if (Array.isArray(value)) {
            for (var i = 0; i < value.length; i++) {
                var arrayText = readText(value[i], depth + 1, seen);
                if (arrayText) {
                    return arrayText;
                }
            }

            return "";
        }

        if (valueType !== "object") {
            return "";
        }

        if (seen.indexOf(value) >= 0) {
            return "";
        }

        seen.push(value);

        var preferredKeys = [
            "markup",
            "text",
            "headingText",
            "title",
            "subtitle",
            "description",
            "summary",
            "caption",
            "altText",
            "label",
            "name",
            "question",
            "answer",
            "value",
            "url",
            "link",
            "body"
        ];

        for (var j = 0; j < preferredKeys.length; j++) {
            var preferredText = readText(value[preferredKeys[j]], depth + 1, seen);
            if (preferredText) {
                return preferredText;
            }
        }

        var keys = Object.keys(value);
        for (var k = 0; k < keys.length; k++) {
            var text = readText(value[keys[k]], depth + 1, seen);
            if (text) {
                return text;
            }
        }

        return "";
    }

    function readUrl(value, depth, seen) {
        if (value == null || depth > 4) {
            return "";
        }

        if (typeof value === "string") {
            return stripHtml(value);
        }

        if (Array.isArray(value)) {
            for (var i = 0; i < value.length; i++) {
                var arrayUrl = readUrl(value[i], depth + 1, seen);
                if (arrayUrl) {
                    return arrayUrl;
                }
            }

            return "";
        }

        if (typeof value !== "object") {
            return "";
        }

        if (seen.indexOf(value) >= 0) {
            return "";
        }

        seen.push(value);

        var urlKeys = [
            "url",
            "href",
            "src",
            "embedUrl",
            "scriptUrl",
            "videoUrl",
            "link",
            "ctaLink"
        ];

        for (var j = 0; j < urlKeys.length; j++) {
            var nestedUrl = readUrl(value[urlKeys[j]], depth + 1, seen);
            if (nestedUrl) {
                return nestedUrl;
            }
        }

        return "";
    }

    function countItems(value) {
        if (!value) {
            return 0;
        }

        if (Array.isArray(value)) {
            return value.length;
        }

        if (typeof value !== "object") {
            return 0;
        }

        if (Array.isArray(value.contentData)) {
            return value.contentData.length;
        }

        if (Array.isArray(value.items)) {
            return value.items.length;
        }

        if (Array.isArray(value.blocks)) {
            return value.blocks.length;
        }

        return 0;
    }

    function toPreviewHost(value) {
        var raw = readUrl(value, 0, []);
        if (!raw) {
            return "";
        }

        var source = raw.trim();
        if (!source) {
            return "";
        }

        if (source.charAt(0) === "/") {
            return source;
        }

        var normalized = source;
        if (!/^[a-z][a-z0-9+.-]*:\/\//i.test(normalized) && normalized.indexOf("//") !== 0) {
            normalized = "https://" + normalized.replace(/^\/+/, "");
        }

        try {
            var parsed = new URL(normalized, window.location.origin);
            var path = parsed.pathname && parsed.pathname !== "/" ? parsed.pathname : "";
            return (parsed.host || "").replace(/^www\./i, "") + path;
        } catch (error) {
            return source;
        }
    }

    /** Path del stylesheet del plugin. Declarado aquí para que coincida con package.manifest. */
    var CSS_HREF = "/App_Plugins/LayoutComposer/styles/layout-composer.css";

    angular
        .module("umbraco")
        .run(["$document", function ($document) {
            if (!$document[0].querySelector("link[href=\"" + CSS_HREF + "\"]")) {
                var link = $document[0].createElement("link");
                link.rel = "stylesheet";
                link.type = "text/css";
                link.href = CSS_HREF;
                $document[0].head.appendChild(link);
            }
        }])
        /**
         * @filter sgPreviewText
         * @api LayoutComposer v1.0
         * Extrae texto legible de cualquier valor — string, array u objeto con campos de texto.
         * La búsqueda sigue una lista de prioridad de claves (markup, text, headingText, title…).
         * @param {*}      value     Valor a extraer — cualquier tipo
         * @param {string} fallback  Texto si value no produce resultado
         * @param {number} maxLength Truncar en N caracteres con "..."
         */
        .filter("sgPreviewText", function () {
            return function (value, fallback, maxLength) {
                var text = readText(value, 0, []) || fallback || "";
                var limit = parseInt(maxLength, 10);

                if (!isNaN(limit) && limit > 0 && text.length > limit) {
                    return text.substring(0, limit - 3).trim() + "...";
                }

                return text;
            };
        })
        /**
         * @filter sgPreviewCount
         * @api LayoutComposer v1.0
         * Cuenta ítems en un array, contentData, items o blocks y devuelve una etiqueta
         * con el número y nombre del ítem en singular o plural.
         * @param {*}      value    Array u objeto con propiedad de colección
         * @param {string} singular Nombre en singular (default: "item")
         * @param {string} plural   Nombre en plural (default: singular + "s")
         */
        .filter("sgPreviewCount", function () {
            return function (value, singular, plural) {
                var count = countItems(value);
                if (!count) {
                    return "";
                }

                var one = singular || "item";
                var many = plural || (one + "s");
                return count + " " + (count === 1 ? one : many);
            };
        })
        /**
         * @filter sgPreviewHost
         * @api LayoutComposer v1.0
         * Extrae el host normalizado de una URL — elimina "www.", conserva pathname si existe.
         * Acepta string, objeto con propiedades url/href/src/embedUrl o array de los anteriores.
         * @param {*}      value    URL o valor que contenga una URL
         * @param {string} fallback Texto si value no produce resultado
         */
        .filter("sgPreviewHost", function () {
            return function (value, fallback) {
                return toPreviewHost(value) || fallback || "";
            };
        });
})();
