(function () {
    "use strict";

    /* Form Wizard 1 */
    let args = {
        "wz_class": ".wizard-tab",
        highlight: true,
        highlight_time: 1000,
    };
    const wizard = new Wizard1(args);
    wizard.init();
    /* Form Wizard 1 */

    /* Data Picker */
    if (document.querySelector("#date")) {
        flatpickr("#date", {});
    }
    /* Data Picker */

    /* Form Wizard with validation */
    if (document.querySelector("#basicwizard")) {
        new Wizard("#basicwizard", {
            validate: true,
        });
    }
    /* Form Wizard with validation */

    /* Wizard with Progress */
    if (document.querySelector("#progresswizard")) {
        new Wizard("#progresswizard", {
            validate: true,
            progress: true
        });
    }
    /* Wizard with Progress */
})();