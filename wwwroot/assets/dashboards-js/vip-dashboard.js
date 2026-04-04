// Dashboard Initialization
function initializeDashboard() {
    // Initialize DataTables
    initializeDataTables();

    // Initialize charts
    if (typeof initializeCharts !== 'undefined') {
        initializeCharts(dashboardData);
    }

    // Initialize date range picker
    if (typeof initializeDateRangePicker !== 'undefined') {
        initializeDateRangePicker();
    }

    // Start real-time updates
    startRealTimeUpdates();

    // Clean up on page unload
    $(window).on('beforeunload', function () {
        if (window.refreshInterval) {
            clearInterval(window.refreshInterval);
        }
    });
}

// DataTable Initialization
function initializeDataTables() {
    $('#allPositionsTable').DataTable({
        pageLength: 10,
        order: [[9, 'desc']],
        language: {
            search: "_INPUT_",
            searchPlaceholder: "Search positions..."
        }
    });

    $('#openOrdersTable').DataTable({
        pageLength: 10,
        order: [[7, 'desc']],
        language: {
            search: "_INPUT_",
            searchPlaceholder: "Search orders..."
        },
        processing: false,
        serverSide: false,
        ajax: null
    });

    $('#allOrdersTable').DataTable({
        pageLength: 10,
        order: [[7, 'desc']],
        language: {
            search: "_INPUT_",
            searchPlaceholder: "Search orders..."
        }
    });
}

// Toast Notification System
function showToast(message, type) {
    const toastId = 'toast-' + Date.now();
    const bgClass = type === 'success' ? 'bg-success' :
        type === 'error' ? 'bg-danger' :
            type === 'warning' ? 'bg-warning' : 'bg-info';

    const toastHtml = `
        <div id="${toastId}" class="toast ${bgClass} text-white" role="alert" aria-live="assertive" aria-atomic="true">
            <div class="toast-body">
                <div class="d-flex align-items-center">
                    <div class="flex-grow-1">
                        ${message}
                    </div>
                    <button type="button" class="btn-close btn-close-white ms-2" data-bs-dismiss="toast" aria-label="Close"></button>
                </div>
            </div>
        </div>
    `;

    $('#toastContainer').append(toastHtml);
    const toastElement = $(`#${toastId}`)[0];
    const toast = new bootstrap.Toast(toastElement, { delay: 3000 });
    toast.show();

    $(toastElement).on('hidden.bs.toast', function () {
        $(this).remove();
    });
}

// Real-time Data Updates
function startRealTimeUpdates() {
    // Update every 5 seconds
    window.refreshInterval = setInterval(updateRealTimeData, 5000);

    // Initial update
    updateRealTimeData();
}

function updateRealTimeData() {
    $.ajax({
        url: window.location.origin + '/VipDashboard/GetDashboardData',
        type: 'GET',
        data: {
            userId: dashboardData.userId,
            startDate: $('#startDate').val() || dashboardData.startDate,
            endDate: $('#endDate').val() || dashboardData.endDate
        },
        success: function (response) {
            if (response.success) {
                // Update real-time P&L banner
                $('#totalOpenPnL').text('$' + response.totalOpenPnL.toFixed(2));
                $('#openPositionsCount').text(response.openPositionsCount);
                $('#openOrdersCount').text(response.openOrdersCount);

                // Update individual position P&L
                if (response.realTimePnL) {
                    response.realTimePnL.forEach(function (pnl) {
                        updatePositionRow(pnl);
                    });
                }
            }
        },
        error: function () {
            // Silently fail for real-time updates
        }
    });
}

function updatePositionRow(pnlData) {
    const row = $(`tr[data-position-id="${pnlData.positionId}"]`);
    if (row.length) {
        row.find(`#currentPrice_${pnlData.positionId}`).text('$' + pnlData.currentPrice.toFixed(2));
        row.find(`#currentPnL_${pnlData.positionId}`).text(
            (pnlData.currentPnL >= 0 ? '+' : '') + '$' + pnlData.currentPnL.toFixed(2)
        );
        row.find(`#currentROI_${pnlData.positionId}`).text(
            (pnlData.currentROI >= 0 ? '+' : '') + pnlData.currentROI.toFixed(2) + '%'
        );

        // Update text colors
        const pnlClass = pnlData.currentPnL >= 0 ? 'text-success' : 'text-danger';
        const roiClass = pnlData.currentROI >= 0 ? 'text-success' : 'text-danger';

        row.find(`#currentPnL_${pnlData.positionId}`).removeClass('text-success text-danger').addClass(pnlClass);
        row.find(`#currentROI_${pnlData.positionId}`).removeClass('text-success text-danger').addClass(roiClass);
    }
}

function getRequestVerificationToken() {
    return document.querySelector('#vipDashboardAntiforgeryForm input[name="__RequestVerificationToken"]')?.value
        || document.querySelector('input[name="__RequestVerificationToken"]')?.value
        || '';
}

// Position Management
function closePosition(positionId) {
    if (!confirm('Are you sure you want to close this position?')) return;

    $.ajax({
        url: window.location.origin + '/VipDashboard/ClosePosition',
        type: 'POST',
        data: { positionId: positionId },
        headers: {
            RequestVerificationToken: getRequestVerificationToken()
        },
        beforeSend: function () {
            showToast('Closing position...', 'info');
        },
        success: function (response) {
            if (response.success) {
                showToast(response.message, 'success');
                // Remove the row from the table
                $(`tr[data-position-id="${positionId}"]`).fadeOut(300, function () {
                    $(this).remove();
                    updateRealTimeData();
                });
            } else {
                showToast(response.message, 'error');
            }
        },
        error: function (xhr) {
            showToast('Error closing position: ' + (xhr.responseJSON?.message || 'Network error'), 'error');
        }
    });
}

function closeAllPositions() {
    if (!confirm('Are you sure you want to close ALL open positions?')) return;

    $.ajax({
        url: window.location.origin + '/VipDashboard/CloseAllPositions',
        type: 'POST',
        headers: {
            RequestVerificationToken: getRequestVerificationToken()
        },
        beforeSend: function () {
            showToast('Closing all positions...', 'info');
        },
        success: function (response) {
            showToast(response.message, response.success ? 'success' : 'warning');
            if (response.success) {
                // Refresh the entire page after a delay
                setTimeout(function () {
                    location.reload();
                }, 2000);
            }
        },
        error: function (xhr) {
            showToast('Error closing positions: ' + (xhr.responseJSON?.message || 'Network error'), 'error');
        }
    });
}

// Order Management
function cancelOrder(orderId) {
    if (!confirm('Are you sure you want to cancel this order?')) return;

    $.ajax({
        url: window.location.origin + '/VipDashboard/CancelOrder',
        type: 'POST',
        data: { orderId: orderId },
        headers: {
            RequestVerificationToken: getRequestVerificationToken()
        },
        beforeSend: function () {
            showToast('Cancelling order...', 'info');
        },
        success: function (response) {
            if (response.success) {
                showToast(response.message, 'success');

                // Remove the row from the table
                const row = $(`tr[data-order-id="${orderId}"]`);
                if (row.length) {
                    // Remove the row with animation
                    row.fadeOut(300, function () {
                        $(this).remove();

                        // Get the current count of rows in the table body
                        const rowCount = $('#openOrdersTable tbody tr').filter(function () {
                            return $(this).find('td').length > 1;
                        }).length;

                        // Update the open orders count in the banner
                        $('#openOrdersCount').text(rowCount);

                        // Update the tab header count
                        const tabLink = $('a[href="#open-orders"]');
                        const baseText = tabLink.text().replace(/\s*\(\d+\)\s*$/, '').trim();
                        tabLink.text(`${baseText} (${rowCount})`);

                        // If no more open orders, show message
                        if (rowCount === 0) {
                            // Clear existing content
                            $('#openOrdersTable tbody').empty();
                            const emptyMessage = `
                                <tr>
                                    <td colspan="9" class="text-center text-muted py-4">
                                        <i class="ti ti-checkbox" style="font-size: 2rem;"></i>
                                        <p class="mt-2">No open orders</p>
                                    </td>
                                </tr>
                            `;
                            $('#openOrdersTable tbody').append(emptyMessage);
                        }
                    });
                }
            } else {
                showToast(response.message, 'error');
            }
        },
        error: function (xhr) {
            showToast('Error cancelling order: ' + (xhr.responseJSON?.message || 'Network error'), 'error');
        }
    });
}

// Dashboard Refresh
function refreshDashboard() {
    const startDate = $('#startDate').val();
    const endDate = $('#endDate').val();

    let url = window.location.origin + '/VipDashboard/Index';
    url += '?userId=' + encodeURIComponent(dashboardData.userId);

    if (startDate && endDate) {
        url += '&startDate=' + encodeURIComponent(startDate);
        url += '&endDate=' + encodeURIComponent(endDate);
    }

    showToast('Refreshing dashboard...', 'info');
    window.location.href = url;
}

// Date Range Management
function formatDateForUrl(date) {
    return date.toISOString().split('T')[0]; // Returns YYYY-MM-DD
}

function updateDashboardWithDateRange(startDate, endDate) {
    const url = new URL(window.location);
    url.searchParams.set('startDate', startDate);
    url.searchParams.set('endDate', endDate);

    // Preserve userId if present (admin viewing another user)
    const userId = url.searchParams.get('userId');
    if (!userId && dashboardData && dashboardData.userId) {
        url.searchParams.set('userId', dashboardData.userId);
    }

    // Remove timeframe so it doesn't conflict with explicit date range
    url.searchParams.delete('timeframe');

    // Show loading toast and redirect
    showToast('Loading data for selected date range...', 'info');
    setTimeout(() => {
        window.location.href = url.toString();
    }, 500);
}

function initializeDateRangePicker() {
    const startDate = new Date(dashboardData.startDate);
    const endDate = new Date(dashboardData.endDate);

    flatpickr("#daterange", {
        mode: "range",
        dateFormat: "m/d/Y",
        defaultDate: [startDate, endDate],
        maxDate: "today",
        onChange: function (selectedDates, dateStr, instance) {
            if (selectedDates.length === 2) {
                // Format dates for URL
                const start = formatDateForUrl(selectedDates[0]);
                const end = formatDateForUrl(selectedDates[1]);

                // Update URL and refresh
                updateDashboardWithDateRange(start, end);
            }
        }
    });
}

// Charts
window.initializeCharts = function (data) {
    // Position Distribution Pie Chart
    if (data.openPositionsCount > 0 || data.closedPositionsCount > 0) {
        var positionOptions = {
            series: [data.openPositionsCount, data.closedPositionsCount],
            chart: {
                type: "pie",
                height: 300
            },
            colors: ["#5c67f7", "#e354d4"],
            labels: ["Open Positions", "Closed Positions"],
            legend: {
                position: "bottom",
                labels: {
                    colors: '#8c9097'
                }
            },
            dataLabels: {
                enabled: true,
                formatter: function (val, opts) {
                    return opts.w.config.series[opts.seriesIndex];
                },
                style: {
                    colors: ['#fff']
                }
            },
            responsive: [{
                breakpoint: 480,
                options: {
                    chart: {
                        height: 250
                    },
                    legend: {
                        position: "bottom"
                    }
                }
            }]
        };

        var positionChart = new ApexCharts(document.querySelector("#positionsChart"), positionOptions);
        positionChart.render();
    }

    // ROI Over Time Area Chart
    if (data.roiOverTime && data.roiOverTime.length > 0) {
        var roiOptions = {
            series: [{
                name: "Total ROI",
                data: data.roiOverTime.map(item => ({
                    x: new Date(item.date).getTime(),
                    y: item.totalROI
                }))
            }],
            chart: {
                type: 'area',
                height: 300,
                zoom: {
                    enabled: false
                },
                toolbar: {
                    show: true
                }
            },
            dataLabels: {
                enabled: false
            },
            stroke: {
                curve: 'smooth',
                width: 2
            },
            fill: {
                type: 'gradient',
                gradient: {
                    shadeIntensity: 1,
                    opacityFrom: 0.7,
                    opacityTo: 0.3,
                    stops: [0, 90, 100]
                }
            },
            colors: ["#5c67f7"],
            xaxis: {
                type: 'datetime',
                labels: {
                    style: {
                        colors: '#8c9097'
                    }
                }
            },
            yaxis: {
                title: {
                    text: 'ROI (%)',
                    style: {
                        color: '#8c9097'
                    }
                },
                labels: {
                    formatter: function (val) {
                        return val.toFixed(2) + "%";
                    },
                    style: {
                        colors: '#8c9097'
                    }
                }
            },
            tooltip: {
                x: {
                    format: 'dd MMM yyyy'
                }
            },
            grid: {
                borderColor: '#2a2e3f'
            }
        };

        var roiChart = new ApexCharts(document.querySelector("#roiChart"), roiOptions);
        roiChart.render();
    }

    // Win Rate Radial Charts
    // Overall Win Rate
    var winrateOptions = {
        series: [data.winRate],
        chart: {
            height: 250,
            type: 'radialBar'
        },
        plotOptions: {
            radialBar: {
                hollow: {
                    size: '60%',
                },
                dataLabels: {
                    name: {
                        show: true,
                        fontSize: '16px',
                        color: '#8c9097',
                        offsetY: -10
                    },
                    value: {
                        show: true,
                        fontSize: '24px',
                        color: '#ffffff',
                        offsetY: 0,
                        formatter: function (val) {
                            return val.toFixed(1) + "%";
                        }
                    }
                }
            }
        },
        colors: [data.winRate >= 50 ? '#10b981' : '#ef4444'],
        stroke: {
            lineCap: 'round'
        },
        labels: ['Win Rate']
    };

    var winrateChart = new ApexCharts(document.querySelector("#winLossChart"), winrateOptions);
    winrateChart.render();

    // Long Win Rate
    var longWinrateOptions = {
        series: [data.winRateLong],
        chart: {
            height: 250,
            type: 'radialBar'
        },
        plotOptions: {
            radialBar: {
                hollow: {
                    size: '60%',
                },
                dataLabels: {
                    name: {
                        show: true,
                        fontSize: '14px',
                        color: '#8c9097',
                        offsetY: -10
                    },
                    value: {
                        show: true,
                        fontSize: '20px',
                        color: '#ffffff',
                        offsetY: 0,
                        formatter: function (val) {
                            return val.toFixed(1) + "%";
                        }
                    }
                }
            }
        },
        colors: [data.winRateLong >= 50 ? '#10b981' : '#ef4444'],
        stroke: {
            lineCap: 'round'
        },
        labels: ['Long Win Rate']
    };

    var longWinrateChart = new ApexCharts(document.querySelector("#longWinrateChart"), longWinrateOptions);
    longWinrateChart.render();

    // Short Win Rate
    var shortWinrateOptions = {
        series: [data.winRateShort],
        chart: {
            height: 250,
            type: 'radialBar'
        },
        plotOptions: {
            radialBar: {
                hollow: {
                    size: '60%',
                },
                dataLabels: {
                    name: {
                        show: true,
                        fontSize: '14px',
                        color: '#8c9097',
                        offsetY: -10
                    },
                    value: {
                        show: true,
                        fontSize: '20px',
                        color: '#ffffff',
                        offsetY: 0,
                        formatter: function (val) {
                            return val.toFixed(1) + "%";
                        }
                    }
                }
            }
        },
        colors: [data.winRateShort >= 50 ? '#10b981' : '#ef4444'],
        stroke: {
            lineCap: 'round'
        },
        labels: ['Short Win Rate']
    };

    var shortWinrateChart = new ApexCharts(document.querySelector("#shortWinrateChart"), shortWinrateOptions);
    shortWinrateChart.render();

    // ROI by Symbol Bar Chart
    if (data.roiBySymbol && data.roiBySymbol.length > 0) {
        // Sort by ROI and take top 10
        var sortedSymbols = [...data.roiBySymbol]
            .sort((a, b) => b.avgROI - a.avgROI)
            .slice(0, 10);

        var options = {
            series: [{
                name: 'Average ROI',
                data: sortedSymbols.map(item => item.avgROI)
            }],
            chart: {
                type: 'bar',
                height: 300
            },
            plotOptions: {
                bar: {
                    borderRadius: 4,
                    horizontal: true,
                    distributed: true,
                    dataLabels: {
                        position: 'center'
                    }
                }
            },
            colors: sortedSymbols.map(item =>
                item.avgROI >= 0 ? '#10b981' : '#ef4444'
            ),
            dataLabels: {
                enabled: true,
                formatter: function (val) {
                    return val.toFixed(2) + "%";
                },
                style: {
                    colors: ['#fff'],
                    fontSize: '12px'
                }
            },
            xaxis: {
                categories: sortedSymbols.map(item => item.symbol),
                labels: {
                    style: {
                        colors: '#8c9097'
                    }
                }
            },
            yaxis: {
                labels: {
                    style: {
                        colors: '#8c9097'
                    }
                }
            },
            grid: {
                borderColor: '#2a2e3f'
            },
            tooltip: {
                y: {
                    formatter: function (val) {
                        return val.toFixed(2) + "%";
                    }
                }
            }
        };

        var roiBySymbolChart = new ApexCharts(document.querySelector("#roiBySymbolChart"), options);
        roiBySymbolChart.render();
    }
};

// Make functions globally available
window.showToast = showToast;
window.closePosition = closePosition;
window.closeAllPositions = closeAllPositions;
window.cancelOrder = cancelOrder;
window.refreshDashboard = refreshDashboard;
window.initializeDashboard = initializeDashboard;
