function createWinRateChartOptions(series, label, color) {
    return {
        series: [series],
        chart: {
            type: 'radialBar',
            height: 150,
            offsetX: 0, // Center horizontally
            offsetY: 0  // Center vertically
        },
        plotOptions: {
            radialBar: {
                startAngle: -135,
                endAngle: 135,
                hollow: {
                    size: '70%'
                },
                dataLabels: {
                    name: {
                        fontSize: '80%'
                    },
                    value: {
                        fontSize: '100%'
                    }
                }
            }
        },
        fill: {
            type: 'gradient',
            gradient: {
                shade: 'dark',
                shadeIntensity: 0.15,
                inverseColors: false,
                opacityFrom: 1,
                opacityTo: 1,
                stops: [0, 50, 65, 91]
            },
        },
        stroke: {
            dashArray: 4
        },
        labels: [label],
        colors: [color]
    };
}



function createTpBreakdownChartOptions(series, labels) {
    return {
        series: series,
        chart: {
            type: 'donut',
            height: 300,
            offsetY: 0
        },
        plotOptions: {
            pie: {
                dataLabels: {
                    enabled: false
                }
            }
        },
        responsive: [{
            breakpoint: 480,
            options: {
                chart: {
                    width: 200
                },
                legend: {
                    show: false
                }
            }
        }],
        legend: {
            show: false,
            fontSize: '10px', // Adjust the font size to make the legend smaller
            markers: {
                width: 10, // Adjust the marker size
                height: 10 // Adjust the marker size
            },
            itemMargin: {
                horizontal: 5,
                vertical: 5
            }
        },
        labels: labels,     
    };
}

function initializeGaugeChart(elementId, initialValue) {
    var chartDom = document.getElementById(elementId);
    var myChart = echarts.init(chartDom);

    // Responsive font size helper
    function getFontSize(base) {
        // Use smaller font on small screens
        return window.innerWidth < 576 ? base * 0.8 : base;
    }

    var option = {
        title: {
            text: "Risk Level",
            left: 'center',
            top: '0%',
            textStyle: {
                fontSize: getFontSize(16),
                fontWeight: 'bold'
            }
        },
        series: [
            {
                type: 'gauge',
                progress: {
                    show: true,
                    width: 9
                },
                axisLine: {
                    lineStyle: {
                        width: 9
                    }
                },
                axisTick: {
                    show: false
                },
                splitLine: {
                    length: 2,
                    lineStyle: {
                        width: 2,
                        color: '#999'
                    }
                },
                axisLabel: {
                    distance: 5,
                    color: '#999',
                    fontSize: getFontSize(8)
                },
                anchor: {
                    show: true,
                    showAbove: true,
                    size: 5,
                    itemStyle: {
                        borderWidth: 10
                    }
                },
                title: {
                    show: true,
                    fontSize: getFontSize(12)
                },
                detail: {
                    valueAnimation: true,
                    fontSize: getFontSize(18),
                    offsetCenter: [0, '70%']
                },
                data: [
                    {
                        value: initialValue
                    }
                ]
            }
        ]
    };

    myChart.setOption(option);

    // Make chart responsive
    window.addEventListener('resize', function () {
        // Update font sizes on resize
        option.title.textStyle.fontSize = getFontSize(16);
        option.series[0].axisLabel.fontSize = getFontSize(8);
        option.series[0].title.fontSize = getFontSize(12);
        option.series[0].detail.fontSize = getFontSize(18);
        myChart.setOption(option);
        myChart.resize();
    });

    return myChart;
}


function createStackedBarChartOptions(series) {
    return {
        series: series,
        chart: {
            type: 'bar',
            height: 150,
            stacked: true,
            /*stackType: '100%',*/
            toolbar: {
                show: false // Hide the burger menu
            }
        },
        plotOptions: {
            bar: {
                horizontal: true,
            },
        },
        stroke: {
            width: 1,
            colors: ['#fff']
        },
        title: {
            text: 'Take Profits Achieved'
        },
        xaxis: {
            categories: ['Category'], // Single category
            labels: {
                show: false // Hide x-axis labels
            }
        },
        yaxis: {
            labels: {
                show: false // Hide y-axis labels
            }
        },
        tooltip: {
            y: {
                formatter: function (val) {
                    return val + "%";
                }
            }
        },
        fill: {
            opacity: 1
        },
        legend: {
            position: 'top',
            horizontalAlign: 'left',
            offsetX: 40
        },
        dataLabels: {
            enabled: true,
            formatter: function (val) {
                return val + "%"; // Show the same value as the series
            },
            style: {
                fontSize: '12px',
                colors: ['#000']
            }
        }
    };
}

function initializeStackedBarChart(elementId, series) {
    var chartDom = document.querySelector(elementId);
    var options = createStackedBarChartOptions(series);
    var chart = new ApexCharts(chartDom, options);
    chart.render();
    return chart;
}



