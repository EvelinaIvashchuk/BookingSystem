// CarRental AI assistant widget.
// Security: everything that comes from the server or the model is inserted with textContent
// (or as attribute values that are validated), never as HTML. Reply formatting (paragraphs, lists,
// links, amounts) builds DOM nodes from plain text; nothing is parsed as markup.
(function () {
    'use strict';

    const widget = document.getElementById('chat-widget');
    if (!widget) return;

    const L = widget.dataset;                 // localized texts (data-* attributes)
    const toggle = document.getElementById('chat-toggle');
    const panel = document.getElementById('chat-window');
    const iconOpen = document.getElementById('chat-icon-open');
    const iconClose = document.getElementById('chat-icon-close');
    const messages = document.getElementById('chat-messages');
    const input = document.getElementById('chat-input');
    const sendBtn = document.getElementById('chat-send');
    const newChatBtn = document.getElementById('chat-new');
    const antiForgeryToken = (widget.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '';
    const locale = document.documentElement.lang || undefined;

    // ── Session (the server owns the history; the widget only keeps the id) ──────
    const SESSION_KEY = 'carrental.chat.sessionId';
    function loadSessionId() { try { return localStorage.getItem(SESSION_KEY); } catch (e) { return null; } }
    function saveSessionId(id) { try { if (id) localStorage.setItem(SESSION_KEY, id); } catch (e) { /* unavailable */ } }
    function clearSessionId() { try { localStorage.removeItem(SESSION_KEY); } catch (e) { /* unavailable */ } }

    let sessionId = loadSessionId();
    let initialized = false;
    let busy = false;

    // ── Helpers ────────────────────────────────────────────────────────────────
    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }
    function format(template, args) {
        return (template || '').replace(/\{(\d+)\}/g, function (_, i) { return args[i] !== undefined ? args[i] : ''; });
    }
    function money(value) {
        return Number(value).toLocaleString(locale, { maximumFractionDigits: 0 });
    }
    function formatDate(iso) {
        const p = (iso || '').split('-');
        return p.length === 3 ? new Date(+p[0], +p[1] - 1, +p[2]).toLocaleDateString(locale) : iso;
    }
    function minutesLeft(expiresAtUtc) {
        const ms = Date.parse(/Z$/.test(expiresAtUtc) ? expiresAtUtc : expiresAtUtc + 'Z') - Date.now();
        return Math.max(0, Math.round(ms / 60000));
    }
    function safeImageUrl(url) {
        if (typeof url !== 'string') return null;
        if (/^(https:\/\/|http:\/\/|\/)/i.test(url)) return url;
        // Storage key of an uploaded photo ("cars/<guid>.jpg") is served by the image proxy, as in ImageHelper.
        return /^[\w-]+\/[\w.-]+$/.test(url) ? '/image/' + url : null;
    }
    function scrollDown() { messages.scrollTop = messages.scrollHeight; }

    // ── Reply formatting ─────────────────────────────────────────────────────────
    // Replies are plain text (the prompt asks for it and the server strips Markdown); lists come as lines
    // starting with "- ". Rendered as paragraphs and lists, with phone numbers and e-mails as links and
    // amounts in hryvnias highlighted.
    const INLINE_RE = /(\+380[\d ]{8,13}\d)|([\w.+-]+@[\w-]+(?:\.[\w-]+)+)|(\d[\d\u00a0\u202f ]*(?:[.,]\d+)?[\u00a0 ]?(?:₴|грн\.?|UAH))/g;
    const LIST_RE = /^\s*(?:([-–•])|(\d{1,2})[.)])\s+(.+)$/;

    function appendInline(parent, text) {
        let last = 0;
        INLINE_RE.lastIndex = 0;
        let m;
        while ((m = INLINE_RE.exec(text)) !== null) {
            if (m.index > last) parent.appendChild(document.createTextNode(text.slice(last, m.index)));
            let node;
            if (m[1]) {
                node = el('a', 'chat-link', m[1]);
                node.href = 'tel:' + m[1].replace(/\s+/g, '');
            } else if (m[2]) {
                node = el('a', 'chat-link', m[2]);
                node.href = 'mailto:' + m[2];
            } else {
                node = el('strong', 'chat-amount', m[3]);
            }
            parent.appendChild(node);
            last = INLINE_RE.lastIndex;
        }
        if (last < text.length) parent.appendChild(document.createTextNode(text.slice(last)));
    }

    function renderReply(container, text) {
        let paragraph = null;
        let list = null;
        String(text).split(/\r?\n/).forEach(function (line) {
            const item = LIST_RE.exec(line);
            if (item) {
                const tag = item[2] ? 'ol' : 'ul';
                if (!list || list.tagName.toLowerCase() !== tag) {
                    list = el(tag);
                    if (item[2] && item[2] !== '1') list.start = +item[2];
                    container.appendChild(list);
                }
                const li = el('li');
                appendInline(li, item[3]);
                list.appendChild(li);
                paragraph = null;
                return;
            }
            if (!line.trim()) { paragraph = null; list = null; return; }
            list = null;
            if (paragraph) paragraph.appendChild(el('br'));
            else { paragraph = el('p'); container.appendChild(paragraph); }
            appendInline(paragraph, line.trim());
        });
    }

    // ── Messages ───────────────────────────────────────────────────────────────
    function appendMessage(text, who) {
        let div;
        if (who === 'bot') {
            div = el('div', 'chat-msg chat-msg--bot chat-msg--rich');
            renderReply(div, text);
        } else {
            div = el('div', 'chat-msg chat-msg--' + who, text);
        }
        messages.appendChild(div);
        scrollDown();
        return div;
    }

    function appendThinking() {
        const div = el('div', 'chat-msg chat-msg--bot chat-msg--typing');
        div.setAttribute('role', 'status');
        const dots = el('span', 'chat-dots');
        for (let i = 0; i < 3; i++) dots.appendChild(el('span'));
        div.appendChild(dots);
        div.appendChild(el('span', 'chat-thinking-label', L.thinking));
        messages.appendChild(div);
        scrollDown();
        return div;
    }

    function setBusy(value) {
        busy = value;
        input.disabled = value;
        sendBtn.disabled = value;
        messages.querySelectorAll('.chat-car__select').forEach(function (b) { b.disabled = value; });
        if (!value) input.focus();
    }

    // ── Car cards ──────────────────────────────────────────────────────────────
    function appendCarCards(cars) {
        const list = el('div', 'chat-cars');
        cars.forEach(function (car) {
            const card = el('div', 'chat-car');

            const imageUrl = safeImageUrl(car.imageUrl);
            if (imageUrl) {
                const img = el('img', 'chat-car__img');
                img.src = imageUrl;
                img.alt = car.name;
                img.loading = 'lazy';
                card.appendChild(img);
            } else {
                const placeholder = el('div', 'chat-car__img chat-car__img--empty');
                placeholder.appendChild(el('i', 'bi bi-car-front'));
                card.appendChild(placeholder);
            }

            const body = el('div', 'chat-car__body');
            body.appendChild(el('div', 'chat-car__name', car.name));
            body.appendChild(el('div', 'chat-car__price', money(car.pricePerDay) + ' ₴ ' + L.perDay));
            if (car.totalPrice) {
                body.appendChild(el('div', 'chat-car__total', format(L.draftTotal, [money(car.totalPrice)])));
            }

            const actions = el('div', 'chat-car__actions');
            const details = el('a', 'btn btn-sm btn-outline-secondary', L.details);
            details.href = '/Car/Details/' + encodeURIComponent(car.id);
            const select = el('button', 'btn btn-sm btn-primary chat-car__select', L.select);
            select.type = 'button';
            select.addEventListener('click', function () {
                if (busy) return;
                list.querySelectorAll('.chat-car').forEach(function (c) { c.classList.remove('chat-car--chosen'); });
                card.classList.add('chat-car--chosen');
                sendMessage(format(L.selectMessage, [car.name, car.id]));
            });
            actions.appendChild(details);
            actions.appendChild(select);
            body.appendChild(actions);

            card.appendChild(body);
            list.appendChild(card);
        });
        messages.appendChild(list);
        scrollDown();
    }

    // ── Booking confirmation card (human-in-the-loop) ──────────────────────────
    function appendDraftCard(draft) {
        const card = el('div', 'chat-card');
        card.appendChild(el('div', 'chat-card__title', L.draftTitle));
        card.appendChild(el('div', 'chat-card__car', draft.carName));
        card.appendChild(el('div', 'chat-card__line',
            format(L.draftDates, [formatDate(draft.pickupDate), formatDate(draft.returnDate), draft.days])));
        card.appendChild(el('div', 'chat-card__total', format(L.draftTotal, [money(draft.totalPrice)])));
        const hint = el('div', 'chat-card__hint', format(L.draftHint, [minutesLeft(draft.expiresAt)]));
        card.appendChild(hint);

        const actions = el('div', 'chat-card__actions');
        const confirmBtn = el('button', 'btn btn-sm btn-primary', L.confirm);
        const cancelBtn = el('button', 'btn btn-sm btn-outline-secondary', L.cancel);
        confirmBtn.type = cancelBtn.type = 'button';
        confirmBtn.addEventListener('click', function () { decide('Confirm', draft.pendingActionId, card, actions, hint); });
        cancelBtn.addEventListener('click', function () { decide('Cancel', draft.pendingActionId, card, actions, hint); });
        actions.appendChild(confirmBtn);
        actions.appendChild(cancelBtn);
        card.appendChild(actions);

        messages.appendChild(card);
        scrollDown();
    }

    function decide(action, id, card, actions, hint) {
        actions.querySelectorAll('button').forEach(function (b) { b.disabled = true; });

        fetch('/Ai/' + action + '/' + encodeURIComponent(id), {
            method: 'POST',
            headers: { 'RequestVerificationToken': antiForgeryToken }
        })
        .then(function (r) {
            // [Authorize] redirects anonymous users to the login page (HTML, not JSON).
            const isJson = (r.headers.get('content-type') || '').indexOf('application/json') !== -1;
            if (r.redirected || !isJson) return { ok: false, message: L.signIn, retry: true };
            return r.json();
        })
        .then(function (data) {
            if (data.ok || !data.retry) { actions.remove(); hint.remove(); }
            else actions.querySelectorAll('button').forEach(function (b) { b.disabled = false; });

            card.classList.add(data.ok && action === 'Confirm' ? 'chat-card--done' : 'chat-card--closed');
            appendMessage(data.message || L.error, 'bot');

            if (data.ok && data.url) {
                const link = el('a', 'chat-card__link', L.myRentals);
                link.href = data.url;
                card.appendChild(link);
            }
        })
        .catch(function () {
            actions.querySelectorAll('button').forEach(function (b) { b.disabled = false; });
            appendMessage(L.error, 'bot');
        });
    }

    // ── Date picker for clarifications ─────────────────────────────────────────
    // Shown when the reply asks for rental dates (askedDates from the server). Submitting sends an ordinary
    // message ("З 20 грудня 2026 р. по 22 грудня 2026 р."), so the agent handles it like typed text.
    let openDateForm = null;

    function isoToday(offsetDays) {
        const d = new Date();
        d.setDate(d.getDate() + (offsetDays || 0));
        return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
    }
    function longDate(iso) {
        const p = iso.split('-');
        return new Date(+p[0], +p[1] - 1, +p[2]).toLocaleDateString(locale, { day: 'numeric', month: 'long', year: 'numeric' });
    }
    function closeDateForm() {
        if (!openDateForm) return;
        openDateForm.classList.add('chat-card--closed');
        openDateForm.querySelectorAll('input, button').forEach(function (n) { n.disabled = true; });
        openDateForm = null;
    }

    function appendDateForm(slots) {
        const askPickup = slots.indexOf('PickupDate') >= 0;
        const askReturn = slots.indexOf('ReturnDate') >= 0;
        if (!askPickup && !askReturn) return;

        const card = el('form', 'chat-card chat-dates');
        card.noValidate = true;
        card.appendChild(el('div', 'chat-card__title', L.datesTitle));
        const fields = el('div', 'chat-dates__fields');
        function field(label, min) {
            const wrap = el('label', 'chat-dates__field');
            wrap.appendChild(el('span', null, label));
            const inputEl = el('input', 'form-control form-control-sm');
            inputEl.type = 'date';
            inputEl.min = min;
            inputEl.required = true;
            wrap.appendChild(inputEl);
            fields.appendChild(wrap);
            return inputEl;
        }
        const pickup = askPickup ? field(L.pickupDate, isoToday(0)) : null;
        const ret = askReturn ? field(L.returnDate, isoToday(1)) : null;
        if (pickup && ret) {
            pickup.addEventListener('change', function () {
                if (!pickup.value) return;
                const next = new Date(pickup.value + 'T00:00');
                next.setDate(next.getDate() + 1);
                ret.min = next.getFullYear() + '-' + String(next.getMonth() + 1).padStart(2, '0') + '-' + String(next.getDate()).padStart(2, '0');
                if (ret.value && ret.value < ret.min) ret.value = '';
            });
        }
        card.appendChild(fields);
        const error = el('div', 'chat-card__hint chat-dates__error d-none', L.datesInvalid);
        card.appendChild(error);
        const send = el('button', 'btn btn-sm btn-primary', L.datesSend);
        send.type = 'submit';
        const actions = el('div', 'chat-card__actions');
        actions.appendChild(send);
        card.appendChild(actions);

        card.addEventListener('submit', function (e) {
            e.preventDefault();
            const inputs = [pickup, ret].filter(Boolean);
            if (inputs.some(function (i) { return !i.value || i.value < i.min; })) { inputs.forEach(function (i) { i.reportValidity(); }); return; }
            if (pickup && ret && ret.value <= pickup.value) { error.classList.remove('d-none'); return; }
            const text = pickup && ret ? format(L.datesBoth, [longDate(pickup.value), longDate(ret.value)])
                : pickup ? format(L.datesPickup, [longDate(pickup.value)])
                : format(L.datesReturn, [longDate(ret.value)]);
            sendMessage(text);
        });

        messages.appendChild(card);
        openDateForm = card;
        scrollDown();
        (pickup || ret).focus({ preventScroll: true });
    }

    // ── Conversation ───────────────────────────────────────────────────────────
    function sendMessage(text) {
        text = (text || '').trim();
        if (!text || busy) return;

        closeDateForm();
        appendMessage(text, 'user');
        input.value = '';
        setBusy(true);
        const thinking = appendThinking();

        const headers = { 'Content-Type': 'application/json', 'RequestVerificationToken': antiForgeryToken };
        if (sessionId) headers['X-Chat-Session'] = sessionId;   // per-session rate limit

        fetch('/Ai/Chat', {
            method: 'POST',
            headers: headers,
            body: JSON.stringify({ message: text, sessionId: sessionId })
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            thinking.remove();
            if (data.sessionId) { sessionId = data.sessionId; saveSessionId(sessionId); }
            appendMessage(data.reply || L.error, 'bot');
            if (Array.isArray(data.cars) && data.cars.length) appendCarCards(data.cars);
            if (data.draft) appendDraftCard(data.draft);
            if (Array.isArray(data.askedDates) && data.askedDates.length) appendDateForm(data.askedDates);
        })
        .catch(function () {
            thinking.remove();
            appendMessage(L.error, 'bot');
        })
        .finally(function () { setBusy(false); });
    }

    function restoreHistory() {
        messages.replaceChildren();
        appendMessage(L.greeting, 'bot');
        if (!sessionId) return;

        fetch('/Ai/History?sessionId=' + encodeURIComponent(sessionId))
            .then(function (r) {
                if (r.status === 404) { clearSessionId(); sessionId = null; return null; }
                return r.ok ? r.json() : null;
            })
            .then(function (data) {
                if (!data) return;
                (data.messages || []).forEach(function (m) {
                    appendMessage(m.text, m.role === 'user' ? 'user' : 'bot');
                });
                if (data.draft) appendDraftCard(data.draft);
            })
            .catch(function () { /* history is a convenience; the chat still works */ });
    }

    function startNewChat() {
        if (busy) return;
        openDateForm = null;
        clearSessionId();
        sessionId = null;
        messages.replaceChildren();
        appendMessage(L.greeting, 'bot');
        input.focus();
    }

    function setOpen(open) {
        panel.classList.toggle('d-none', !open);
        iconOpen.classList.toggle('d-none', open);
        iconClose.classList.toggle('d-none', !open);
        toggle.setAttribute('aria-expanded', String(open));
        if (open) {
            if (!initialized) { initialized = true; restoreHistory(); }
            input.focus();
        }
    }

    // Entry point for the home page: the hero input and suggestion chips start a new conversation with their text.
    window.CarRentalChat = {
        open: function () { setOpen(true); },
        ask: function (text) {
            if (busy) return;
            initialized = true;
            clearSessionId();
            sessionId = null;
            openDateForm = null;
            messages.replaceChildren();
            appendMessage(L.greeting, 'bot');
            setOpen(true);
            sendMessage(text);
        }
    };

    toggle.addEventListener('click', function () { setOpen(panel.classList.contains('d-none')); });
    newChatBtn.addEventListener('click', startNewChat);
    sendBtn.addEventListener('click', function () { sendMessage(input.value); });
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(input.value); }
    });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !panel.classList.contains('d-none')) setOpen(false);
    });
})();
