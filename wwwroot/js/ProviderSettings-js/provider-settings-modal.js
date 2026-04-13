(function () {
    'use strict';

    function initializeModalScripts(modal) {
        const form = modal.querySelector('#providerSettingsForm');
        const saveButton = modal.querySelector('#saveSettings');

        function getRequestVerificationToken() {
            return form?.querySelector('input[name="__RequestVerificationToken"]')?.value
                || document.querySelector('input[name="__RequestVerificationToken"]')?.value
                || '';
        }

        const useStoplossSwitch = modal.querySelector('#useStoplossSwitch');
        const stoplossInput = modal.querySelector('#stoplossInput');
        const moveStoplossSwitch = modal.querySelector('#moveStoplossSwitch');
        const moveStoplossOnSelect = modal.querySelector('#moveStoplossOnSelect');

        const ignoreLongSwitch = modal.querySelector('#ignoreLongSwitch');
        const ignoreShortSwitch = modal.querySelector('#ignoreShortSwitch');
        const positionWarning = modal.querySelector('#positionWarning');

        function updateStoplossInput() {
            if (!stoplossInput || !useStoplossSwitch) return;

            stoplossInput.disabled = !useStoplossSwitch.checked;
            if (!useStoplossSwitch.checked) stoplossInput.value = '0';
        }

        updateStoplossInput();
        useStoplossSwitch?.addEventListener('change', updateStoplossInput);

        moveStoplossSwitch?.addEventListener('change', function () {
            if (!moveStoplossOnSelect) return;

            moveStoplossOnSelect.disabled = !this.checked;
            if (!this.checked) moveStoplossOnSelect.value = '0';
        });

        if (moveStoplossOnSelect && moveStoplossSwitch) {
            moveStoplossOnSelect.disabled = !moveStoplossSwitch.checked;
        }

        function validatePositionFilters(e) {
            if (!ignoreLongSwitch || !ignoreShortSwitch || !positionWarning) return true;

            if (ignoreLongSwitch.checked && ignoreShortSwitch.checked) {
                positionWarning.style.display = 'block';
                if (e?.target === ignoreLongSwitch) ignoreShortSwitch.checked = false;
                else if (e?.target === ignoreShortSwitch) ignoreLongSwitch.checked = false;
                return false;
            }

            positionWarning.style.display = 'none';
            return true;
        }

        ignoreLongSwitch?.addEventListener('change', validatePositionFilters);
        ignoreShortSwitch?.addEventListener('change', validatePositionFilters);
        validatePositionFilters();

        function getNumberByName(name) {
            const input = form?.querySelector(`input[name="${name}"]`);
            return input ? (parseFloat(input.value) || 0) : 0;
        }

        function validateTradeSizes() {
            const minTrade = getNumberByName('MinTradeSizeUsd');
            const maxTrade = getNumberByName('MaxTradeSizeUsd');

            if (minTrade > maxTrade) {
                Swal.fire('Error', 'Maximum trade size must be greater than minimum trade size', 'error');
                return false;
            }

            return true;
        }

        function buildPayload() {
            return {
                id: parseInt(form.querySelector('input[name="Id"]').value || '0', 10),
                providerId: form.querySelector('input[name="ProviderId"]').value,
                userId: form.querySelector('input[name="UserId"]').value,

                isEnabled: form.querySelector('input[name="IsEnabled"]').checked,
                testing: form.querySelector('input[name="Testing"]').checked,
                useMoonbag: form.querySelector('input[name="UseMoonbag"]').checked,

                riskPercentage: getNumberByName('RiskPercentage'),
                overideLeverage: form.querySelector('input[name="OverideLeverage"]').checked,
                leverage: parseInt(form.querySelector('input[name="Leverage"]').value || '0', 10),

                ignoreStoploss: form.querySelector('input[name="IgnoreStoploss"]')?.checked ?? false,
                useStoploss: form.querySelector('input[name="UseStoploss"]')?.checked ?? false,
                stoplossPercentage: getNumberByName('StoplossPercentage'),

                moveStoploss: form.querySelector('input[name="MoveStoploss"]')?.checked ?? false,
                moveStoplossOn: parseInt(form.querySelector('select[name="MoveStoplossOn"]')?.value || '0', 10),

                minTradeSizeUsd: getNumberByName('MinTradeSizeUsd'),
                maxTradeSizeUsd: getNumberByName('MaxTradeSizeUsd'),

                isIsolated: (form.querySelector('input[name="IsIsolated"]:checked')?.value === 'true'),

                ignorLong: ignoreLongSwitch?.checked ?? false,
                ignorShort: ignoreShortSwitch?.checked ?? false,

                moonbagPercentage: parseInt(form.querySelector('input[name="MoonbagPercentage"]')?.value || '0', 10),
                moonbagSize: form.querySelector('input[name="MoonbagSize"]')?.value ?? '',

                tpPercentages: Array.from(form.querySelectorAll('input[name^="TpPercentages["]'))
                    .map(x => parseFloat(x.value) || 0),
                connectionId: (() => {
                    const v = form.querySelector('select[name="ConnectionId"]')?.value;
                    return v ? parseInt(v, 10) : null;
                })()
            };
        }

        async function saveProviderSettings() {
            if (!validatePositionFilters()) return;
            if (!validateTradeSizes()) return;

            const payload = buildPayload();
            const token = getRequestVerificationToken();

            if (!token) {
                Swal.fire('Error', 'Security token missing. Please refresh the page.', 'error');
                return;
            }

            const response = await fetch('/Settings/SaveProviderSettings', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    RequestVerificationToken: token
                },
                body: JSON.stringify(payload)
            });

            const result = await response.json();

            if (!result?.success) {
                Swal.fire('Error', result?.message ?? 'Save failed', 'error');
                return;
            }

            Swal.fire('Saved', 'Provider settings updated', 'success')
                .then(() => location.reload());
        }

        saveButton?.addEventListener('click', function (e) {
            e.preventDefault();
            saveProviderSettings();
        });
    }

    function observeForProviderSettingsModal() {
        const observer = new MutationObserver(function (mutations) {
            mutations.forEach(function (mutation) {
                mutation.addedNodes.forEach(function (node) {
                    if (node && node.nodeType === 1 && node.id === 'providerSettingsModal') {
                        initializeModalScripts(node);
                    }
                });
            });
        });

        observer.observe(document.body, { childList: true, subtree: true });
    }

    document.addEventListener('DOMContentLoaded', observeForProviderSettingsModal);
})();
