(function () {
    "use strict";

    $(document).ready(function () {
        // Bind delete button click event
        $(document).on("click", ".delete-rule-ajax", function (e) {
            e.preventDefault();

            const ruleId = $(this).data("rule-id");
            const ruleElement = $(this).closest(".list-group-item");

            if (!ruleId) return;

            if (confirm("Are you sure you want to delete this rule?")) {
                $.ajax({
                    url: document.body.dataset.deleteRuleAjaxUrl,
                    type: "POST",
                    data: {
                        id: ruleId,
                        __RequestVerificationToken: $("input[name=\"__RequestVerificationToken\"]").val()
                    },
                    success: function (response) {
                        if (response && response.success) {
                            // Fade out and remove the deleted rule
                            ruleElement.fadeOut(300, function () {
                                $(this).remove();
                                showAlert("Rule deleted successfully", "success");
                            });
                        } else {
                            showAlert("Delete failed: " + (response?.message ?? "Unknown error"), "danger");
                        }
                    },
                    error: function () {
                        showAlert("Delete request failed", "danger");
                    }
                });
            }
        });

        function showAlert(message, type) {
            const alertHtml =
                "<div class=\"alert alert-" + type + " alert-dismissible fade show\" role=\"alert\">" +
                "<button type=\"button\" class=\"btn-close\" data-bs-dismiss=\"alert\" aria-label=\"Close\"></button>" +
                message +
                "</div>";

            // Add or update alert message
            const existingAlert = $(".alert-dismissible");
            if (existingAlert.length) {
                existingAlert.replaceWith(alertHtml);
            } else {
                $(".card-body").prepend(alertHtml);
            }

            // Auto-dismiss after 3 seconds
            setTimeout(function () {
                $(".alert-dismissible").alert("close");
            }, 3000);
        }
    });
})();