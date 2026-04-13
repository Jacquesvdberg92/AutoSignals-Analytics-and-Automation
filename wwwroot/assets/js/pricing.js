(function () {
    "use strict";

    var checkbox = document.getElementById("pricingToggle");
    if (!checkbox) return;

    function applyBillingCycle(annual) {
        document.querySelectorAll(".billing-monthly").forEach(function (el) {
            el.style.display = annual ? "none" : "";
        });
        document.querySelectorAll(".billing-annual").forEach(function (el) {
            el.style.display = annual ? "" : "none";
        });
        // legacy single-element support
        var yearly1 = document.getElementById("yearly1");
        var monthly1 = document.getElementById("monthly1");
        if (yearly1)  { yearly1.style.display  = annual ? "" : "none"; }
        if (monthly1) { monthly1.style.display = annual ? "none" : ""; }
    }

    checkbox.addEventListener("change", function () {
        applyBillingCycle(checkbox.checked);
    });

    // initialise — ensure annual elements are hidden on load
    applyBillingCycle(false);

})();