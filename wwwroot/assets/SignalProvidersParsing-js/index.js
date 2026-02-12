(function () {
    "use strict";

    // Wait for jQuery to be fully loaded
    if (typeof jQuery === "undefined") {
        console.error("jQuery is not loaded. Loading it now...");
        const script = document.createElement("script");
        script.src = "https://code.jquery.com/jquery-3.6.0.min.js";
        script.onload = initializeDataTable;
        document.head.appendChild(script);
    } else {
        initializeDataTable();
    }

    function initializeDataTable() {
        $(document).ready(function () {
            console.log("jQuery version:", $.fn.jquery);

            // Initialize DataTable
            const table = $("#providersTable").DataTable({
                pageLength: 25,
                lengthMenu: [10, 25, 50, 100],
                order: [[1, "asc"]], // Sort by Name by default
                columnDefs: [
                    {
                        orderable: false,
                        targets: "no-sort",
                        searchable: false
                    }
                ],
                language: {
                    emptyTable: "No signal providers found",
                    search: "Search:",
                    lengthMenu: "Show _MENU_ entries",
                    info: "Showing _START_ to _END_ of _TOTAL_ providers",
                    infoEmpty: "Showing 0 to 0 of 0 providers",
                    paginate: {
                        first: "First",
                        last: "Last",
                        next: "Next",
                        previous: "Previous"
                    }
                }
            });

            function getRequestVerificationToken() {
                let token = $("input[name=\"__RequestVerificationToken\"]").val();
                if (!token) {
                    token =
                        $("input[name=\"__RequestVerificationToken\"]").val() ||
                        $("input[name=\"__RequestVerificationToken\"]").val() ||
                        $("[name=\"__RequestVerificationToken\"]").val();
                }
                return token;
            }

            // Handle provider toggle
            $("#providersTable").on("change", ".provider-toggle", function () {
                const providerId = $(this).data("provider-id");
                const isActive = $(this).is(":checked");
                const $switch = $(this);
                const $row = $switch.closest("tr");

                const token = getRequestVerificationToken();
                if (!token) {
                    console.error("Anti-forgery token not found");
                    showToast("Security token missing. Please refresh the page.", "error");
                    $switch.prop("checked", !isActive);
                    return;
                }

                $.ajax({
                    url: document.body.dataset.toggleProviderStatusUrl,
                    type: "POST",
                    data: {
                        id: providerId,
                        isActive: isActive
                    },
                    headers: {
                        RequestVerificationToken: token
                    },
                    success: function (response) {
                        if (response && response.success) {
                            // Update badge text
                            const $badge = $row.find(".badge");
                            if (isActive) {
                                $badge.removeClass("bg-secondary").addClass("bg-success").text("Active");
                            } else {
                                $badge.removeClass("bg-success").addClass("bg-secondary").text("Inactive");
                            }

                            showToast(response.message, "success");
                        } else {
                            $switch.prop("checked", !isActive);
                            showToast(response?.message ?? "Failed updating provider status", "error");
                        }
                    },
                    error: function (xhr, status, error) {
                        $switch.prop("checked", !isActive);
                        console.error("Toggle error:", error);
                        showToast("Error updating provider status", "error");
                    }
                });
            });

            // Handle delete button click
            let providerToDelete = null;
            let providerNameToDelete = null;

            $("#providersTable").on("click", ".delete-provider-btn", function () {
                providerToDelete = $(this).data("provider-id");
                providerNameToDelete = $(this).data("provider-name");

                $("#providerNameToDelete").text(providerNameToDelete);
                $("#confirmDelete").prop("checked", false);
                $("#confirmDeleteBtn").prop("disabled", true);

                $("#deleteProviderModal").modal("show");
            });

            // Enable/disable delete button based on confirmation
            $("#confirmDelete").change(function () {
                $("#confirmDeleteBtn").prop("disabled", !$(this).is(":checked"));
            });

            // Handle confirmed delete
            $("#confirmDeleteBtn").click(function () {
                if (!providerToDelete) return;

                const $btn = $(this);
                const token = getRequestVerificationToken();

                if (!token) {
                    console.error("Anti-forgery token not found");
                    showToast("Security token missing. Please refresh the page.", "error");
                    return;
                }

                $.ajax({
                    url: document.body.dataset.deleteProviderAjaxUrl,
                    type: "POST",
                    data: {
                        id: providerToDelete
                    },
                    headers: {
                        RequestVerificationToken: token
                    },
                    beforeSend: function () {
                        $btn.prop("disabled", true).html(
                            "<span class=\"spinner-border spinner-border-sm\" role=\"status\" aria-hidden=\"true\"></span> Deleting..."
                        );
                    },
                    success: function (response) {
                        if (response && response.success) {
                            const $row = $("tr[data-provider-id=\"" + providerToDelete + "\"]");
                            table.row($row).remove().draw();

                            $("#deleteProviderModal").modal("hide");

                            $("#confirmDelete").prop("checked", false);
                            $btn.prop("disabled", true).html("<i class=\"fas fa-trash me-1\"></i> Delete Provider");

                            showToast(response.message, "success");
                        } else {
                            showToast(response?.message ?? "Delete failed", "error");
                            $btn.prop("disabled", false).html("<i class=\"fas fa-trash me-1\"></i> Delete Provider");
                        }
                    },
                    error: function (xhr, status, error) {
                        showToast("Error deleting provider: " + error, "error");
                        $btn.prop("disabled", false).html("<i class=\"fas fa-trash me-1\"></i> Delete Provider");
                    }
                });
            });

            function showToast(message, type) {
                const toastId = "toast-" + Date.now();
                const bgClass = type === "success" ? "bg-success" : "bg-danger";
                const icon = type === "success"
                    ? "<i class=\"fas fa-check-circle me-2\"></i>"
                    : "<i class=\"fas fa-exclamation-circle me-2\"></i>";

                const toastHtml = `
                    <div id="${toastId}" class="toast align-items-center text-white ${bgClass} border-0" role="alert" aria-live="assertive" aria-atomic="true">
                        <div class="d-flex">
                            <div class="toast-body">
                                ${icon} ${message}
                            </div>
                            <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
                        </div>
                    </div>
                `;

                if ($(".toast-container").length === 0) {
                    $("body").append("<div class=\"toast-container position-fixed top-0 end-0 p-3\"></div>");
                }

                $(".toast-container").append(toastHtml);

                const toastElement = document.getElementById(toastId);
                const toast = new bootstrap.Toast(toastElement, {
                    autohide: true,
                    delay: 5000
                });

                toast.show();

                $(toastElement).on("hidden.bs.toast", function () {
                    $(this).remove();
                });
            }
        });
    }
})();