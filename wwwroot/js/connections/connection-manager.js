(function () {
    'use strict';

    function getToken() {
        return document.querySelector('#settingsAntiforgeryForm input[name="__RequestVerificationToken"]')?.value
            || document.querySelector('input[name="__RequestVerificationToken"]')?.value
            || '';
    }

    function showModal(id) {
        const el = document.getElementById(id);
        if (el) bootstrap.Modal.getOrCreateInstance(el).show();
    }

    function hideModal(id) {
        const el = document.getElementById(id);
        if (el) bootstrap.Modal.getInstance(el)?.hide();
    }

    async function postJson(url, body) {
        const resp = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getToken()
            },
            body: JSON.stringify(body)
        });
        return resp.json();
    }

    async function postEmpty(url) {
        const resp = await fetch(url, {
            method: 'POST',
            headers: { 'RequestVerificationToken': getToken() }
        });
        return resp.json();
    }

    async function loadModal(url, placeholderId) {
        const resp = await fetch(url);
        if (!resp.ok) { Swal.fire('Error', 'Could not load form.', 'error'); return false; }
        document.getElementById(placeholderId).innerHTML = await resp.text();
        return true;
    }

    function collectForm(form) {
        const data = {};
        new FormData(form).forEach((v, k) => {
            if (k === '__RequestVerificationToken') return;
            if (k === 'exchangeId') data.exchangeId = parseInt(v, 10) || 0;
            else if (k === 'isDefault') data.isDefault = true;
            else if (k === 'isActive') data.isActive = true;
            else data[k] = v;
        });
        // Ensure booleans default to false if not set
        if (data.isDefault === undefined) data.isDefault = false;
        if (data.isActive === undefined) data.isActive = false;
        return data;
    }

    document.addEventListener('click', async function (e) {
        const target = e.target.closest('button, [data-id]');
        if (!target) return;

        // Add Connection button
        if (target.id === 'addConnectionBtn') {
            e.preventDefault();
            const ok = await loadModal('/Settings/GetAddConnectionModal', 'connectionModalsPlaceholder');
            if (!ok) return;
            showModal('addConnectionModal');

            document.getElementById('saveAddConnection')?.addEventListener('click', async function () {
                const form = document.getElementById('addConnectionForm');
                if (!form.checkValidity()) { form.reportValidity(); return; }
                const payload = collectForm(form);
                const result = await postJson('/Settings/AddConnection', payload);
                if (result.success) {
                    hideModal('addConnectionModal');
                    location.reload();
                } else {
                    Swal.fire('Error', result.message || 'Failed to add connection.', 'error');
                }
            });
            return;
        }

        // Edit connection
        if (target.classList.contains('edit-connection')) {
            e.preventDefault();
            const id = target.dataset.id;
            const ok = await loadModal(`/Settings/GetEditConnectionModal?id=${id}`, 'connectionModalsPlaceholder');
            if (!ok) return;
            showModal('editConnectionModal');

            document.getElementById('saveEditConnection')?.addEventListener('click', async function () {
                const form = document.getElementById('editConnectionForm');
                const payload = collectForm(form);
                const result = await postJson(`/Settings/EditConnection?id=${id}`, payload);
                if (result.success) {
                    hideModal('editConnectionModal');
                    location.reload();
                } else {
                    Swal.fire('Error', result.message || 'Failed to update connection.', 'error');
                }
            });
            return;
        }

        // Test connection
        if (target.classList.contains('test-connection')) {
            e.preventDefault();
            const id = target.dataset.id;
            target.disabled = true;
            target.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';
            try {
                const result = await postEmpty(`/Settings/TestConnection?id=${id}`);
                if (result.success) {
                    const passed = result.testResult === '1';
                    Swal.fire(
                        passed ? 'Connection Valid' : 'Connection Failed',
                        passed ? `Balance: $${parseFloat(result.balance).toFixed(2)}` : 'Could not connect. Check your API credentials.',
                        passed ? 'success' : 'warning'
                    ).then(() => location.reload());
                } else {
                    Swal.fire('Error', result.message || 'Test failed.', 'error');
                }
            } finally {
                target.disabled = false;
                target.innerHTML = '<i class="bi bi-lightning-fill"></i>';
            }
            return;
        }

        // Set default connection
        if (target.classList.contains('set-default-connection')) {
            e.preventDefault();
            const id = target.dataset.id;
            const result = await postEmpty(`/Settings/SetDefaultConnection?id=${id}`);
            if (result.success) location.reload();
            else Swal.fire('Error', result.message || 'Failed.', 'error');
            return;
        }

        // Delete connection
        if (target.classList.contains('delete-connection')) {
            e.preventDefault();
            const id = target.dataset.id;
            const confirm = await Swal.fire({
                title: 'Delete Connection?',
                text: 'This will remove the connection. Any provider settings pointing to it will revert to default.',
                icon: 'warning',
                showCancelButton: true,
                confirmButtonText: 'Delete',
                confirmButtonColor: '#dc3545'
            });
            if (!confirm.isConfirmed) return;
            const result = await postEmpty(`/Settings/DeleteConnection?id=${id}`);
            if (result.success) location.reload();
            else Swal.fire('Error', result.message || 'Failed to delete.', 'error');
            return;
        }
    });
}());
