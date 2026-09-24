document.addEventListener("DOMContentLoaded", function () {
    if (typeof feather !== "undefined") feather.replace();

    const errors = window.AUTH_ERRORS || [];
    if (errors.length > 0) {
        const msg = errors.join("\n");
        if (typeof showAlert === "function") {
            showAlert(msg, { type: "error", title: "Ошибка входа" });
        } else {
            console.error(msg);
        }
    }
});
