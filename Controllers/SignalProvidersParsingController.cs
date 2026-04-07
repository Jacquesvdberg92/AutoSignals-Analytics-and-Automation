// Controllers/Admin/SignalProvidersController.cs
using AutoSignals.Data;
using AutoSignals.Models;
using AutoSignals.Services;
using AutoSignals.Utilities;
using AutoSignals.ViewModels.ProviderRegex;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoSignals.Controllers.Admin
{
    [Authorize(Roles = "Admin")]
    public class SignalProvidersParsingController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly DynamicSignalParserService _parserService;
        private readonly RegexGeneratorService _regexGenerator;

        public SignalProvidersParsingController(
            AutoSignalsDbContext context,
            DynamicSignalParserService parserService,
            RegexGeneratorService regexGenerator)
        {
            _context = context;
            _parserService = parserService;
            _regexGenerator = regexGenerator;
        }

        // GET: admin/signal-providers
        public async Task<IActionResult> Index()
        {
            var providers = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return View(providers);
        }

        // GET: admin/signal-providers/create
        public IActionResult Create()
        {
            var model = new SignalProvider
            {
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                ParsingRules = []
            };

            return View(model);
        }

        // POST: admin/signal-providers/create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SignalProvider provider)
        {
            if (ModelState.IsValid)
            {
                provider.CreatedAt = DateTime.UtcNow;
                _context.Add(provider);
                await _context.SaveChangesAsync();

                // Redirect to "Add First Rule" page
                return RedirectToAction(nameof(AddFirstRule), new { providerId = provider.Id });
            }

            return View(provider);
        }

        // GET: admin/signal-providers/first-rule/5
        public async Task<IActionResult> AddFirstRule(int providerId)
        {
            var provider = await _context.SignalProviders.FindAsync(providerId);
            if (provider == null)
                return NotFound();

            ViewBag.Provider = provider;
            ViewBag.IsFirstRule = true;

            var model = new ProviderParsingRule
            {
                ProviderId = providerId,
                IsRequired = true,
                CreatedAt = DateTime.UtcNow,
                Order = 1 // Default to first order
            };

            return View("CreateRule", model);
        }

        // POST: admin/signal-providers/first-rule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddFirstRule(ProviderParsingRule rule, bool addAnother = false)
        {
            if (ModelState.IsValid)
            {
                rule.CreatedAt = DateTime.UtcNow;
                _context.Add(rule);
                await _context.SaveChangesAsync();

                await _parserService.RefreshCacheAsync(rule.ProviderId);

                if (addAnother)
                {
                    // Redirect to add another rule
                    return RedirectToAction(nameof(CreateRule), new { providerId = rule.ProviderId });
                }
                else
                {
                    // Redirect to Edit page
                    return RedirectToAction(nameof(Edit), new { id = rule.ProviderId });
                }
            }

            var provider = await _context.SignalProviders.FindAsync(rule.ProviderId);
            ViewBag.Provider = provider;
            ViewBag.IsFirstRule = true;

            return View("CreateRule", rule);
        }

        // GET: admin/signal-providers/edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var provider = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (provider == null)
                return NotFound();

            return View(provider);
        }

        // POST: admin/signal-providers/edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SignalProvider provider)
        {
            if (id != provider.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    provider.UpdatedAt = DateTime.UtcNow;
                    _context.Update(provider);
                    await _context.SaveChangesAsync();

                    // Refresh parser cache
                    await _parserService.RefreshCacheAsync(id);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.SignalProviders.AnyAsync(p => p.Id == id))
                        return NotFound();
                    throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(provider);
        }

        // GET: admin/signal-providers/rules/create/5
        public async Task<IActionResult> CreateRule(int providerId)
        {
            var provider = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == providerId);

            if (provider == null)
                return NotFound();

            ViewBag.Provider = provider;
            ViewBag.IsFirstRule = false;

            // Determine next order number
            var nextOrder = provider.ParsingRules.Any() ?
                provider.ParsingRules.Max(r => r.Order) + 1 : 1;

            var model = new ProviderParsingRule
            {
                ProviderId = providerId,
                IsRequired = true,
                CreatedAt = DateTime.UtcNow,
                Order = nextOrder
            };

            return View(model);
        }

        // POST: admin/signal-providers/rules/create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRule(ProviderParsingRule rule, bool addAnother = false)
        {
            // Navigation properties like rule.Provider are not posted from the form; they will be null here.
            // Validation should rely on ProviderId, so skip validating the navigation property.
            ModelState.Remove(nameof(ProviderParsingRule.Provider));
            ModelState.Remove(nameof(ProviderParsingRule.ValidationLogic));

            if (ModelState.IsValid)
            {
                rule.CreatedAt = DateTime.UtcNow;
                _context.Add(rule);
                await _context.SaveChangesAsync();

                await _parserService.RefreshCacheAsync(rule.ProviderId);

                if (addAnother)
                {
                    // Stay on the same page to add another rule
                    return RedirectToAction(nameof(CreateRule), new { providerId = rule.ProviderId });
                }
                else
                {
                    // Return to Edit page
                    return RedirectToAction(nameof(Edit), new { id = rule.ProviderId });
                }
            }

            var provider = await _context.SignalProviders.FindAsync(rule.ProviderId);
            ViewBag.Provider = provider;
            ViewBag.IsFirstRule = false;

            return View(rule);
        }

        // GET: admin/signal-providers/rules/edit/5
        public async Task<IActionResult> EditRule(int id)
        {
            var rule = await _context.ProviderParsingRules
                .Include(r => r.Provider)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null)
                return NotFound();

            ViewBag.IsFirstRule = false;
            return View(rule);
        }

        // POST: admin/signal-providers/rules/edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRule(int id, ProviderParsingRule rule)
        {
            if (id != rule.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    rule.UpdatedAt = DateTime.UtcNow;
                    _context.Update(rule);
                    await _context.SaveChangesAsync();

                    // Refresh parser cache
                    await _parserService.RefreshCacheAsync(rule.ProviderId);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.ProviderParsingRules.AnyAsync(r => r.Id == id))
                        return NotFound();
                    throw;
                }

                return RedirectToAction(nameof(Edit), new { id = rule.ProviderId });
            }

            ViewBag.IsFirstRule = false;
            return View(rule);
        }

        // Delete provider action (if not exists)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var provider = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (provider == null)
                return NotFound();

            _context.SignalProviders.Remove(provider);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // Add to SignalProvidersParsingController.cs
        // GET: admin/signal-providers/test-parsing/5
        public async Task<IActionResult> TestParsing(int id)
        {
            var provider = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (provider == null)
                return NotFound();

            var model = new TestRegexViewModel
            {
                ProviderId = provider.Id,
                ProviderName = provider.Name
            };

            // Pre-populate with sample message if there are rules
            if (provider.ParsingRules.Any())
            {
                model.TelegramMessage = @"$BTC/USDT
Entry: 45000
Stop Loss: 44000
Targets: 46000 - 47000 - 48000
Leverage: 10x
Side: Long";
            }

            return View(model);
        }

        // POST: admin/signal-providers/test-parsing
        // Replace the POST TestParsing method in SignalProvidersParsingController.cs with this:
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> TestParsing(TestRegexViewModel model)
        //{
        //    var provider = await _context.SignalProviders
        //        .Include(p => p.ParsingRules)
        //        .FirstOrDefaultAsync(p => p.Id == model.ProviderId);

        //    if (provider == null)
        //        return NotFound();

        //    model.ProviderName = provider.Name;
        //    model.Results = new List<RuleTestResult>();

        //    // Simulate the EXACT same workflow as DynamicSignalParserService.ParseWithProviderConfig
        //    var sanitizedMessage = MessageSanitizer.SanitizeMessage(model.TelegramMessage);
        //    var workingCopy = sanitizedMessage;
        //    var allTpValues = new List<string>();

        //    // Track detailed info for each rule application
        //    var ruleDetails = new List<RuleApplicationDetail>();

        //    // Process rules in order exactly as in production
        //    foreach (var rule in provider.ParsingRules.OrderBy(r => r.Order))
        //    {
        //        var result = new RuleTestResult
        //        {
        //            RuleId = rule.Id,
        //            RuleType = rule.RuleType,
        //            RegexPattern = rule.RegexPattern,
        //            RegexGroupName = rule.RegexGroupName,
        //            FallbackValue = rule.FallbackValue,
        //            IsRequired = rule.IsRequired,
        //            Order = rule.Order,
        //            ValidationLogic = rule.ValidationLogic,
        //            ValidationPassed = true, // default to true
        //            ValidationErrors = new List<string>()
        //        };

        //        try
        //        {
        //            // Track the state before applying rule
        //            var beforeState = new RuleApplicationDetail
        //            {
        //                RuleId = rule.Id,
        //                RuleType = rule.RuleType,
        //                WorkingCopyBefore = workingCopy,
        //                RulePattern = rule.RegexPattern
        //            };

        //            // Apply the rule EXACTLY as in DynamicSignalParserService
        //            var (success, value, rawMatch, remainingMessage, matchInfo) =
        //                ApplyRuleWithProductionLogic(workingCopy, sanitizedMessage, rule);

        //            result.IsSuccess = success;
        //            result.ExtractedValue = value;
        //            result.RawMatch = rawMatch;

        //            beforeState.MatchInfo = matchInfo;
        //            beforeState.WorkingCopyAfter = remainingMessage;
        //            beforeState.ExtractedValue = value;

        //            ruleDetails.Add(beforeState);

        //            if (success)
        //            {
        //                if (!string.IsNullOrEmpty(rule.ValidationLogic))
        //                {
        //                    var validationResult = ValidateWithJsonLogic(value, rule.ValidationLogic, rule.RuleType);
        //                    result.ValidationPassed = validationResult.IsValid;
        //                    result.ValidationErrors = validationResult.Errors;

        //                    if (!validationResult.IsValid)
        //                    {
        //                        if (rule.IsRequired)
        //                        {
        //                            result.IsSuccess = false;
        //                            result.ErrorMessage = $"Validation failed: {validationResult.ErrorMessage}";
        //                        }
        //                        else
        //                        {
        //                            result.Notes = $"Warning: Validation failed but rule is not required. {validationResult.ErrorMessage}";
        //                        }
        //                    }
        //                }

        //                if (rule.RuleType == "TakeProfit")
        //                {
        //                    // Process TP values exactly as in production
        //                    ProcessTakeProfitValuesForTesting(value, allTpValues);
        //                }

        //                // Update working copy (only for TP rules in production)
        //                if (rule.RuleType == "TakeProfit")
        //                {
        //                    workingCopy = remainingMessage;
        //                }
        //            }
        //            else
        //            {
        //                if (rule.IsRequired)
        //                {
        //                    result.ErrorMessage = $"Required rule failed. ";
        //                    if (!string.IsNullOrEmpty(rule.FallbackValue))
        //                        result.ErrorMessage += $"Will use fallback: {rule.FallbackValue}";
        //                }
        //            }

        //            if (success && !string.IsNullOrEmpty(rule.ValidationLogic))
        //            {
        //                var validationResult = ValidateWithJsonLogic(value, rule.ValidationLogic, rule.RuleType);
        //                result.ValidationPassed = validationResult.IsValid;
        //                result.ValidationErrors = validationResult.Errors;
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            result.IsSuccess = false;
        //            result.ErrorMessage = $"Error: {ex.Message}";
        //        }

        //        model.Results.Add(result);
        //    }

        //    // Consolidate TP values exactly as in production
        //    if (allTpValues.Any())
        //    {
        //        var distinctTps = allTpValues
        //            .Where(v => !string.IsNullOrWhiteSpace(v))
        //            .Select(v => decimal.TryParse(v, out decimal num) ? num.ToString("0.########") : v)
        //            .Distinct()
        //            .OrderBy(v => decimal.Parse(v))
        //            .ToList();

        //        // Update TP results to show final consolidated values
        //        foreach (var result in model.Results.Where(r => r.RuleType == "TakeProfit"))
        //        {
        //            if (result.IsSuccess)
        //            {
        //                result.ExtractedValue = string.Join(",", distinctTps);
        //                result.Notes = $"Consolidated from multiple matches: {result.ExtractedValue}";
        //            }
        //        }
        //    }

        //    // Try to build a complete signal using the same mapping logic
        //    model.ParsedSignal = BuildParsedSignalWithProductionLogic(model.Results, allTpValues);

        //    // Also show the rule application details for debugging
        //    ViewBag.RuleDetails = ruleDetails;

        //    return View(model);
        //}

        [HttpPost]
        public async Task<IActionResult> TestSingleRule(
    [FromBody] AutoSignals.ViewModels.ProviderRegex.TestSingleRuleRequest request)
        {
            try
            {
                // Create a temporary rule from the form data
                var testRule = new ProviderParsingRule
                {
                    RuleType = request.RuleType,
                    RegexPattern = request.RegexPattern,
                    RegexGroupName = request.RegexGroupName,
                    FallbackValue = request.FallbackValue,
                    ValidationLogic = request.ValidationLogic,
                    IsRequired = request.IsRequired,
                    Order = request.Order ?? 1
                };

                // Prepare response
                var response = new TestSingleRuleResponse
                {
                    Success = true
                };

                try
                {
                    // 1. Test regex matching
                    var match = Regex.Match(request.SampleText, testRule.RegexPattern,
                        RegexOptions.IgnoreCase | RegexOptions.Multiline);

                    if (match.Success)
                    {
                        // Extract value
                        string extractedValue = null;

                        if (!string.IsNullOrEmpty(testRule.RegexGroupName))
                        {
                            // Try named group
                            if (match.Groups[testRule.RegexGroupName].Success)
                            {
                                extractedValue = match.Groups[testRule.RegexGroupName].Value;
                            }
                        }
                        else
                        {
                            // Use first group or whole match
                            extractedValue = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                        }

                        response.ExtractedValue = extractedValue ?? match.Value;

                        // Capture all groups for debugging
                        foreach (Group group in match.Groups)
                        {
                            if (group.Success && !string.IsNullOrEmpty(group.Name) && group.Name != "0")
                            {
                                response.Matches.Add(new RuleMatchInfo
                                {
                                    GroupName = group.Name,
                                    Value = group.Value,
                                    Index = group.Index,
                                    Length = group.Length
                                });
                            }
                        }

                        // 2. Test validation logic if provided
                        if (!string.IsNullOrEmpty(testRule.ValidationLogic) &&
                            !string.IsNullOrEmpty(extractedValue))
                        {
                            var validationRules = JsonSerializer.Deserialize<List<ValidationRule>>(testRule.ValidationLogic);

                            foreach (var validationRule in validationRules)
                            {
                                var validationResult = ValidateAgainstRule(extractedValue, validationRule);
                                response.ValidationResults.Add(new ValidationTestResult
                                {
                                    Operator = validationRule.Operator,
                                    Value = validationRule.Value,
                                    IsValid = validationResult.IsValid,
                                    ErrorMessage = validationResult.ErrorMessage
                                });
                            }
                        }
                    }
                    else
                    {
                        // No regex match - use fallback if available
                        if (!string.IsNullOrEmpty(testRule.FallbackValue))
                        {
                            response.ExtractedValue = testRule.FallbackValue;
                            response.FallbackUsed = true;
                        }
                        else if (testRule.IsRequired)
                        {
                            response.Success = false;
                            response.Error = "Required rule failed: No match found and no fallback value provided.";
                        }
                        else
                        {
                            response.ExtractedValue = null;
                            response.Error = "No match found (rule is not required)";
                        }
                    }
                }
                catch (RegexParseException regexEx)
                {
                    response.Success = false;
                    response.Error = $"Invalid regex pattern: {regexEx.Message}";
                }
                catch (JsonException jsonEx)
                {
                    response.Success = false;
                    response.Error = $"Invalid validation logic JSON: {jsonEx.Message}";
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.Error = $"Error testing rule: {ex.Message}";
                }

                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new TestSingleRuleResponse
                {
                    Success = false,
                    Error = $"Unexpected error: {ex.Message}"
                });
            }
        }

        private (bool IsValid, string ErrorMessage) ValidateAgainstRule(string value, ValidationRule rule)
        {
            try
            {
                if (rule == null || string.IsNullOrEmpty(rule.Operator))
                    return (true, null); // No validation rule

                switch (rule.Operator.ToLower())
                {
                    case "min":
                        if (decimal.TryParse(value, out decimal numValue) && numValue < Convert.ToDecimal(rule.Value))
                            return (false, rule.ErrorMessage ?? $"Value must be at least {rule.Value}");
                        break;
                    case "max":
                        if (decimal.TryParse(value, out numValue) && numValue > Convert.ToDecimal(rule.Value))
                            return (false, rule.ErrorMessage ?? $"Value cannot exceed {rule.Value}");
                        break;
                    case "range":
                        var rangeParts = rule.Value.ToString().Split('-');
                        if (rangeParts.Length == 2 &&
                            decimal.TryParse(value, out numValue) &&
                            decimal.TryParse(rangeParts[0], out decimal min) &&
                            decimal.TryParse(rangeParts[1], out decimal max))
                        {
                            if (numValue < min || numValue > max)
                                return (false, rule.ErrorMessage ?? $"Value must be between {min} and {max}");
                        }
                        break;
                    case "regex":
                        if (!Regex.IsMatch(value, rule.Value.ToString()))
                            return (false, rule.ErrorMessage ?? $"Value must match pattern: {rule.Value}");
                        break;
                    case "in":
                        var allowedValues = JsonSerializer.Deserialize<List<string>>(rule.Value.ToString());
                        if (!allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
                            return (false, rule.ErrorMessage ?? $"Value must be one of: {string.Join(", ", allowedValues)}");
                        break;
                    case "required":
                        if (string.IsNullOrWhiteSpace(value))
                            return (false, rule.ErrorMessage ?? "Value is required");
                        break;
                }

                return (true, null);
            }
            catch
            {
                return (false, $"Validation rule '{rule.Operator}' failed to execute");
            }
        }

        private (bool success, string value, string rawMatch, string remainingMessage, string matchInfo)
    ApplyRuleWithProductionLogic(string workingCopy, string originalSanitized, ProviderParsingRule rule)
        {
            try
            {
                string matchInfo = "";
                string remainingMessage = workingCopy;

                // Try matching against the working copy first
                var match = Regex.Match(workingCopy, rule.RegexPattern,
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);

                matchInfo += $"Working copy match: {match.Success}";

                if (match.Success)
                {
                    matchInfo += $" (pos: {match.Index}, len: {match.Length})";
                }
                matchInfo += " | ";

                // If no match in working copy, try the original sanitized message
                if (!match.Success && workingCopy != originalSanitized)
                {
                    match = Regex.Match(originalSanitized, rule.RegexPattern,
                        RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    matchInfo += $"Original match: {match.Success}";

                    if (match.Success)
                    {
                        matchInfo += $" (pos: {match.Index}, len: {match.Length})";
                    }
                    matchInfo += " | ";
                }

                if (!match.Success)
                {
                    return (false, rule.FallbackValue ?? "", "No match", workingCopy, matchInfo);
                }

                string extractedValue = "";
                string rawMatch = match.Value;

                // Extract value based on rule type (simulating ExtractValueFromMatch)
                if (rule.RuleType == "TakeProfit")
                {
                    var tpValues = new List<string>();

                    // Check for numbered TP groups (tp1, tp2, tp3, tp4, etc.)
                    bool foundTpGroup = false;
                    for (int i = 1; i <= 10; i++)
                    {
                        var groupName = $"tp{i}";
                        if (match.Groups[groupName].Success)
                        {
                            var value = match.Groups[groupName].Value.Trim();
                            if (IsValidDecimalForTesting(value))
                            {
                                tpValues.Add(value);
                                matchInfo += $"tp{i}: {value} | ";
                                foundTpGroup = true;
                            }
                        }
                        else if (i > 1 && foundTpGroup)
                        {
                            // If we found at least one TP group but this one is missing,
                            // check if there might be more with gaps (tp1, tp3, tp5)
                            // Continue scanning but don't break
                        }
                    }

                    if (tpValues.Any())
                    {
                        extractedValue = string.Join(",", tpValues);
                        matchInfo += $"Combined TPs: {extractedValue} | ";
                    }
                    else if (!string.IsNullOrEmpty(rule.RegexGroupName) && match.Groups[rule.RegexGroupName].Success)
                    {
                        extractedValue = match.Groups[rule.RegexGroupName].Value.Trim();
                        matchInfo += $"Group '{rule.RegexGroupName}': {extractedValue} | ";
                    }
                    else
                    {
                        // Try to extract just the numbers from the match
                        var numberMatches = Regex.Matches(match.Value, @"\d+(\.\d+)?");
                        if (numberMatches.Count > 0)
                        {
                            var numbers = numberMatches.Cast<Match>()
                                .Select(m => m.Value.Trim())
                                .Where(IsValidDecimalForTesting)
                                .ToList();

                            if (numbers.Any())
                            {
                                extractedValue = string.Join(",", numbers);
                                matchInfo += $"Numbers extracted: {extractedValue} | ";
                            }
                            else
                            {
                                // Fallback to the whole match
                                extractedValue = match.Value.Trim();
                                matchInfo += $"Fallback match: {extractedValue} | ";
                            }
                        }
                        else
                        {
                            extractedValue = match.Value.Trim();
                            matchInfo += $"Fallback match: {extractedValue} | ";
                        }
                    }

                    // Remove matched content for TP rules (simulating production)
                    if (match.Success && match.Length > 0)
                    {
                        remainingMessage = RemoveMatchedTextForTesting(workingCopy, match.Index, match.Length);
                        matchInfo += $"Removed match from position {match.Index}, length {match.Length} | ";
                    }
                }
                else
                {
                    // Non-TP rules
                    if (!string.IsNullOrEmpty(rule.RegexGroupName))
                    {
                        if (match.Groups[rule.RegexGroupName].Success)
                        {
                            extractedValue = match.Groups[rule.RegexGroupName].Value.Trim();

                            // Special handling for specific rule types
                            if (rule.RuleType == "Symbol")
                            {
                                // Clean up symbol format
                                extractedValue = extractedValue
                                    .Replace("$", "")
                                    .Replace(" ", "")
                                    .Replace("//", "/")
                                    .ToUpper();

                                // Ensure it ends with /USDT if not already
                                if (!extractedValue.Contains("/") && extractedValue.EndsWith("USDT"))
                                {
                                    extractedValue = extractedValue.Replace("USDT", "/USDT");
                                }
                            }
                            else if (rule.RuleType == "Side")
                            {
                                extractedValue = extractedValue.ToLower();
                            }
                            else if (rule.RuleType == "Leverage")
                            {
                                // Clean leverage value
                                extractedValue = extractedValue.Replace("x", "").Replace("X", "").Trim();
                            }

                            matchInfo += $"Group '{rule.RegexGroupName}': {extractedValue} | ";
                        }
                        else
                        {
                            // Group specified but not found
                            matchInfo += $"Group '{rule.RegexGroupName}' not found in match | ";

                            // Try fallback to full match
                            extractedValue = match.Value.Trim();
                            matchInfo += $"Using full match: {extractedValue} | ";
                        }
                    }
                    else
                    {
                        extractedValue = match.Value.Trim();

                        // Clean up based on rule type
                        if (rule.RuleType == "Symbol")
                        {
                            extractedValue = extractedValue
                                .Replace("$", "")
                                .Replace(" ", "")
                                .Replace("//", "/")
                                .ToUpper();
                        }
                        else if (rule.RuleType == "Side")
                        {
                            extractedValue = extractedValue.ToLower();
                        }
                        else if (rule.RuleType == "Leverage")
                        {
                            extractedValue = extractedValue.Replace("x", "").Replace("X", "").Trim();
                        }

                        matchInfo += $"Full match: {extractedValue} | ";
                    }
                }

                // Log additional debugging info
                matchInfo += $"Working copy length before: {workingCopy.Length}, after: {remainingMessage.Length}";

                // Validate extracted value for numeric rules
                if (new[] { "Entry", "Stoploss", "TakeProfit" }.Contains(rule.RuleType) && !string.IsNullOrEmpty(extractedValue))
                {
                    if (rule.RuleType == "TakeProfit" && extractedValue.Contains(","))
                    {
                        var tpParts = extractedValue.Split(',');
                        foreach (var part in tpParts)
                        {
                            if (!IsValidDecimalForTesting(part.Trim()))
                            {
                                matchInfo += $" | Invalid TP part: '{part}'";
                                return (false, rule.FallbackValue ?? "", rawMatch, workingCopy, matchInfo);
                            }
                        }
                    }
                    else if (!IsValidDecimalForTesting(extractedValue))
                    {
                        matchInfo += $" | Invalid numeric value: '{extractedValue}'";
                        return (false, rule.FallbackValue ?? "", rawMatch, workingCopy, matchInfo);
                    }
                }

                return (true, extractedValue, rawMatch, remainingMessage, matchInfo);
            }
            catch (RegexParseException regexEx)
            {
                return (false, rule.FallbackValue ?? "", $"Invalid regex pattern: {regexEx.Message}", workingCopy, $"Regex error: {regexEx.Message}");
            }
            catch (Exception ex)
            {
                return (false, rule.FallbackValue ?? "", $"Error: {ex.Message}", workingCopy, ex.Message);
            }
        }

        private bool IsValidDecimalForTesting(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Handle leverage with 'x' suffix
            var cleanValue = value.Replace("x", "").Replace("X", "").Trim();

            return decimal.TryParse(cleanValue, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        }

        private string RemoveMatchedTextForTesting(string text, int startIndex, int length)
        {
            if (string.IsNullOrEmpty(text) || startIndex < 0 || length <= 0)
                return text;

            // Ensure we don't go out of bounds
            if (startIndex >= text.Length)
                return text;

            length = Math.Min(length, text.Length - startIndex);

            // Remove the matched portion
            return text.Remove(startIndex, length);
        }

        private void ProcessTakeProfitValuesForTesting(string tpString, List<string> tpValues)
        {
            if (tpString.Contains(','))
            {
                // Multiple TPs in one string
                var splitValues = tpString.Split(',')
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrEmpty(v) && IsValidDecimalForTesting(v));
                tpValues.AddRange(splitValues);
            }
            else if (IsValidDecimalForTesting(tpString))
            {
                // Single TP value
                tpValues.Add(tpString.Trim());
            }
        }

        private ParsedSignal BuildParsedSignalWithProductionLogic(List<RuleTestResult> results, List<string> tpValues)
        {
            var signal = new ParsedSignal();
            var errors = new List<string>();
            var warnings = new List<string>();

            // Simulate the mapping logic from DynamicSignalParserService.MapToSignal
            foreach (var result in results.OrderBy(r => r.Order))
            {
                if (!result.IsSuccess && result.IsRequired)
                {
                    errors.Add($"Required rule '{result.RuleType}' failed");
                }

                if (result.IsSuccess && !string.IsNullOrEmpty(result.ExtractedValue))
                {
                    try
                    {
                        switch (result.RuleType)
                        {
                            case "Symbol":
                                signal.Symbol = result.ExtractedValue.ToUpper();
                                break;
                            case "Side":
                                signal.Side = result.ExtractedValue.ToLower();
                                break;
                            case "Entry":
                                if (float.TryParse(result.ExtractedValue,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out float entry))
                                    signal.Entry = (decimal)entry;
                                else
                                    errors.Add($"Invalid entry format: {result.ExtractedValue}");
                                break;
                            case "Stoploss":
                                if (float.TryParse(result.ExtractedValue,
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out float sl))
                                    signal.Stoploss = (decimal)sl;
                                else
                                    errors.Add($"Invalid stoploss format: {result.ExtractedValue}");
                                break;
                            case "TakeProfit":
                                // Use consolidated TP values
                                if (tpValues.Any())
                                {
                                    var distinctTps = tpValues
                                        .Select(v => decimal.TryParse(v, out decimal num) ?
                                            num.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) : v)
                                        .Distinct()
                                        .OrderBy(v => decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
                                        .ToList();

                                    signal.TakeProfits = string.Join(",", distinctTps);
                                }
                                else
                                {
                                    signal.TakeProfits = result.ExtractedValue;
                                }
                                break;
                            case "Leverage":
                                if (int.TryParse(result.ExtractedValue.Replace("x", ""), out int lev))
                                    signal.Leverage = lev;
                                else
                                    errors.Add($"Invalid leverage format: {result.ExtractedValue}");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error processing {result.RuleType}: {ex.Message}");
                    }
                }
                else if (result.IsRequired && !string.IsNullOrEmpty(result.FallbackValue))
                {
                    warnings.Add($"Using fallback for {result.RuleType}: {result.FallbackValue}");

                    // Apply fallback value
                    switch (result.RuleType)
                    {
                        case "Symbol":
                            signal.Symbol = result.FallbackValue;
                            break;
                        case "Side":
                            signal.Side = result.FallbackValue;
                            break;
                        case "Entry":
                            if (decimal.TryParse(result.FallbackValue, out decimal entry))
                                signal.Entry = entry;
                            break;
                        case "Leverage":
                            if (int.TryParse(result.FallbackValue?.Replace("x", "", StringComparison.OrdinalIgnoreCase), out var fallbackLev))
                                signal.Leverage = fallbackLev;
                            else
                                warnings.Add($"Invalid leverage fallback: {result.FallbackValue}");
                            break;
                            // ... handle other types
                    }
                }
            }

            // Apply symbol unification (from MapToSignal)
            // Normalize inputs like "BTCUSDT" and "BTC/USDT" to "BTC/USDT:USDT"
            if (!string.IsNullOrWhiteSpace(signal.Symbol))
            {
                var normalized = signal.Symbol.Trim().ToUpperInvariant().Replace("/", "");

                if (normalized.EndsWith("USDT", StringComparison.Ordinal))
                {
                    var baseAsset = normalized[..^"USDT".Length];
                    signal.Symbol = $"{baseAsset}/USDT:USDT";
                }
                else
                {
                    // If only base asset is provided (e.g., "BTC"), assume USDT quote.
                    signal.Symbol = $"{normalized}/USDT:USDT";
                }
            }


            signal.ValidationErrors = errors;
            signal.Warnings = warnings;
            signal.IsValid = !errors.Any();

            return signal;
        }

        // Helper class for debugging
        public class RuleApplicationDetail
        {
            public int RuleId { get; set; }
            public string RuleType { get; set; } = "";
            public string WorkingCopyBefore { get; set; } = "";
            public string WorkingCopyAfter { get; set; } = "";
            public string RulePattern { get; set; } = "";
            public string MatchInfo { get; set; } = "";
            public string ExtractedValue { get; set; } = "";
        }

        // POST: admin/signal-providers/test-parsing-partial
        // In SignalProvidersParsingController.cs, update the TestParsing method

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TestParsingPartial(TestRegexViewModel model)
        {
            var provider = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == model.ProviderId);

            if (provider == null)
            {
                return Content("<div class='alert alert-danger'>Provider not found</div>");
            }

            model.ProviderName = provider.Name;
            model.Results = new List<RuleTestResult>();

            // Simulate the EXACT same workflow as DynamicSignalParserService.ParseWithProviderConfig
            var sanitizedMessage = MessageSanitizer.SanitizeMessage(model.TelegramMessage);
            var workingCopy = sanitizedMessage;
            var allTpValues = new List<string>();

            // Process rules in order exactly as in production
            foreach (var rule in provider.ParsingRules.OrderBy(r => r.Order))
            {
                var result = new RuleTestResult
                {
                    RuleId = rule.Id,
                    RuleType = rule.RuleType,
                    RegexPattern = rule.RegexPattern,
                    RegexGroupName = rule.RegexGroupName,
                    FallbackValue = rule.FallbackValue,
                    IsRequired = rule.IsRequired,
                    Order = rule.Order,
                    ValidationLogic = rule.ValidationLogic,
                    ValidationPassed = true, // default to true
                    ValidationErrors = new List<string>()
                };

                try
                {
                    // Apply the rule EXACTLY as in DynamicSignalParserService
                    var (success, value, rawMatch, remainingMessage, matchInfo) =
                        ApplyRuleWithProductionLogic(workingCopy, sanitizedMessage, rule);

                    result.IsSuccess = success;
                    result.ExtractedValue = value;
                    result.RawMatch = rawMatch;

                    if (success)
                    {
                        if (!string.IsNullOrEmpty(rule.ValidationLogic))
                        {
                            var validationResult = ValidateWithJsonLogic(value, rule.ValidationLogic, rule.RuleType);
                            result.ValidationPassed = validationResult.IsValid;
                            result.ValidationErrors = validationResult.Errors;

                            if (!validationResult.IsValid)
                            {
                                if (rule.IsRequired)
                                {
                                    result.IsSuccess = false;
                                    result.ErrorMessage = $"Validation failed: {validationResult.ErrorMessage}";
                                }
                                else
                                {
                                    result.Notes = $"Warning: Validation failed but rule is not required. {validationResult.ErrorMessage}";
                                }
                            }
                        }

                        if (rule.RuleType == "TakeProfit")
                        {
                            // Process TP values exactly as in production
                            ProcessTakeProfitValuesForTesting(value, allTpValues);
                        }

                        // Update working copy (only for TP rules in production)
                        if (rule.RuleType == "TakeProfit")
                        {
                            workingCopy = remainingMessage;
                        }
                    }
                    else
                    {
                        if (rule.IsRequired)
                        {
                            result.ErrorMessage = $"Required rule failed. ";
                            if (!string.IsNullOrEmpty(rule.FallbackValue))
                                result.ErrorMessage += $"Will use fallback: {rule.FallbackValue}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = $"Error: {ex.Message}";
                }

                model.Results.Add(result);
            }

            // Consolidate TP values exactly as in production
            if (allTpValues.Any())
            {
                var distinctTps = allTpValues
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => decimal.TryParse(v, out decimal num) ? num.ToString("0.########") : v)
                    .Distinct()
                    .OrderBy(v => decimal.Parse(v))
                    .ToList();

                // Update TP results to show final consolidated values
                foreach (var result in model.Results.Where(r => r.RuleType == "TakeProfit"))
                {
                    if (result.IsSuccess)
                    {
                        result.ExtractedValue = string.Join(",", distinctTps);
                        result.Notes = $"Consolidated from multiple matches: {result.ExtractedValue}";
                    }
                }
            }

            // Try to build a complete signal using the same mapping logic
            model.ParsedSignal = BuildParsedSignalWithProductionLogic(model.Results, allTpValues);

            // Return a partial view with just the content (not the modal wrapper)
            return PartialView("_TestParsingContent", model);
        }



        private (bool success, string value, string rawMatch, string remainingMessage)
         TestRegexPatternWithRemoval(
        string message,
        string pattern,
        string groupName,
        string fallbackValue,
        string ruleType)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(pattern))
            {
                return (false, fallbackValue, "No match", message);
            }

            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                var match = regex.Match(message);

                if (match.Success)
                {
                    string value;
                    string rawMatch = match.Value;
                    string remainingMessage = message;

                    // Handle TP groups
                    if (ruleType == "TakeProfit")
                    {
                        var tpValues = new List<string>();

                        // Check for numbered TP groups
                        for (int i = 1; i <= 10; i++)
                        {
                            var tpGroupName = $"tp{i}";
                            if (match.Groups[tpGroupName].Success)
                            {
                                tpValues.Add(match.Groups[tpGroupName].Value.Trim());
                            }
                        }

                        if (tpValues.Any())
                        {
                            value = string.Join(",", tpValues);
                        }
                        else if (!string.IsNullOrEmpty(groupName) && match.Groups[groupName].Success)
                        {
                            value = match.Groups[groupName].Value.Trim();
                        }
                        else
                        {
                            value = match.Value.Trim();
                        }

                        // Remove matched content for TP rules
                        if (match.Length > 0)
                        {
                            remainingMessage = message.Remove(match.Index,
                                Math.Min(match.Length, message.Length - match.Index));
                        }
                    }
                    else
                    {
                        // Non-TP rules
                        if (!string.IsNullOrEmpty(groupName) && match.Groups[groupName].Success)
                        {
                            value = match.Groups[groupName].Value.Trim();
                        }
                        else if (!string.IsNullOrEmpty(groupName))
                        {
                            return (false, fallbackValue, $"Group '{groupName}' not found in match", message);
                        }
                        else
                        {
                            value = match.Value.Trim();
                        }
                    }

                    return (true, value, rawMatch, remainingMessage);
                }
                else
                {
                    return (false, fallbackValue, "No match", message);
                }
            }
            catch (RegexParseException ex)
            {
                return (false, fallbackValue, $"Invalid regex pattern: {ex.Message}", message);
            }
        }

        private ParsedSignal BuildParsedSignal(List<RuleTestResult> results, List<string> tpValues)
        {
            var signal = new ParsedSignal();
            var errors = new List<string>();

            // Process results
            foreach (var result in results.OrderBy(r => r.Order))
            {
                if (!result.IsSuccess && result.IsRequired)
                {
                    errors.Add($"Required rule '{result.RuleType}' failed");
                }

                // Map extracted values to signal properties
                if (result.IsSuccess && !string.IsNullOrEmpty(result.ExtractedValue))
                {
                    switch (result.RuleType)
                    {
                        case "Symbol":
                            signal.Symbol = result.ExtractedValue;
                            break;
                        case "Side":
                            signal.Side = result.ExtractedValue;
                            break;
                        case "Entry":
                            if (decimal.TryParse(result.ExtractedValue, out decimal entry))
                                signal.Entry = entry;
                            else
                                errors.Add($"Invalid entry format: {result.ExtractedValue}");
                            break;
                        case "Stoploss":
                            if (decimal.TryParse(result.ExtractedValue, out decimal sl))
                                signal.Stoploss = sl;
                            else
                                errors.Add($"Invalid stoploss format: {result.ExtractedValue}");
                            break;
                        case "TakeProfit":
                            // Use consolidated TP values
                            if (tpValues.Any())
                                signal.TakeProfits = string.Join(",", tpValues.Distinct());
                            else
                                signal.TakeProfits = result.ExtractedValue;
                            break;
                        case "Leverage":
                            if (int.TryParse(result.ExtractedValue.Replace("x", ""), out int lev))
                                signal.Leverage = lev;
                            else
                                errors.Add($"Invalid leverage format: {result.ExtractedValue}");
                            break;
                    }
                }
                else if (result.IsRequired)
                {
                    // Try to use fallback value for required rules
                    switch (result.RuleType)
                    {
                        case "Symbol":
                            signal.Symbol = result.FallbackValue;
                            break;
                        case "Side":
                            signal.Side = result.FallbackValue;
                            break;
                        case "Entry":
                            if (decimal.TryParse(result.FallbackValue, out decimal entry))
                                signal.Entry = entry;
                            break;
                            // ... handle other types similarly
                    }
                }
            }

            signal.ValidationErrors = errors;
            signal.IsValid = !errors.Any();

            return signal;
        }

        private (bool success, string value, string rawMatch) TestRegexPattern(
            string message,
            string pattern,
            string groupName,
            string fallbackValue)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(pattern))
            {
                return (false, fallbackValue, "No match");
            }

            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                var match = regex.Match(message);

                if (match.Success)
                {
                    string value;
                    string rawMatch = match.Value;

                    if (!string.IsNullOrEmpty(groupName) && match.Groups[groupName].Success)
                    {
                        value = match.Groups[groupName].Value;
                    }
                    else if (!string.IsNullOrEmpty(groupName))
                    {
                        return (false, fallbackValue, $"Group '{groupName}' not found in match");
                    }
                    else
                    {
                        value = match.Value;
                    }

                    return (true, value.Trim(), rawMatch);
                }
                else
                {
                    return (false, fallbackValue, "No match");
                }
            }
            catch (RegexParseException ex)
            {
                return (false, fallbackValue, $"Invalid regex pattern: {ex.Message}");
            }
        }

        private ParsedSignal BuildParsedSignal(List<RuleTestResult> results)
        {
            var signal = new ParsedSignal();
            var errors = new List<string>();

            foreach (var result in results.OrderBy(r => r.Order))
            {
                if (!result.IsSuccess && result.IsRequired)
                {
                    errors.Add($"Required rule '{result.RuleType}' failed");
                }

                // Map extracted values to signal properties
                if (result.IsSuccess && !string.IsNullOrEmpty(result.ExtractedValue))
                {
                    switch (result.RuleType)
                    {
                        case "Symbol":
                            signal.Symbol = result.ExtractedValue;
                            break;
                        case "Side":
                            signal.Side = result.ExtractedValue;
                            break;
                        case "Entry":
                            if (decimal.TryParse(result.ExtractedValue, out decimal entry))
                                signal.Entry = entry;
                            else
                                errors.Add($"Invalid entry format: {result.ExtractedValue}");
                            break;
                        case "Stoploss":
                            if (decimal.TryParse(result.ExtractedValue, out decimal sl))
                                signal.Stoploss = sl;
                            else
                                errors.Add($"Invalid stoploss format: {result.ExtractedValue}");
                            break;
                        case "TakeProfit":
                            signal.TakeProfits = result.ExtractedValue;
                            break;
                        case "Leverage":
                            if (int.TryParse(result.ExtractedValue.Replace("x", ""), out int lev))
                                signal.Leverage = lev;
                            else
                                errors.Add($"Invalid leverage format: {result.ExtractedValue}");
                            break;
                    }
                }
                else if (result.IsRequired)
                {
                    // Try to use fallback value for required rules
                    switch (result.RuleType)
                    {
                        case "Symbol":
                            signal.Symbol = result.FallbackValue;
                            break;
                        case "Side":
                            signal.Side = result.FallbackValue;
                            break;
                        case "Entry":
                            if (decimal.TryParse(result.FallbackValue, out decimal entry))
                                signal.Entry = entry;
                            break;
                            // ... handle other types similarly
                    }
                }
            }

            signal.ValidationErrors = errors;
            signal.IsValid = !errors.Any();

            return signal;
        }

        private ValidationResult ValidateWithJsonLogic(string value, string jsonLogic, string ruleType)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(jsonLogic))
                return result;

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var validationRules = JsonSerializer.Deserialize<List<ValidationRule>>(jsonLogic, options);

                if (validationRules == null || !validationRules.Any())
                    return result;

                foreach (var rule in validationRules)
                {
                    bool isValid = ValidateRule(value, rule, ruleType);

                    if (!isValid)
                    {
                        string error = rule.ErrorMessage ?? $"Value '{value}' failed validation for {ruleType} rule";
                        result.Errors.Add(error);
                        result.IsValid = false;
                    }
                }
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"Invalid JSON validation logic: {ex.Message}");
                result.IsValid = false;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Validation error: {ex.Message}");
                result.IsValid = false;
            }

            return result;
        }

        private bool ValidateRule(string value, ValidationRule rule, string ruleType)
        {
            if (rule.Operator == null || rule.Value == null)
                return true;

            string operatorLower = rule.Operator.ToLowerInvariant();

            switch (operatorLower)
            {
                case "min":
                    if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numVal) &&
                        decimal.TryParse(rule.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal minVal))
                        return numVal >= minVal;
                    break;

                case "max":
                    if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numVal2) &&
                        decimal.TryParse(rule.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal maxVal))
                        return numVal2 <= maxVal;
                    break;

                case "regex":
                    if (rule.Value is string regexPattern)
                        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase);
                    break;

                case "in":
                    if (rule.Value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
                    {
                        var allowedValues = jsonElement.EnumerateArray()
                            .Select(e => e.ToString())
                            .ToArray();
                        return allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase);
                    }
                    else if (rule.Value is string stringValue)
                    {
                        var allowedValues = stringValue.Split(',')
                            .Select(v => v.Trim())
                            .ToArray();
                        return allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase);
                    }
                    break;

                case "notnull":
                case "required":
                    return !string.IsNullOrWhiteSpace(value);

                case "lengthmin":
                    if (int.TryParse(rule.Value.ToString(), out int minLength))
                        return value.Length >= minLength;
                    break;

                case "lengthmax":
                    if (int.TryParse(rule.Value.ToString(), out int maxLength))
                        return value.Length <= maxLength;
                    break;

                case "positive":
                    if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal posVal))
                        return posVal > 0;
                    break;

                case "negative":
                    if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal negVal))
                        return negVal < 0;
                    break;

                case "range":
                    if (rule.Value is string rangeStr)
                    {
                        var parts = rangeStr.Split('-');
                        if (parts.Length == 2 &&
                            decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rangeMin) &&
                            decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rangeMax) &&
                            decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rangeVal))
                            return rangeVal >= rangeMin && rangeVal <= rangeMax;
                    }
                    break;

                case "equal":
                    return value.Equals(rule.Value.ToString(), StringComparison.OrdinalIgnoreCase);

                case "notequal":
                    return !value.Equals(rule.Value.ToString(), StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        // GET: admin/signal-providers/rules/delete/5 (Confirmation)
        public async Task<IActionResult> DeleteRule(int id)
        {
            var rule = await _context.ProviderParsingRules
                .Include(r => r.Provider)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null)
                return NotFound();

            return View(rule);
        }

        // POST: admin/signal-providers/rules/delete/5
        [HttpPost]
        [ActionName("DeleteRule")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRuleConfirmed(int id)
        {
            var rule = await _context.ProviderParsingRules
                .Include(r => r.Provider)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null)
                return NotFound();

            int providerId = rule.ProviderId;

            // Delete the rule
            _context.ProviderParsingRules.Remove(rule);
            await _context.SaveChangesAsync();

            // Reorder remaining rules
            await ReorderRules(providerId);

            // Refresh parser cache
            await _parserService.RefreshCacheAsync(providerId);

            return RedirectToAction(nameof(Edit), new { id = providerId });
        }

        // AJAX delete endpoint
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRuleAjax(int id)
        {
            var rule = await _context.ProviderParsingRules
                .Include(r => r.Provider)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null)
                return Json(new { success = false, message = "Rule not found" });

            int providerId = rule.ProviderId;

            try
            {
                _context.ProviderParsingRules.Remove(rule);
                await _context.SaveChangesAsync();

                // Reorder remaining rules
                await ReorderRules(providerId);

                // Refresh cache
                await _parserService.RefreshCacheAsync(providerId);

                return Json(new
                {
                    success = true,
                    providerId = providerId,
                    message = "Rule deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Delete failed: {ex.Message}"
                });
            }
        }

        // Helper method to reorder rules after deletion
        private async Task ReorderRules(int providerId)
        {
            var rules = await _context.ProviderParsingRules
                .Where(r => r.ProviderId == providerId)
                .OrderBy(r => r.Order)
                .ToListAsync();

            // Reassign order numbers
            int order = 1;
            foreach (var rule in rules)
            {
                rule.Order = order++;
                _context.Update(rule);
            }

            await _context.SaveChangesAsync();
        }

        // POST: admin/signal-providers/toggle-status
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleProviderStatus(int id, bool isActive)
        {
            var provider = await _context.SignalProviders.FindAsync(id);

            if (provider == null)
            {
                return Json(new { success = false, message = "Provider not found" });
            }

            try
            {
                provider.IsActive = isActive;
                provider.UpdatedAt = DateTime.UtcNow;

                _context.Update(provider);
                await _context.SaveChangesAsync();

                // Refresh parser cache
                await _parserService.RefreshCacheAsync(id);

                return Json(new
                {
                    success = true,
                    message = $"Provider {(isActive ? "activated" : "deactivated")} successfully"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error updating provider status: {ex.Message}"
                });
            }
        }

        // POST: admin/signal-providers/delete-ajax
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProviderAjax(int id)
        {
            var provider = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (provider == null)
            {
                return Json(new { success = false, message = "Provider not found" });
            }

            try
            {
                // Remove all associated parsing rules first
                if (provider.ParsingRules.Any())
                {
                    _context.ProviderParsingRules.RemoveRange(provider.ParsingRules);
                }

                // Remove the provider
                _context.SignalProviders.Remove(provider);

                await _context.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Provider '{provider.Name}' deleted successfully"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Delete failed: {ex.Message}"
                });
            }
        }

        // GET: admin/signal-providers/generate-rules/5
        public async Task<IActionResult> GenerateRules(int providerId)
        {
            var provider = await _context.SignalProviders.FindAsync(providerId);
            if (provider == null)
                return NotFound();

            var model = new GenerateRulesViewModel
            {
                ProviderId = provider.Id,
                ProviderName = provider.Name
            };

            return View(model);
        }

        // POST: admin/signal-providers/generate-rules/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateRules(GenerateRulesViewModel model)
        {
            var provider = await _context.SignalProviders.FindAsync(model.ProviderId);
            if (provider == null)
                return NotFound();

            model.ProviderName = provider.Name;

            var examples = model.GetExamples().ToList();
            if (examples.Count == 0)
            {
                model.ErrorMessage = "Please provide at least one example signal message.";
                return View(model);
            }

            var (rules, error) = await _regexGenerator.GenerateRulesAsync(examples);

            if (error != null)
            {
                model.ErrorMessage = error;
                return View(model);
            }

            model.SuggestedRules = rules;
            model.RulesGenerated = true;

            return View(model);
        }

        // POST: admin/signal-providers/generate-rules-ajax (AJAX endpoint — returns partial view HTML or JSON error)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateRulesAjax(GenerateRulesViewModel model)
        {
            var provider = await _context.SignalProviders.FindAsync(model.ProviderId);
            if (provider == null)
                return BadRequest(new { error = "Provider not found." });

            model.ProviderName = provider.Name;

            var examples = model.GetExamples().ToList();
            if (examples.Count == 0)
                return BadRequest(new { error = "Please provide at least one example signal message." });

            var (rules, error) = await _regexGenerator.GenerateRulesAsync(examples);

            if (error != null)
                return BadRequest(new { error });

            model.SuggestedRules = rules;
            model.RulesGenerated = true;

            return PartialView("_GeneratedRulesPanel", model);
        }

        // POST: admin/signal-providers/save-generated-rules
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGeneratedRules(int providerId, [FromForm] List<SuggestedParsingRule> selectedRules)
        {
            var provider = await _context.SignalProviders
                .Include(p => p.ParsingRules)
                .FirstOrDefaultAsync(p => p.Id == providerId);

            if (provider == null)
                return NotFound();

            // Determine next available order number after existing rules
            int nextOrder = provider.ParsingRules.Any()
                ? provider.ParsingRules.Max(r => r.Order) + 1
                : 1;

            foreach (var suggested in selectedRules.Where(r => !string.IsNullOrWhiteSpace(r.RegexPattern)))
            {
                var rule = new ProviderParsingRule
                {
                    ProviderId = providerId,
                    RuleType = suggested.RuleType,
                    RegexPattern = suggested.RegexPattern,
                    RegexGroupName = suggested.RegexGroupName ?? "",
                    FallbackValue = string.IsNullOrWhiteSpace(suggested.FallbackValue) ? null : suggested.FallbackValue,
                    IsRequired = suggested.IsRequired,
                    Order = nextOrder++,
                    ValidationLogic = string.IsNullOrWhiteSpace(suggested.ValidationLogic) ? null : suggested.ValidationLogic,
                    CreatedAt = DateTime.UtcNow
                };

                _context.ProviderParsingRules.Add(rule);
            }

            await _context.SaveChangesAsync();
            await _parserService.RefreshCacheAsync(providerId);

            TempData["Success"] = $"Successfully saved {selectedRules.Count} generated rules.";
            return RedirectToAction(nameof(Edit), new { id = providerId });
        }
    }
}