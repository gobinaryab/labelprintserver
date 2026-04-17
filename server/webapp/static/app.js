(() => {
    const PRINTERS = ["p750w", "p300bt"];
    const DEFAULT_PRINTER = "p750w";
    const HEALTH_POLL_MS = 10000;

    const textEl = document.getElementById("text");
    const printBtn = document.getElementById("print");
    const banner = document.getElementById("banner");
    const details = document.getElementById("details");
    const statusBody = document.getElementById("status-body");
    const segButtons = document.querySelectorAll(".seg");

    let selectedPrinter = localStorage.getItem("printer") || DEFAULT_PRINTER;
    if (!PRINTERS.includes(selectedPrinter)) selectedPrinter = DEFAULT_PRINTER;

    function setSelected(printer) {
        selectedPrinter = printer;
        localStorage.setItem("printer", printer);
        segButtons.forEach(btn => {
            btn.classList.toggle("active", btn.dataset.printer === printer);
        });
        // Invalidate the cached status panel when switching printers.
        if (details.open) loadStatus();
    }

    segButtons.forEach(btn => {
        btn.addEventListener("click", () => setSelected(btn.dataset.printer));
    });
    setSelected(selectedPrinter);

    function showBanner(kind, message) {
        banner.textContent = message;
        banner.className = `banner ${kind}`;
        if (kind === "ok") {
            setTimeout(() => banner.classList.add("hidden"), 3000);
        }
    }

    function hideBanner() {
        banner.classList.add("hidden");
    }

    async function pollHealth() {
        try {
            const res = await fetch("/api/health", { cache: "no-store" });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            for (const p of PRINTERS) {
                const dot = document.getElementById(`dot-${p}`);
                dot.classList.toggle("ok", data[p] === "ok");
                dot.classList.toggle("bad", data[p] !== "ok");
            }
        } catch {
            for (const p of PRINTERS) {
                const dot = document.getElementById(`dot-${p}`);
                dot.classList.remove("ok");
                dot.classList.add("bad");
            }
        }
    }

    async function loadStatus() {
        statusBody.textContent = "Loading…";
        try {
            const res = await fetch(`/api/status/${selectedPrinter}`, { cache: "no-store" });
            const data = await res.json();
            statusBody.textContent = JSON.stringify(data, null, 2);
        } catch (err) {
            statusBody.textContent = `Error: ${err.message}`;
        }
    }

    details.addEventListener("toggle", () => {
        if (details.open) loadStatus();
    });

    async function doPrint() {
        const text = textEl.value.trim();
        if (!text) {
            showBanner("bad", "Enter label text first.");
            return;
        }
        hideBanner();
        printBtn.disabled = true;
        printBtn.textContent = "Printing…";
        try {
            const res = await fetch(`/api/print/${selectedPrinter}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ text }),
            });
            const data = await res.json().catch(() => ({}));
            if (res.ok && data.success) {
                showBanner("ok", "Printed ✓");
                textEl.value = "";
            } else {
                showBanner("bad", data.error || `HTTP ${res.status}`);
            }
        } catch (err) {
            showBanner("bad", err.message);
        } finally {
            printBtn.disabled = false;
            printBtn.textContent = "Print";
        }
    }

    printBtn.addEventListener("click", doPrint);

    pollHealth();
    setInterval(pollHealth, HEALTH_POLL_MS);
})();
