(function () {
    var userId = document.body.dataset.userId;
    if (!userId) return;

    // ── Initial unread count ────────────────────────────────
    fetch('/Notification/UnreadCount')
        .then(function (r) { return r.json(); })
        .then(function (data) { setBadge(data.count); });

    // ── SignalR connection ──────────────────────────────────
    var connection = new signalR.HubConnectionBuilder()
        .withUrl('/notificationHub')
        .withAutomaticReconnect()
        .build();

    connection.on('ReceiveNotification', function (notif) {
        incrementBadge();
        prependItem(notif);
        showToast(notif);
    });

    connection.start().catch(function (err) {
        console.error('SignalR error:', err);
    });

    // ── Bell toggle ─────────────────────────────────────────
    var toggle = document.getElementById('notif-toggle');
    var dropdown = document.getElementById('notif-dropdown');

    if (toggle && dropdown) {
        toggle.addEventListener('click', function (e) {
            e.stopPropagation();
            var open = dropdown.classList.contains('show');
            dropdown.classList.toggle('show', !open);
            if (!open) loadNotifications();
        });

        document.addEventListener('click', function () {
            dropdown.classList.remove('show');
        });

        dropdown.addEventListener('click', function (e) {
            e.stopPropagation();
        });
    }

    // ── Mark all read ───────────────────────────────────────
    var markAllBtn = document.getElementById('notif-mark-all');
    if (markAllBtn) {
        markAllBtn.addEventListener('click', function () {
            fetch('/Notification/MarkAllRead', { method: 'POST' })
                .then(function () {
                    setBadge(0);
                    document.querySelectorAll('.notif-item.unread')
                        .forEach(function (el) { el.classList.remove('unread', 'bg-light'); });
                });
        });
    }

    // ── Load notifications into dropdown ────────────────────
    function loadNotifications() {
        fetch('/Notification/GetRecent')
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var list = document.getElementById('notif-list');
                if (!list) return;
                if (!data || data.length === 0) {
                    list.innerHTML = '<div class="text-center text-muted p-4 small">No notifications</div>';
                    return;
                }
                list.innerHTML = data.map(renderItem).join('');
            });
    }

    function renderItem(n) {
        var timeAgo = formatTimeAgo(new Date(n.sentAt));
        var icon = getIcon(n.type);
        var unreadCls = n.isRead ? '' : 'unread bg-light';
        var href = n.link ? 'href="' + escapeHtml(n.link) + '"' : 'href="#"';
        return '<a ' + href + ' class="d-block text-decoration-none text-dark notif-item ' + unreadCls + ' px-3 py-2 border-bottom" data-id="' + n.notificationId + '">' +
            '<div class="d-flex gap-2 align-items-start">' +
            '<i class="bi ' + icon + ' mt-1 text-primary flex-shrink-0"></i>' +
            '<div class="flex-grow-1">' +
            '<div class="small lh-sm">' + escapeHtml(n.message) + '</div>' +
            '<div class="text-muted" style="font-size:11px">' + timeAgo + '</div>' +
            '</div>' +
            (!n.isRead ? '<span class="flex-shrink-0 mt-1"><span class="rounded-circle bg-primary d-inline-block" style="width:8px;height:8px;"></span></span>' : '') +
            '</div></a>';
    }

    function prependItem(n) {
        var list = document.getElementById('notif-list');
        if (!list) return;
        var empty = list.querySelector('.text-muted.p-4');
        if (empty) list.innerHTML = '';
        var div = document.createElement('div');
        div.innerHTML = renderItem(n);
        list.insertBefore(div.firstChild, list.firstChild);
    }

    function showToast(n) {
        var container = document.getElementById('toast-container');
        if (!container) return;
        var icon = getIcon(n.type);
        var toast = document.createElement('div');
        toast.className = 'toast show align-items-center text-bg-primary border-0 mb-2';
        toast.setAttribute('role', 'alert');
        toast.setAttribute('aria-live', 'assertive');
        toast.innerHTML =
            '<div class="d-flex">' +
            '<div class="toast-body d-flex align-items-start gap-2">' +
            '<i class="bi ' + icon + ' fs-6"></i>' +
            '<span class="small">' + escapeHtml(n.message) + '</span>' +
            '</div>' +
            '<button type="button" class="btn-close btn-close-white me-2 m-auto" onclick="this.closest(\'.toast\').remove()"></button>' +
            '</div>';
        container.appendChild(toast);
        setTimeout(function () { if (toast.parentNode) toast.remove(); }, 5000);
    }

    // ── Helpers ─────────────────────────────────────────────
    function setBadge(count) {
        var badge = document.getElementById('notif-badge');
        if (!badge) return;
        if (count > 0) {
            badge.textContent = count > 99 ? '99+' : count;
            badge.classList.remove('d-none');
        } else {
            badge.classList.add('d-none');
        }
    }

    function incrementBadge() {
        var badge = document.getElementById('notif-badge');
        if (!badge) return;
        var current = parseInt(badge.textContent) || 0;
        setBadge(current + 1);
    }

    function getIcon(type) {
        switch (type) {
            case 'Announcement': return 'bi-megaphone';
            case 'EventRegistration': return 'bi-calendar-check';
            case 'WaitlistPromotion': return 'bi-arrow-up-circle';
            case 'PasswordReset': return 'bi-shield-lock';
            case 'NewPendingStudent': return 'bi-person-plus';
            default: return 'bi-bell';
        }
    }

    function formatTimeAgo(date) {
        var seconds = Math.floor((Date.now() - date.getTime()) / 1000);
        if (seconds < 60) return 'just now';
        var minutes = Math.floor(seconds / 60);
        if (minutes < 60) return minutes + 'm ago';
        var hours = Math.floor(minutes / 60);
        if (hours < 24) return hours + 'h ago';
        return Math.floor(hours / 24) + 'd ago';
    }

    function escapeHtml(str) {
        var d = document.createElement('div');
        d.appendChild(document.createTextNode(str || ''));
        return d.innerHTML;
    }
})();
