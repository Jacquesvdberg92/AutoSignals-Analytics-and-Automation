(function () {
    "use strict";

    window.initializeCharts = function (data) {
        // Pie Chart: Position Distribution
        var positionOptions = {
            series: [
                data.openPositionsCount,
                data.closedPositionsCount
            ],
            chart: {
                type: "pie",
                height: 300
            },
            colors: ["#5c67f7", "#e354d4"], // Blue for Open, Pink for Closed
            labels: ["Open Positions", "Closed Positions"],
            legend: {
                position: "bottom"
            },
            dataLabels: {
                formatter: function (val, opts) {
                    // Return the raw count value from the series
                    return opts.w.config.series[opts.seriesIndex];
                },
                dropShadow: {
                    enabled: false
                }
            },
            title: {
                text: "Position Distribution",
                align: "center",
                style: {
                    fontSize: "16px",
                    fontWeight: "bold",
                    color: "#8c9097"
                }
            }
        };
        var positionChart = new ApexCharts(document.querySelector("#positionsChart"), positionOptions);
        positionChart.render();

        // Pie Chart: Orders Distribution
        var ordersOptions = {
            series: [
                data.openOrdersCount,
                data.closedOrdersCount,
                data.pendingOrderCount,
                data.cancelledOrderCount
            ],
            chart: {
                type: "pie",
                height: 300
            },
            colors: ["#5c67f7", "#e354d4", "#ff8e6f", "#0ca3e7"],
            labels: ["Open Orders", "Closed Orders", "Pending Orders", "Cancelled Orders"],
            legend: {
                position: "bottom"
            },
            dataLabels: {
                formatter: function (val, opts) {
                    // Return the raw count value from the series
                    return opts.w.config.series[opts.seriesIndex];
                },
                dropShadow: {
                    enabled: false
                }
            },
            title: {
                text: "Orders Distribution",
                align: "center",
                style: {
                    fontSize: "16px",
                    fontWeight: "bold",
                    color: "#8c9097"
                }
            }
        };
        var ordersChart = new ApexCharts(document.querySelector("#ordersChart"), ordersOptions);
        ordersChart.render();

        // Area Chart: ROI Statistics Over Time
        var roiOptions = {
            series: [{
                name: "Total ROI",
                data: data.roiOverTime.map(item => ({ x: item.date, y: item.totalROI }))
            }, {
                name: "Average ROI",
                data: data.roiOverTime.map(item => ({ x: item.date, y: item.averageROI }))
            }, {
                name: "Open ROI",
                data: data.roiOverTime.map(item => ({ x: item.date, y: item.openROI }))
            }, {
                name: "Closed ROI",
                data: data.roiOverTime.map(item => ({ x: item.date, y: item.closedROI }))
            }],
            chart: {
                type: 'area',
                height: 300,
                zoom: {
                    enabled: false
                }
            },
            dataLabels: {
                enabled: false
            },
            stroke: {
                curve: 'smooth'
            },
            title: {
                text: 'ROI Statistics Over Time',
                align: 'left',
                style: {
                    fontSize: '16px',
                    fontWeight: 'bold',
                    color: '#8c9097'
                }
            },
            xaxis: {
                type: 'datetime',
                labels: {
                    style: {
                        colors: "#8c9097",
                        fontSize: '11px',
                        fontWeight: 600
                    }
                }
            },
            yaxis: {
                title: {
                    text: 'ROI (%)',
                    style: {
                        color: "#8c9097"
                    }
                },
                labels: {
                    formatter: function (val) {
                        return val.toFixed(2) + "%";
                    },
                    style: {
                        colors: "#8c9097",
                        fontSize: '11px',
                        fontWeight: 600
                    }
                }
            },
            colors: ["#5c67f7", "#e354d4", "#ff8e6f", "#0ca3e7"], // Add new colors for Open/Closed ROI
            fill: {
                type: 'gradient',
                gradient: {
                    shadeIntensity: 1,
                    opacityFrom: 0.2,
                    opacityTo: 0.3,
                    stops: [0, 100]
                }
            },
            tooltip: {
                shared: true,
                x: {
                    format: 'dd MMM yyyy'
                }
            }
        };

        var roiChart = new ApexCharts(document.querySelector("#roiChart"), roiOptions);
        roiChart.render();

        // Radial Bar Chart: Winrate
        var winrateOptions = {
            series: [data.winRate], // Win rate percentage
            chart: {
                height: 320,
                type: 'radialBar',
                toolbar: {
                    show: true
                }
            },
            plotOptions: {
                radialBar: {
                    startAngle: -135,
                    endAngle: 225,
                    hollow: {
                        margin: 0,
                        size: '70%',
                        background: '#fff',
                        dropShadow: {
                            enabled: false,
                            top: 3,
                            left: 0,
                            blur: 4,
                            opacity: 0.24
                        }
                    },
                    track: {
                        background: '#fff',
                        strokeWidth: '67%',
                        dropShadow: {
                            enabled: false,
                            top: -3,
                            left: 0,
                            blur: 4,
                            opacity: 0.35
                        }
                    },
                    dataLabels: {
                        show: true,
                        name: {
                            offsetY: -10,
                            show: true,
                            color: '#888',
                            fontSize: '17px',
                            text: 'Winrate'
                        },
                        value: {
                            formatter: function (val) {
                                return val.toFixed(2) + "%";
                            },
                            color: '#111',
                            fontSize: '36px',
                            show: true
                        }
                    }
                }
            },
            fill: {
                type: 'gradient',
                gradient: {
                    shade: 'light',
                    type: 'horizontal',
                    shadeIntensity: 0.5,
                    gradientToColors: ['#A7D477'], // Pastel green
                    inverseColors: false,
                    opacityFrom: 1,
                    opacityTo: 1,
                    stops: [0, 50, 100], // Smooth transition from red to green
                    colorStops: [
                        { offset: 0, color: '#F72C5B', opacity: 1 }, // Pastel red
                        { offset: 30, color: '#FFD3B6', opacity: 1 }, // Pastel orange
                        { offset: 100, color: '#A7D477', opacity: 1 } // Pastel green
                    ]
                }
            },
            stroke: {
                lineCap: 'round'
            },
            labels: ['Winrate']
        };

        var winrateChart = new ApexCharts(document.querySelector("#winLossChart"), winrateOptions);
        winrateChart.render();

        // Radial Bar Chart: Long Winrate
        var longWinrateOptions = {
            series: [data.winRateLong], // Long win rate percentage
            chart: {
                height: 200, // Increased height for better proportions
                type: 'radialBar',
                toolbar: {
                    show: false // Disabled toolbar for a cleaner look
                }
            },
            plotOptions: {
                radialBar: {
                    startAngle: -135,
                    endAngle: 225,
                    hollow: {
                        margin: 0,
                        size: '65%', // Adjusted size for better proportions
                        background: '#fff',
                        dropShadow: {
                            enabled: false,
                            top: 3,
                            left: 0,
                            blur: 4,
                            opacity: 0.24
                        }
                    },
                    track: {
                        background: '#fff',
                        strokeWidth: '67%',
                        dropShadow: {
                            enabled: false,
                            top: -3,
                            left: 0,
                            blur: 4,
                            opacity: 0.35
                        }
                    },
                    dataLabels: {
                        show: true,
                        name: {
                            offsetY: -5, // Adjusted offset for better alignment
                            show: true,
                            color: '#888',
                            fontSize: '12px', // Increased font size for better readability
                            text: 'Long Winrate'
                        },
                        value: {
                            formatter: function (val) {
                                return val.toFixed(2) + "%";
                            },
                            color: '#111',
                            fontSize: '22px', // Increased font size for better emphasis
                            show: true
                        }
                    }
                }
            },
            fill: {
                type: 'gradient',
                gradient: {
                    shade: 'light',
                    type: 'horizontal',
                    shadeIntensity: 0.5,
                    gradientToColors: ['#A7D477'], // Pastel green
                    inverseColors: false,
                    opacityFrom: 1,
                    opacityTo: 1,
                    stops: [0, 50, 100]
                }
            },
            stroke: {
                lineCap: 'round'
            },
            labels: ['Long Winrate']
        };

        var longWinrateChart = new ApexCharts(document.querySelector("#longWinrateChart"), longWinrateOptions);
        longWinrateChart.render();

        // Radial Bar Chart: Short Winrate
        var shortWinrateOptions = {
            series: [data.winRateShort], // Short win rate percentage
            chart: {
                height: 200, // Increased height for better proportions
                type: 'radialBar',
                toolbar: {
                    show: false // Disabled toolbar for a cleaner look
                }
            },
            plotOptions: {
                radialBar: {
                    startAngle: -135,
                    endAngle: 225,
                    hollow: {
                        margin: 0,
                        size: '65%', // Adjusted size for better proportions
                        background: '#fff',
                        dropShadow: {
                            enabled: false,
                            top: 3,
                            left: 0,
                            blur: 4,
                            opacity: 0.24
                        }
                    },
                    track: {
                        background: '#fff',
                        strokeWidth: '67%',
                        dropShadow: {
                            enabled: false,
                            top: -3,
                            left: 0,
                            blur: 4,
                            opacity: 0.35
                        }
                    },
                    dataLabels: {
                        show: true,
                        name: {
                            offsetY: -5, // Adjusted offset for better alignment
                            show: true,
                            color: '#888',
                            fontSize: '12px', // Increased font size for better readability
                            text: 'Short Winrate'
                        },
                        value: {
                            formatter: function (val) {
                                return val.toFixed(2) + "%";
                            },
                            color: '#111',
                            fontSize: '22px', // Increased font size for better emphasis
                            show: true
                        }
                    }
                }
            },
            fill: {
                type: 'gradient',
                gradient: {
                    shade: 'light',
                    type: 'horizontal',
                    shadeIntensity: 0.5,
                    gradientToColors: ['#A7D477'], // Pastel green
                    inverseColors: false,
                    opacityFrom: 1,
                    opacityTo: 1,
                    stops: [0, 50, 100]
                }
            },
            stroke: {
                lineCap: 'round'
            },
            labels: ['Short Winrate']
        };

        var shortWinrateChart = new ApexCharts(document.querySelector("#shortWinrateChart"), shortWinrateOptions);
        shortWinrateChart.render();

        // Bar Chart: ROI by Symbol
        if (data.roiBySymbol && data.roiBySymbol.length > 0) {
            var options = {
                series: [{
                    name: 'Average ROI',
                    data: data.roiBySymbol.map(item => item.avgROI)
                }],
                chart: {
                    type: 'bar',
                    height: 320
                },
                plotOptions: {
                    bar: {
                        colors: {
                            ranges: [
                                {
                                    from: -10000,
                                    to: 0,
                                    color: '#fe5454' // Red for negative ROI
                                },
                                {
                                    from: 0,
                                    to: 10000,
                                    color: '#5c67f7' // Blue for positive ROI
                                }
                            ]
                        },
                        columnWidth: '80%'
                    }
                },
                grid: {
                    borderColor: '#f2f5f7'
                },
                colors: ["#5c67f7"], // Default color
                dataLabels: {
                    enabled: false
                },
                yaxis: {
                    title: {
                        text: 'ROI (%)',
                        style: {
                            color: "#8c9097"
                        }
                    },
                    labels: {
                        formatter: function (y) {
                            return y.toFixed(2) + "%";
                        },
                        style: {
                            colors: "#8c9097",
                            fontSize: '11px',
                            fontWeight: 600,
                            cssClass: 'apexcharts-yaxis-label'
                        }
                    }
                },
                xaxis: {
                    categories: data.roiBySymbol.map(item => item.symbol),
                    labels: {
                        rotate: -45,
                        style: {
                            colors: "#8c9097",
                            fontSize: '11px',
                            fontWeight: 600,
                            cssClass: 'apexcharts-xaxis-label'
                        }
                    }
                },
                tooltip: {
                    y: {
                        formatter: function (val) {
                            return val + "%";
                        }
                    }
                }
            };

            var roiBySymbolChart = new ApexCharts(document.querySelector("#roiBySymbolChart"), options);
            roiBySymbolChart.render();
        }

        $(document).ready(function () {
            $('#positionsTable').DataTable({
                responsive: true,
                pageLength: 10,
                lengthChange: false
            }).buttons().container().appendTo('#positionsTable_wrapper .col-md-6:eq(0)');

            $('#allPositionsTable').DataTable({
                responsive: true,
                pageLength: 10,
                lengthChange: false
            }).buttons().container().appendTo('#allPositionsTable_wrapper .col-md-6:eq(0)');

            $('#openOrdersTable').DataTable({
                responsive: true,
                pageLength: 10,
                lengthChange: false
            }).buttons().container().appendTo('#openOrdersTable_wrapper .col-md-6:eq(0)');

            $('#allOrdersTable').DataTable({
                responsive: true,
                pageLength: 10,
                lengthChange: false
            }).buttons().container().appendTo('#allOrdersTable_wrapper .col-md-6:eq(0)');
        });
    };
})();
