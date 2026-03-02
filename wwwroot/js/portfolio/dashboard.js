(function () {
    'use strict';

    function attachAddHoldingEvents() {
        $('#assetSelect').off('change.dashboard').on('change.dashboard', function () {
            const symbol = $(this).val();
            if (symbol) {
                getCurrentPrice(symbol, function (price) {
                    if (price > 0) {
                        $('#pricePreview').removeClass('d-none');
                        $('#currentPriceDisplay').text('$' + parseFloat(price).toFixed(2));
                    }
                });
            } else {
                $('#pricePreview').addClass('d-none');
            }
        });
    }

    function getCurrentPrice(symbol, callback) {
        $.ajax({
            url: window.portfolioDashboardUrls.getCurrentPrice,
            type: 'GET',
            data: { symbol: symbol },
            success: function (data) {
                if (callback && typeof callback === 'function') callback(data.price);
            },
            error: function () {
                if (callback && typeof callback === 'function') callback(0);
            }
        });
    }

    window.loadCreateModal = function () {
        $.ajax({
            url: window.portfolioDashboardUrls.create,
            type: 'GET',
            success: function (data) {
                $('#modals-container').html(data);
                $('#createPortfolioModal').modal('show');
            }
        });
    };

    window.loadAddHoldingModal = function (portfolioId) {
        $.ajax({
            url: window.portfolioDashboardUrls.addHolding,
            type: 'GET',
            data: { portfolioId: portfolioId },
            success: function (data) {
                $('#modals-container').html(data);
                $('#addHoldingModal').modal('show');
                attachAddHoldingEvents();
            }
        });
    };

    window.loadEditModal = function (holdingId) {
        $.ajax({
            url: window.portfolioDashboardUrls.editHolding,
            type: 'GET',
            data: { id: holdingId },
            success: function (data) {
                $('#modals-container').html(data);
                $('#editHoldingModal').modal('show');
            }
        });
    };

    window.loadManageAssetModal = function (portfolioId, symbol) {
        $.ajax({
            url: window.portfolioDashboardUrls.manageAsset,
            type: 'GET',
            data: { portfolioId: portfolioId, symbol: symbol },
            success: function (data) {
                $('#modals-container').html(data);
                $('#manageAssetModal').modal('show');
            }
        });
    };

    window.loadRenamePortfolioModal = function (portfolioId) {
        $.ajax({
            url: window.portfolioDashboardUrls.rename,
            type: 'GET',
            data: { id: portfolioId },
            success: function (data) {
                $('#modals-container').html(data);
                $('#renamePortfolioModal').modal('show');
            }
        });
    };

    window.loadDeletePortfolioModal = function (portfolioId) {
        $.ajax({
            url: window.portfolioDashboardUrls.delete,
            type: 'GET',
            data: { id: portfolioId },
            success: function (data) {
                $('#modals-container').html(data);
                $('#deletePortfolioModal').modal('show');
            }
        });
    };

    // Keep modal forms submitting via AJAX
    $(document).on('submit.dashboard', 'form', function (e) {
        const $form = $(this);
        const action = $form.attr('action') || '';

        if (action.includes('AddHolding') || action.includes('Create') || action.includes('Rename') || action.includes('Delete')) {
            e.preventDefault();

            $.ajax({
                url: action,
                type: 'POST',
                data: $form.serialize(),
                success: function (data) {
                    if (typeof data === 'string' && data.trim().startsWith('<')) {
                        $('#modals-container').html(data);
                        $.validator.unobtrusive.parse('form');

                        if (action.includes('AddHolding')) attachAddHoldingEvents();

                        const modalId = $form.closest('.modal').attr('id');
                        if (modalId) $('#' + modalId).modal('show');
                    } else if (data && data.redirectUrl) {
                        window.location.href = data.redirectUrl;
                    } else {
                        window.location.reload();
                    }
                },
                error: function () {
                    alert('An error occurred. Please try again.');
                }
            });
        }
    });

    // Auto-refresh prices every 50 minutes (3000000 ms)
    setInterval(function () {
        if (window.location.pathname.includes('/Portfolio/Dashboard')) window.location.reload();
    }, 3000000);

    // Tooltips
    $(function () {
        $('[data-bs-toggle="tooltip"]').tooltip();
    });
})();