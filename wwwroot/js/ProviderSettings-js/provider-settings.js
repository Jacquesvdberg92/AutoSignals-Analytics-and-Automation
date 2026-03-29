(function ($) {
    'use strict';

    $(document).ready(function () {
        let currentProviderId = null;

        function getRequestVerificationToken() {
            return $('input[name="__RequestVerificationToken"]').first().val();
        }

        // Initialize DataTable
        const table = $('#providersTable').DataTable({
            pageLength: 10,
            responsive: true,
            order: [[1, 'asc']],
            columnDefs: [
                { orderable: false, targets: [0, 7] }
            ],
            language: {
                search: "Search providers:",
                lengthMenu: "Show _MENU_ providers"
            }
        });

        // Edit Provider - Open Modal
        $(document).on('click', '.edit-provider', function () {
            const providerId = $(this).data('provider-id');
            currentProviderId = providerId;

            $.ajax({
                url: '/Settings/GetProviderSettings',
                type: 'GET',
                data: { providerId: providerId },
                success: function (response) {
                    // Clear placeholder and add modal HTML
                    $('#providerSettingsModalPlaceholder').html(response);

                    // Get modal element and show it
                    const modalElement = document.getElementById('providerSettingsModal');
                    if (modalElement) {
                        const modal = new bootstrap.Modal(modalElement);
                        $('#modalProviderName').text('Provider ' + providerId);
                        modal.show();

                        // Set up save button handler
                        $('#saveSettings').off('click').on('click', saveProviderSettings);

                        // Initialize form validation
                        initializeFormValidation();
                        initializeTpLevels();
                    }
                },
                error: function (xhr, status, error) {
                    Swal.fire('Error', 'Failed to load provider settings: ' + error, 'error');
                }
            });
        });

        // Quick Toggle Enable/Disable
        $(document).on('click', '.toggle-provider', function () {
            const providerId = $(this).data('provider-id');
            const isEnabled = $(this).data('enabled').toString().toLowerCase() === 'true';

            Swal.fire({
                title: 'Confirm',
                text: `Are you sure you want to ${isEnabled ? 'disable' : 'enable'} Provider ${providerId}?`,
                icon: 'warning',
                showCancelButton: true,
                confirmButtonText: 'Yes',
                cancelButtonText: 'No'
            }).then((result) => {
                if (result.isConfirmed) {
                    const token = getRequestVerificationToken();

                    if (!token) {
                        Swal.fire('Error', 'Security token missing. Please refresh the page.', 'error');
                        return;
                    }

                    $.ajax({
                        url: '/Settings/SaveProviderSettings',
                        type: 'POST',
                        contentType: 'application/json',
                        headers: {
                            RequestVerificationToken: token
                        },
                        data: JSON.stringify({
                            ProviderId: providerId.toString(),
                            IsEnabled: !isEnabled
                        }),
                        success: function (response) {
                            if (response.success) {
                                Swal.fire('Success', 'Provider status updated successfully', 'success').then(() => {
                                    location.reload();
                                });
                            } else {
                                Swal.fire('Error', response.message, 'error');
                            }
                        },
                        error: function (xhr, status, error) {
                            Swal.fire('Error', 'Failed to update provider status: ' + error, 'error');
                        }
                    });
                }
            });
        });

        // Bulk Actions
        $('#bulkEnable').click(function () {
            const selectedIds = getSelectedProviderIds();
            if (selectedIds.length > 0) {
                bulkUpdateProviders(selectedIds, true);
            } else {
                Swal.fire('Warning', 'Please select at least one provider', 'warning');
            }
        });

        $('#bulkDisable').click(function () {
            const selectedIds = getSelectedProviderIds();
            if (selectedIds.length > 0) {
                bulkUpdateProviders(selectedIds, false);
            } else {
                Swal.fire('Warning', 'Please select at least one provider', 'warning');
            }
        });

        // Select All Checkboxes
        $('#selectAll').click(function () {
            const isChecked = $(this).prop('checked');
            $('.provider-checkbox').prop('checked', isChecked);
            table.rows().nodes().to$().find('.provider-checkbox').prop('checked', isChecked);
        });

        // Table row checkbox click
        $(document).on('change', '.provider-checkbox', function () {
            const allChecked = $('.provider-checkbox').length === $('.provider-checkbox:checked').length;
            $('#selectAll').prop('checked', allChecked);
        });

        // Quick Copy Settings
        $(document).on('click', '.quick-copy', function () {
            const sourceProviderId = $(this).data('provider-id');

            // Get all provider IDs except the source
            const allProviderIds = $('.provider-checkbox').map(function () {
                return $(this).val();
            }).get();

            const targetOptions = allProviderIds
                .filter(id => id !== sourceProviderId)
                .map(id => `<option value="${id}">Provider ${id}</option>`)
                .join('');

            Swal.fire({
                title: 'Copy Settings',
                text: `Copy settings from Provider ${sourceProviderId} to:`,
                html: `
                        <div class="form-group mt-3">
                            <select id="targetProviders" class="form-select" multiple style="height: 200px;">
                                ${targetOptions}
                            </select>
                        </div>
                        <div class="form-check mt-3">
                            <input type="checkbox" id="copyAll" class="form-check-input" checked>
                            <label class="form-check-label" for="copyAll">Copy all settings</label>
                        </div>
                    `,
                showCancelButton: true,
                confirmButtonText: 'Copy',
                cancelButtonText: 'Cancel',
                preConfirm: () => {
                    const targets = $('#targetProviders').val();
                    const copyAll = $('#copyAll').is(':checked');

                    if (!targets || targets.length === 0) {
                        Swal.showValidationMessage('Please select at least one target provider');
                        return false;
                    }

                    return {
                        sourceProviderId: parseInt(sourceProviderId),
                        targetProviderIds: targets.map(id => parseInt(id)),
                        copyAll: copyAll
                    };
                }
            }).then((result) => {
                if (result.isConfirmed) {
                    const token = getRequestVerificationToken();

                    if (!token) {
                        Swal.fire('Error', 'Security token missing. Please refresh the page.', 'error');
                        return;
                    }

                    $.ajax({
                        url: '/Settings/CopyProviderSettings',
                        type: 'POST',
                        contentType: 'application/json',
                        headers: {
                            RequestVerificationToken: token
                        },
                        data: JSON.stringify(result.value),
                        success: function (response) {
                            if (response.success) {
                                Swal.fire('Success', response.message, 'success').then(() => {
                                    location.reload();
                                });
                            } else {
                                Swal.fire('Error', response.message, 'error');
                            }
                        },
                        error: function (xhr, status, error) {
                            Swal.fire('Error', 'Failed to copy settings: ' + error, 'error');
                        }
                    });
                }
            });
        });

        // Save Settings from Modal
        function saveProviderSettings() {
            if (validateForm()) {
                const form = $('#providerSettingsForm');
                const formData = form.serializeArray();
                const data = {};

                // Convert form data to object
                formData.forEach(item => {
                    if (item.name.startsWith('TpPercentages')) {
                        if (!data.TpPercentages) data.TpPercentages = [];
                        data.TpPercentages.push(parseFloat(item.value) || 0);
                    } else if (item.name === 'IsIsolated') {
                        data[item.name] = item.value === 'true';
                    } else {
                        data[item.name] = item.value;
                    }
                });

                // Ensure checkbox values are always included (serializeArray omits unchecked)
                const booleanFields = [
                    'IsEnabled', 'Testing', 'OverideLeverage', 'UseStoploss',
                    'IgnorLong', 'IgnorShort', 'IgnoreStoploss', 'MoveStoploss',
                    'UseMoonbag'
                ];

                booleanFields.forEach(field => {
                    data[field] = form.find(`input[name="${field}"]`).is(':checked');
                });

                // Convert numeric values
                const numericFields = [
                    'Leverage', 'RiskPercentage', 'StoplossPercentage', 'MoveStoplossOn',
                    'MinTradeSizeUsd', 'MaxTradeSizeUsd', 'MoonbagPercentage'
                ];

                numericFields.forEach(field => {
                    if (data[field] !== undefined) {
                        data[field] = parseFloat(data[field]) || 0;
                    }
                });

                const token = getRequestVerificationToken();

                if (!token) {
                    Swal.fire('Error', 'Security token missing. Please refresh the page.', 'error');
                    return;
                }

                $.ajax({
                    url: '/Settings/SaveProviderSettings',
                    type: 'POST',
                    contentType: 'application/json',
                    headers: {
                        RequestVerificationToken: token
                    },
                    data: JSON.stringify(data),
                    success: function (response) {
                        if (response.success) {
                            const modalElement = document.getElementById('providerSettingsModal');
                            if (modalElement) {
                                const modal = bootstrap.Modal.getInstance(modalElement);
                                if (modal) modal.hide();
                            }

                            Swal.fire('Success', 'Settings saved successfully', 'success').then(() => {
                                location.reload();
                            });
                        } else {
                            Swal.fire('Error', response.message, 'error');
                        }
                    },
                    error: function (xhr, status, error) {
                        Swal.fire('Error', 'Failed to save settings: ' + error, 'error');
                    }
                });
            }
        }

        // Helper Functions
        function getSelectedProviderIds() {
            return $('.provider-checkbox:checked').map(function () {
                return parseInt($(this).val());
            }).get();
        }

        function bulkUpdateProviders(providerIds, isEnabled) {
            Swal.fire({
                title: 'Confirm Bulk Update',
                text: `Are you sure you want to ${isEnabled ? 'enable' : 'disable'} ${providerIds.length} selected provider(s)?`,
                icon: 'warning',
                showCancelButton: true,
                confirmButtonText: 'Yes',
                cancelButtonText: 'No'
            }).then((result) => {
                if (result.isConfirmed) {
                    const token = getRequestVerificationToken();

                    if (!token) {
                        Swal.fire('Error', 'Security token missing. Please refresh the page.', 'error');
                        return;
                    }

                    $.ajax({
                        url: '/Settings/BulkUpdateProviderSettings',
                        type: 'POST',
                        contentType: 'application/json',
                        headers: {
                            RequestVerificationToken: token
                        },
                        data: JSON.stringify({
                            ProviderId: providerIds,
                            IsEnabled: isEnabled,
                            Testing: false // Default value
                        }),
                        success: function (response) {
                            if (response.success) {
                                Swal.fire('Success', 'Bulk update completed successfully', 'success').then(() => {
                                    location.reload();
                                });
                            } else {
                                Swal.fire('Error', response.message || 'Bulk update failed', 'error');
                            }
                        },
                        error: function (xhr, status, error) {
                            Swal.fire('Error', 'Failed to perform bulk update: ' + error, 'error');
                        }
                    });
                }
            });
        }

        function initializeFormValidation() {
            const minInput = $('input[name="MinTradeSizeUsd"]');
            const maxInput = $('input[name="MaxTradeSizeUsd"]');

            function validateTradeSize() {
                const min = parseFloat(minInput.val()) || 0;
                const max = parseFloat(maxInput.val()) || 0;

                if (min > max) {
                    maxInput[0].setCustomValidity('Maximum must be greater than minimum');
                    return false;
                } else {
                    maxInput[0].setCustomValidity('');
                    return true;
                }
            }

            minInput.on('input', validateTradeSize);
            maxInput.on('input', validateTradeSize);
        }

        function initializeTpLevels() {
            const container = $('#tpLevelsContainer');
            let tpCount = container.find('.input-group').length;

            $('#addTpLevel').off('click').on('click', function () {
                const html = `
                        <div class="input-group mb-2">
                            <span class="input-group-text">TP ${tpCount + 1}</span>
                            <input name="TpPercentages[${tpCount}]" class="form-control tp-level" type="number" step="0.1" min="0" max="100" value="0" />
                            <span class="input-group-text">%</span>
                            <button type="button" class="btn btn-danger remove-tp"><i class="bi bi-trash"></i></button>
                        </div>
                    `;
                container.append(html);
                tpCount++;
            });

            $(document).on('click', '.remove-tp', function () {
                if (container.find('.input-group').length > 1) {
                    $(this).closest('.input-group').remove();
                    // Renumber remaining TP levels
                    container.find('.input-group').each(function (index) {
                        $(this).find('.input-group-text').first().text(`TP ${index + 1}`);
                        $(this).find('input').attr('name', `TpPercentages[${index}]`);
                    });
                    tpCount--;
                }
            });
        }

        function validateForm() {
            const form = document.getElementById('providerSettingsForm');
            if (!form.checkValidity()) {
                form.reportValidity();
                return false;
            }

            // Custom validation for trade sizes
            const minInput = $('input[name="MinTradeSizeUsd"]');
            const maxInput = $('input[name="MaxTradeSizeUsd"]');
            const min = parseFloat(minInput.val()) || 0;
            const max = parseFloat(maxInput.val()) || 0;

            if (min > max) {
                Swal.fire('Validation Error', 'Maximum trade size must be greater than minimum trade size', 'error');
                return false;
            }

            return true;
        }
    });

})(jQuery);
