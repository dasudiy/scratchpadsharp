(function () {
    function dumpRoot() {
        return document.getElementById("dump");
    }

    function dumpPane() {
        return document.getElementById("dump-pane");
    }

    function textPane() {
        return document.getElementById("text-pane");
    }

    function tableCollapseTarget(table) {
        return table.querySelector(":scope > thead > tr.table-info-header > th")
            || table.querySelector(":scope > thead > tr > th");
    }

    function setCollapsed(table, collapsed) {
        table.classList.toggle("collapsed", collapsed);
        var caret = tableCollapseTarget(table)?.querySelector(".caret-up-icon, .caret-down-icon");
        if (!caret) return;
        caret.classList.toggle("caret-up-icon", !collapsed);
        caret.classList.toggle("caret-down-icon", collapsed);
    }

    function wireTable(table) {
        table.classList.add("table", "table-sm", "table-bordered");
        var target = tableCollapseTarget(table);
        if (!target || target.dataset.collapseWired) return;

        target.dataset.collapseWired = "1";
        target.classList.add("collapse-actionable");
        if (!target.querySelector(".caret-up-icon, .caret-down-icon")) {
            var caret = document.createElement("i");
            caret.className = "caret-up-icon";
            target.insertBefore(caret, target.firstChild);
        }
    }

    function afterInsert(root) {
        root.querySelectorAll("table").forEach(wireTable);
        root.querySelectorAll("code[language]").forEach(function (el) {
            var lang = el.getAttribute("language");
            if (lang) el.classList.add("language-" + lang);
        });
        if (window.hljs) {
            root.querySelectorAll("pre code").forEach(function (el) {
                window.hljs.highlightElement(el);
            });
        }
        root.querySelectorAll("[data-destruct]").forEach(function (el) {
            if (el.dataset.destructScheduled) return;
            var ms = parseInt(el.getAttribute("data-destruct"), 10);
            if (!ms) return;
            el.dataset.destructScheduled = "1";
            setTimeout(function () {
                el.remove();
            }, ms);
        });
    }

    function appendDump(html) {
        var wrap = dumpRoot();
        if (!wrap || !html) return;
        var tmp = document.createElement("div");
        tmp.innerHTML = html;
        afterInsert(tmp);
        while (tmp.firstChild) wrap.appendChild(tmp.firstChild);
        var pane = dumpPane();
        if (pane) pane.scrollTop = pane.scrollHeight;
    }

    function clearDump() {
        var wrap = dumpRoot();
        if (wrap) wrap.innerHTML = "";
    }

    function setTextHtml(html) {
        var pane = textPane();
        if (pane) pane.innerHTML = html || "";
    }

    function setHtmlMode(htmlMode) {
        var dump = dumpPane();
        var text = textPane();
        if (dump) dump.hidden = !htmlMode;
        if (text) text.hidden = !!htmlMode;
    }

    document.addEventListener("click", function (e) {
        var title = e.target.closest(".title");
        if (title) {
            var group = title.closest(".group.titled");
            if (group) group.classList.toggle("collapsed");
            return;
        }

        var actionable = e.target.closest(".collapse-actionable");
        if (!actionable) return;
        var table = actionable.closest("table");
        if (table) setCollapsed(table, !table.classList.contains("collapsed"));
    });

    window.scratchpad = {
        appendDump: appendDump,
        clearDump: clearDump,
        setTextHtml: setTextHtml,
        setHtmlMode: setHtmlMode
    };

    var root = dumpRoot();
    if (root) afterInsert(root);
})();
