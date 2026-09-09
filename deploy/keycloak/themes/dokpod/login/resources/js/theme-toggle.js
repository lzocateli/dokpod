(function () {
    "use strict";

    var STORAGE_KEY = "dokpod-theme";
    var root = document.documentElement;

    function readStoredTheme() {
        try {
            return window.localStorage.getItem(STORAGE_KEY);
        } catch (error) {
            return null;
        }
    }

    function persistTheme(theme) {
        try {
            window.localStorage.setItem(STORAGE_KEY, theme);
        } catch (error) {
            // Web Storage indisponível; a escolha vale só para esta página.
        }
    }

    function applyTheme(theme) {
        if (theme === "light" || theme === "dark") {
            root.setAttribute("data-dokpod-theme", theme);
        } else {
            root.removeAttribute("data-dokpod-theme");
        }
    }

    function effectiveTheme() {
        var stored = readStoredTheme();
        if (stored === "light" || stored === "dark") {
            return stored;
        }
        var prefersDark = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
        return prefersDark ? "dark" : "light";
    }

    function updateToggle(button, theme) {
        var nextThemeLabel = theme === "dark" ? "claro" : "escuro";
        button.textContent = theme === "dark" ? "Tema claro" : "Tema escuro";
        button.setAttribute("aria-label", "Ativar tema " + nextThemeLabel);
        button.setAttribute("aria-pressed", theme === "dark" ? "true" : "false");
    }

    function createToggle() {
        var button = document.createElement("button");
        button.type = "button";
        button.className = "dokpod-theme-toggle";
        updateToggle(button, effectiveTheme());

        button.addEventListener("click", function () {
            var next = effectiveTheme() === "dark" ? "light" : "dark";
            applyTheme(next);
            persistTheme(next);
            updateToggle(button, next);
        });

        return button;
    }

    function insertToggle() {
        var container = document.querySelector(".pf-v5-c-login__container");
        var header = document.getElementById("kc-header");
        var anchor = header || container;
        if (!anchor || !anchor.parentNode) {
            return;
        }
        anchor.parentNode.insertBefore(createToggle(), anchor);
    }

    var stored = readStoredTheme();
    if (stored === "light" || stored === "dark") {
        applyTheme(stored);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", insertToggle);
    } else {
        insertToggle();
    }
})();