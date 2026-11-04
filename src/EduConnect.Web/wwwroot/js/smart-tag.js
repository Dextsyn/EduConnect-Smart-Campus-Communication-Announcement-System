// Smart tag on the announcement Create/Edit forms: sends the title and
// body to /Announcement/SmartTag and fills in category, priority, expiry,
// summary and audience. The poster can change anything before saving;
// the server still validates every target as usual.
(function () {
    const button = document.getElementById('btnSmartTag');
    if (!button) return;
    const form = button.closest('form');
    const status = document.getElementById('smartTagStatus');

    const audienceNames = ['TargetCollegeIDs', 'TargetDepartmentIDs', 'TargetProgramIDs', 'SelectedTagIDs'];

    function setStatus(text, kind) {
        status.textContent = text;
        status.className = 'small ' + (kind === 'error' ? 'text-danger' : kind === 'warn' ? 'text-warning' : 'text-muted');
    }

    // One note per field, placed after `anchor`, saying why the AI chose it.
    function note(anchor, key, reason) {
        if (!anchor) return;
        let el = form.querySelector('.ec-smart-note[data-for="' + key + '"]');
        if (!el) {
            el = document.createElement('div');
            el.className = 'ec-smart-note';
            el.dataset.for = key;
            anchor.insertAdjacentElement('afterend', el);
        }
        el.innerHTML = '';
        const badge = document.createElement('span');
        badge.className = 'ec-smart-badge';
        badge.innerHTML = '<i class="bi bi-stars"></i> AI';
        el.appendChild(badge);
        el.appendChild(document.createTextNode(' ' + (reason || 'Picked from the text.')));
        anchor.classList.add('ec-smart-filled');
    }

    function clearNotes() {
        form.querySelectorAll('.ec-smart-note').forEach(n => n.remove());
        form.querySelectorAll('.ec-smart-filled').forEach(n => n.classList.remove('ec-smart-filled'));
    }

    function apply(s) {
        clearNotes();

        if (s.categoryID != null) {
            const card = form.querySelector('.cat-card[data-cat-id="' + s.categoryID + '"]');
            if (card) {
                selectCategory(card, s.categoryID);
                note(form.querySelector('.cat-grid'), 'category', s.categoryReason);
            }
        }

        if (s.priority != null) {
            const pill = form.querySelectorAll('.priority-pill')[s.priority - 1];
            if (pill) {
                selectPriority(pill, s.priority);
                note(form.querySelector('.priority-group'), 'priority', s.priorityReason);
            }
        }

        if (s.expiresAt) {
            const toggle = document.getElementById('expiryToggle');
            const input = document.getElementById('expiryInput');
            if (toggle && input) {
                toggle.checked = true;
                toggleExpiry(toggle);
                input.value = s.expiresAt.slice(0, 16);
                note(document.getElementById('expiryRow'), 'expiry', s.expiryReason);
            }
        }

        if (s.summary) {
            const summary = document.getElementById('AISummary');
            if (summary) {
                summary.value = s.summary;
                note(summary, 'summary', 'Written from your message. Edit it if needed.');
            }
        }

        if (s.hasAudience) {
            const wanted = {
                TargetCollegeIDs: s.collegeIDs, TargetDepartmentIDs: s.departmentIDs,
                TargetProgramIDs: s.programIDs, SelectedTagIDs: s.tagIDs
            };
            audienceNames.forEach(name => {
                form.querySelectorAll('input[type="checkbox"][name="' + name + '"]').forEach(box => {
                    // Targets the poster can't re-pick (Edit's "Already
                    // targeted" list) are left alone.
                    if (box.id.startsWith('tk_')) return;
                    const on = wanted[name].includes(parseInt(box.value, 10));
                    if (box.checked !== on) {
                        box.checked = on;
                        box.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                });
            });
            const audienceMsg = form.querySelector('[data-valmsg-for="SelectedTagIDs"]');
            if (audienceMsg) audienceMsg.textContent = '';
            note(document.getElementById('targetPicker') || audienceMsg, 'audience', s.audienceReason);
        }

        const logField = document.getElementById('SmartTagLogID');
        if (logField) logField.value = s.logID ?? '';

        if (s.source === 'ai')
            setStatus('Filled in by AI. Check each field before you save.');
        else
            setStatus('The AI is unavailable right now, so keyword rules filled in what they could. Summary was skipped.', 'warn');
    }

    button.addEventListener('click', async () => {
        const title = (document.getElementById('Title')?.value || '').trim();
        const body = (document.getElementById('Body')?.value || '').trim();
        if ((title + body).length < 20) {
            setStatus('Write a title and message first, then click Smart tag.', 'error');
            return;
        }

        const data = new FormData();
        data.append('title', title);
        data.append('body', body);
        data.append('__RequestVerificationToken',
            form.querySelector('input[name="__RequestVerificationToken"]').value);

        button.disabled = true;
        const label = button.innerHTML;
        button.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status"></span>Reading…';
        setStatus('Reading your announcement…');
        try {
            const res = await fetch(button.dataset.url, { method: 'POST', body: data, credentials: 'same-origin' });
            const json = await res.json().catch(() => null);
            if (!res.ok) {
                setStatus((json && json.error) || 'Smart tag failed. Please try again.', 'error');
                return;
            }
            apply(json);
        } catch (e) {
            setStatus('Smart tag failed. Check your connection and try again.', 'error');
        } finally {
            button.disabled = false;
            button.innerHTML = label;
        }
    });
})();
