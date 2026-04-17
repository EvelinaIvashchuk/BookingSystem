// ─── Toast Notifications ──────────────────────────────────────────────────────
function showToast(type, message, duration) {
    duration = duration || 4000;
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const icons = { success: 'bi-check-circle-fill', error: 'bi-exclamation-triangle-fill', warning: 'bi-exclamation-circle-fill', info: 'bi-info-circle-fill' };
    const icon  = icons[type] || icons.info;

    const toast = document.createElement('div');
    toast.className = `app-toast app-toast--${type}`;
    toast.style.setProperty('--toast-duration', duration + 'ms');
    toast.innerHTML = `
        <i class="bi ${icon} app-toast__icon"></i>
        <div class="app-toast__body">${message}</div>
        <button class="app-toast__close" aria-label="Close">&#x2715;</button>
        <div class="app-toast__progress"></div>`;

    container.appendChild(toast);

    const close = () => {
        if (toast.classList.contains('hiding')) return;
        toast.classList.add('hiding');
        toast.addEventListener('animationend', () => toast.remove(), { once: true });
    };

    toast.querySelector('.app-toast__close').addEventListener('click', close);
    const timer = setTimeout(close, duration);
    toast.addEventListener('mouseenter', () => clearTimeout(timer));
    toast.addEventListener('mouseleave', () => setTimeout(close, 1000));
}
