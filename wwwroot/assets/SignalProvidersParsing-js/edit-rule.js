(function () {
    "use strict";

    // Auto-update regex examples based on selected rule type
    function wireRuleTypeExamples() {
        const ruleTypeEl = document.getElementById("RuleType");
        const regexPatternEl = document.getElementById("RegexPattern");

        if (!ruleTypeEl || !regexPatternEl) return;

        const examples = {
            Symbol: "\\$(?<symbol>[A-Z]+)(\\/USDT)?",
            Entry: "Entry\\s*:\\s*(?<entry>\\d+(\\.\\d+)?)",
            Stoploss: "(?:SL|Stop\\s*Loss)\\s*:\\s*(?<sl>\\d+(\\.\\d+)?)",
            TakeProfit: "Targets?\\s*:\\s*((\\d+(\\.\\d+)?\\s*-\\s*)+\\d+(\\.\\d+)?)",
            Side: "(Long|Short|BUY|SELL)",
            Leverage: "Leverage\\s*:\\s*(?<lev>\\d+)x"
        };

        ruleTypeEl.addEventListener("change", function () {
            if (examples[this.value]) {
                regexPatternEl.value = examples[this.value];
            }
        });
    }

    function validateJson() {
        const jsonField = document.getElementById("ValidationLogic");
        if (!jsonField) return { isValid: true, message: "" };

        const raw = (jsonField.value ?? "").trim();

        // Allow empty (no validation logic)
        if (raw === "") return { isValid: true, message: "" };

        // Also allow explicit null (treat as not provided)
        if (raw.toLowerCase() === "null") return { isValid: true, message: "" };

        try {
            const parsed = JSON.parse(raw);

            if (!Array.isArray(parsed)) {
                return { isValid: false, message: "Validation logic must be a JSON array. Example: [{...}, {...}]" };
            }

            for (let i = 0; i < parsed.length; i++) {
                const rule = parsed[i];

                if (!rule.Operator) return { isValid: false, message: `Rule ${i + 1} is missing "Operator" property` };
                if (rule.Value === undefined || rule.Value === null) {
                    return { isValid: false, message: `Rule ${i + 1} is missing "Value" property` };
                }
            }

            return { isValid: true, message: "JSON syntax is valid!" };
        } catch {
            return { isValid: false, message: "Invalid JSON format. Please check your validation logic syntax." };
        }
    }

    function showToast(type, title, message) {
        const toastId = "toast-" + Date.now();
        const toastHtml = `
            <div id="${toastId}" class="toast" role="alert" aria-live="assertive" aria-atomic="true">
                <div class="toast-header bg-${type} text-white">
                    <strong class="me-auto">${title}</strong>
                    <button type="button" class="btn-close btn-close-white" data-bs-dismiss="toast" aria-label="Close"></button>
                </div>
                <div class="toast-body">${message}</div>
            </div>
        `;

        $(".toast-container").append(toastHtml);
        const toastElement = $("#" + toastId);
        const toast = new bootstrap.Toast(toastElement);
        toast.show();

        toastElement.on("hidden.bs.toast", function () {
            $(this).remove();
        });
    }

    // Expose for inline onclick/onsubmit usage in the Razor view
    window.testJsonSyntax = function () {
        const result = validateJson();
        if (result.isValid) {
            showToast("success", "Validation Successful", result.message || "JSON syntax is valid!");
        } else {
            showToast("error", "Validation Error", result.message || "Invalid JSON format.");
        }
    };

    window.validateForm = function () {
        const result = validateJson();
        if (!result.isValid) {
            alert("Validation Logic Error: " + result.message);
            return false;
        }
        return true;
    };

    function autoOpenValidationGuide() {
        const validationField = document.getElementById("ValidationLogic");
        if (validationField && validationField.value.trim()) {
            const validationGuide = document.getElementById("validationGuide");
            if (validationGuide) {
                new bootstrap.Collapse(validationGuide, { toggle: true });
            }
        }
    }

    function wireSingleRuleModalPrefill() {
        const modalEl = document.getElementById("testSingleRuleModal");
        if (!modalEl) return;

        modalEl.addEventListener("show.bs.modal", function () {
            const ruleType = document.getElementById("RuleType")?.value;
            const sampleTextArea = document.getElementById("sampleText");
            if (!sampleTextArea) return;

            const examples = {
                Symbol: `BTC/USDT ready for entry!\nETH/USDT breaking out.\n$SOL looking strong.`,
                Entry: `Entry: 0.025\nBuy at 0.025\nEntry price is 45000`,
                Stoploss: `SL: 0.023\nStop Loss at 0.023\nStoploss: 44000`,
                TakeProfit: `Targets: 0.027 - 0.029 - 0.032\nTP: 0.027, 0.029, 0.032\nTake profit at 46000`,
                Side: `Long position\nGoing short\nBUY signal\nSELL now`,
                Leverage: `Leverage: 10x\nUse 20x leverage\n5x recommended`
            };

            sampleTextArea.value = examples[ruleType] ?? "";

            const testResult = document.getElementById("testResult");
            const resultContent = document.getElementById("resultContent");
            if (testResult) testResult.style.display = "none";
            if (resultContent) resultContent.innerHTML = "";
        });
    }

    window.testCurrentRule = function () {
        const sampleText = document.getElementById("sampleText")?.value?.trim();
        if (!sampleText) {
            alert("Please enter sample text to test against.");
            return;
        }

        const validationLogic = document.getElementById("ValidationLogic")?.value;

        if (validationLogic && validationLogic.trim()) {
            const jsonValidation = validateJson();
            if (!jsonValidation.isValid) {
                alert("Validation Logic Error: " + jsonValidation.message);
                return;
            }
        }

        const url = document.body?.dataset?.testSingleRuleUrl;
        if (!url) {
            alert("Missing test URL configuration (data-test-single-rule-url).");
            return;
        }

        const requestData = {
            RuleType: document.getElementById("RuleType")?.value,
            RegexPattern: document.getElementById("RegexPattern")?.value,
            RegexGroupName: document.getElementById("RegexGroupName")?.value,
            FallbackValue: document.getElementById("FallbackValue")?.value,
            ValidationLogic: validationLogic ? validationLogic.trim() : null,
            IsRequired: document.getElementById("IsRequired")?.checked ?? false,
            Order: document.getElementById("Order")?.value ? parseInt(document.getElementById("Order").value) : 1,
            SampleText: sampleText
        };

        const testResult = document.getElementById("testResult");
        const resultContent = document.getElementById("resultContent");

        if (testResult) testResult.style.display = "block";
        if (resultContent) {
            resultContent.innerHTML = `
                <div class="text-center">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Loading...</span>
                    </div>
                    <p class="mt-2">Testing rule...</p>
                </div>
            `;
        }

        fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]')?.value
            },
            body: JSON.stringify(requestData)
        })
            .then(response => {
                if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
                return response.json();
            })
            .then(data => {
                if (!resultContent) return;

                if (data.success) {
                    let html = `<div class="alert alert-success">✅ Rule executed successfully!</div>`;

                    if (data.fallbackUsed) {
                        html += `<div class="alert alert-warning">⚠️ Used fallback value: ${data.extractedValue}</div>`;
                    } else if (data.matches && data.matches.length > 0) {
                        html += `<h6>Matches Found:</h6>
                                 <table class="table table-sm">
                                   <thead><tr><th>Group</th><th>Value</th><th>Position</th></tr></thead>
                                   <tbody>`;
                        data.matches.forEach(match => {
                            html += `<tr>
                                        <td><code>${match.groupName}</code></td>
                                        <td><strong>${match.value}</strong></td>
                                        <td>${match.index} (${match.length} chars)</td>
                                     </tr>`;
                        });
                        html += `</tbody></table>`;
                    }

                    if (data.extractedValue) {
                        html += `<div class="alert alert-info"><strong>Extracted Value:</strong> <code>${data.extractedValue}</code></div>`;
                    }

                    if (data.validationResults && data.validationResults.length > 0) {
                        html += `<h6>Validation Results:</h6><ul class="list-group">`;
                        data.validationResults.forEach(validation => {
                            const isValid = validation.isValid;
                            html += `<li class="list-group-item ${isValid ? "list-group-item-success" : "list-group-item-danger"}">
                                        <i class="fe ${isValid ? "fe-check-circle" : "fe-alert-circle"}"></i>
                                        <code>${validation.operator}: ${JSON.stringify(validation.value)}</code>
                                        ${!isValid ? `<br><small class="text-danger">${validation.errorMessage}</small>` : ""}
                                     </li>`;
                        });
                        html += `</ul>`;
                    }

                    resultContent.innerHTML = html;
                } else {
                    resultContent.innerHTML = `
                        <div class="alert alert-danger">
                            <h6>❌ Error Testing Rule</h6>
                            <p>${data.error || "Unknown error occurred"}</p>
                        </div>
                    `;
                }
            })
            .catch(error => {
                if (!resultContent) return;
                resultContent.innerHTML = `
                    <div class="alert alert-danger">
                        <h6>❌ Request Failed</h6>
                        <p>${error.message || "Network error occurred"}</p>
                    </div>
                `;
            });
    };

    window.applyValidationTemplate = function () {
        const templateKey = document.getElementById("ValidationTemplate")?.value;
        const field = document.getElementById("ValidationLogic");
        if (!templateKey || !field) return;

        const templates = {
            required: [
                { Operator: "required", Value: true, ErrorMessage: "Value is required" }
            ],
            minmax: [
                { Operator: "min", Value: 0, ErrorMessage: "Value must be at least 0" },
                { Operator: "max", Value: 100000, ErrorMessage: "Value cannot exceed 100,000" }
            ],
            range: [
                { Operator: "range", Value: "1-100", ErrorMessage: "Value must be between 1 and 100" }
            ],
            inSide: [
                {
                    Operator: "in",
                    Value: ["long", "short", "buy", "sell", "Long", "Short", "Buy", "Sell", "LONG", "SHORT", "BUY", "SELL"],
                    ErrorMessage: "Side must be long/short (buy/sell accepted)"
                }
            ],
            regexSymbol: [
                { Operator: "regex", Value: "^[A-Z]+/USDT$", ErrorMessage: "Symbol must be in format: BTC/USDT" }
            ]
        };

        const json = JSON.stringify(templates[templateKey] ?? [], null, 2);
        if (!json || json === "[]") return;

        const existing = (field.value ?? "").trim();

        // If empty (or "null"), just set it. Otherwise append with a newline separator.
        if (existing === "" || existing.toLowerCase() === "null") {
            field.value = json;
        } else {
            field.value = existing + "\n\n" + json;
        }
    };

    document.addEventListener("DOMContentLoaded", function () {
        wireRuleTypeExamples();
        autoOpenValidationGuide();
        wireSingleRuleModalPrefill();
    });
})();