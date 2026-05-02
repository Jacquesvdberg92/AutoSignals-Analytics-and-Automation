using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AutoSignals.Data;
using AutoSignals.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace AutoSignals.Controllers
{
    [Authorize]
    public class UserFeedbacksController : Controller
    {
        private readonly AutoSignalsDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RecaptchaService _recaptchaService;

        // Update the type of `_configuration` to `IConfiguration` to ensure proper indexing.  
        private readonly IConfiguration _configuration;

        public UserFeedbacksController(AutoSignalsDbContext context, UserManager<IdentityUser> userManager, RecaptchaService recaptchaService, IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _recaptchaService = recaptchaService;
            _configuration = configuration;
        }

        // GET: UserFeedbacks
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var isAdmin = user != null && await _userManager.IsInRoleAsync(user, "Admin");
            ViewBag.IsAdmin = isAdmin;

            IQueryable<UserFeedback> feedbacks = _context.UserFeedback
                .Include(f => f.Replies);

            if (!isAdmin)
            {
                feedbacks = feedbacks.Where(f => f.UserId == user.Id);
            }

            var list = await feedbacks.ToListAsync();

            if (isAdmin)
            {
                var userIds = list.Select(f => f.UserId).Distinct().ToList();
                var userNames = new Dictionary<string, string>();
                foreach (var uid in userIds)
                {
                    var u = await _userManager.FindByIdAsync(uid);
                    userNames[uid] = u?.UserName ?? uid;
                }
                ViewBag.UserNames = userNames;
            }

            return View(list);
        }

        // GET: UserFeedbacks/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var userFeedback = await _context.UserFeedback
                .Include(f => f.Images)
                .Include(f => f.Replies)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (userFeedback == null)
            {
                return NotFound();
            }

            if (!await CanAccessFeedbackAsync(user, userFeedback))
            {
                return Forbid();
            }

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            ViewBag.IsAdmin = isAdmin;

            if (isAdmin)
            {
                // Build a display-name lookup for all authors in the thread
                var authorIds = userFeedback.Replies.Select(r => r.AuthorId)
                    .Append(userFeedback.UserId)
                    .Distinct().ToList();
                var authors = new Dictionary<string, string>();
                foreach (var aid in authorIds)
                {
                    var u = await _userManager.FindByIdAsync(aid);
                    authors[aid] = u?.UserName ?? aid;
                }
                ViewBag.Authors = authors;

                // Department options for the assign dropdown — no real user accounts exposed
                var departments = new[] { "Support", "Development", "Marketing" };
                ViewBag.AdminList = departments.Select(d => new SelectListItem
                {
                    Value = d,
                    Text = d,
                    Selected = d == userFeedback.AssignedTo
                }).ToList();
            }
            else
            {
                var authorIds = userFeedback.Replies.Select(r => r.AuthorId)
                    .Append(userFeedback.UserId)
                    .Distinct().ToList();
                var authors = new Dictionary<string, string>();
                foreach (var aid in authorIds)
                {
                    var u = await _userManager.FindByIdAsync(aid);
                    authors[aid] = u?.UserName ?? aid;
                }
                ViewBag.Authors = authors;
            }

            return View(userFeedback);
        }


        [AllowAnonymous]
        public IActionResult Create()
        {
            if (!(User?.Identity?.IsAuthenticated ?? false))
            {
                var returnUrl = $"{Request.Path}{Request.QueryString}";
                return RedirectToAction("AccountNeeded", "Home", new { returnUrl });
            }

            ViewBag.RecaptchaSiteKey = _configuration["Recaptcha:SiteKey"];
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowAnonymous]
        public async Task<IActionResult> Create(
    [Bind("Subject,Message,Status")] UserFeedback userFeedback,
    List<IFormFile> ScreenshotFiles,
    [FromForm(Name = "g-recaptcha-response")] string recaptchaResponse)
        {
            if (!(User?.Identity?.IsAuthenticated ?? false))
            {
                var returnUrl = $"{Request.Path}{Request.QueryString}";
                return RedirectToAction("AccountNeeded", "Home", new { returnUrl });
            }

            var recaptchaResult = await _recaptchaService.VerifyAsyncFull(recaptchaResponse);

            if (recaptchaResult == null || !recaptchaResult.Success || recaptchaResult.Score < 0.5)
            {
                ModelState.AddModelError(string.Empty, "CAPTCHA validation failed. Please try again.");
                return View(userFeedback);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            userFeedback.UserId = user.Id;
            userFeedback.SubmittedAt = DateTime.UtcNow;
            userFeedback.Status = "New";
            var isVip = await _userManager.IsInRoleAsync(user, "VIP");
            userFeedback.Priority = isVip ? "Important" : "Normal";

            if (ScreenshotFiles != null && ScreenshotFiles.Count > 0)
            {
                long totalSize = ScreenshotFiles.Sum(f => f.Length);
                if (totalSize > 25 * 1024 * 1024)
                {
                    ModelState.AddModelError("ScreenshotFiles", "Total file size must not exceed 25MB.");
                    return View(userFeedback);
                }

                foreach (var file in ScreenshotFiles)
                {
                    using (var ms = new MemoryStream())
                    {
                        await file.CopyToAsync(ms);
                        userFeedback.Images.Add(new UserFeedbackImage
                        {
                            Data = ms.ToArray(),
                            FileName = file.FileName
                        });
                    }
                }
            }

            _context.Add(userFeedback);
            await _context.SaveChangesAsync();

            // Generate ticket number after we have the Id
            userFeedback.TicketNumber = $"TKT-{userFeedback.Id:D5}";
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // GET: UserFeedbacks/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userFeedback = await _context.UserFeedback
                .Include(f => f.Images)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (userFeedback == null)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            ViewBag.IsAdmin = isAdmin;

            // Only allow non-admins to edit their own feedback
            if (!isAdmin && userFeedback.UserId != user.Id)
            {
                return Forbid();
            }

            return View(userFeedback);
        }

        // POST: UserFeedbacks/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
    int id,
    [Bind("Id,UserId,Subject,Message,SubmittedAt,Status,Priority,AdminNotes")] UserFeedback userFeedback,
    List<IFormFile> ScreenshotFiles)
        {
            if (id != userFeedback.Id)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            // Load the existing feedback including images
            var existingFeedback = await _context.UserFeedback
                .Include(f => f.Images)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (existingFeedback == null)
            {
                return NotFound();
            }

            // Only allow non-admins to edit their own feedback
            if (!isAdmin && existingFeedback.UserId != user.Id)
            {
                return Forbid();
            }

            // Calculate total size of existing and new images
            long existingImagesSize = existingFeedback.Images.Sum(i => i.Data.Length);
            long newImagesSize = ScreenshotFiles?.Sum(f => f.Length) ?? 0;
            long totalSize = existingImagesSize + newImagesSize;

            if (totalSize > 25 * 1024 * 1024)
            {
                ModelState.AddModelError("ScreenshotFiles", "Total file size must not exceed 25MB.");
                ViewBag.IsAdmin = isAdmin;
                return View(existingFeedback);
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Only update allowed fields
                    if (isAdmin)
                    {
                        existingFeedback.Subject = userFeedback.Subject;
                        existingFeedback.Message = userFeedback.Message;
                        existingFeedback.Status = userFeedback.Status;
                        existingFeedback.Priority = userFeedback.Priority;
                        existingFeedback.AdminNotes = userFeedback.AdminNotes;
                    }
                    else
                    {
                        existingFeedback.Message = userFeedback.Message;
                        // Non-admins cannot change subject, status, priority, or admin notes
                    }

                    // Add new images
                    if (ScreenshotFiles != null && ScreenshotFiles.Count > 0)
                    {
                        foreach (var file in ScreenshotFiles)
                        {
                            using (var ms = new MemoryStream())
                            {
                                await file.CopyToAsync(ms);
                                var image = new UserFeedbackImage
                                {
                                    Data = ms.ToArray(),
                                    FileName = file.FileName,
                                    UserFeedbackId = existingFeedback.Id
                                };
                                _context.UserFeedbackImages.Add(image);
                            }
                        }
                    }

                    _context.Update(existingFeedback);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserFeedbackExists(userFeedback.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }

            ViewBag.IsAdmin = isAdmin;
            return View(existingFeedback);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteImage(int feedbackId, int imageId)
        {
            var user = await _userManager.GetUserAsync(User);
            var isAdmin = user != null && await _userManager.IsInRoleAsync(user, "Admin");

            // Only admin can delete images
            if (!isAdmin)
                return Forbid();

            var image = await _context.UserFeedbackImages
                .FirstOrDefaultAsync(i => i.Id == imageId && i.UserFeedbackId == feedbackId);

            if (image == null)
                return NotFound();

            _context.UserFeedbackImages.Remove(image);
            await _context.SaveChangesAsync();

            // Redirect back to Edit page for the feedback
            return RedirectToAction(nameof(Edit), new { id = feedbackId });
        }


        // GET: UserFeedbacks/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var userFeedback = await _context.UserFeedback
                .FirstOrDefaultAsync(m => m.Id == id);
            if (userFeedback == null)
            {
                return NotFound();
            }

            if (!await CanAccessFeedbackAsync(user, userFeedback))
            {
                return Forbid();
            }

            return View(userFeedback);
        }

        // POST: UserFeedbacks/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var userFeedback = await _context.UserFeedback
                .Include(f => f.Images)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (userFeedback == null)
            {
                return NotFound();
            }

            if (!await CanAccessFeedbackAsync(user, userFeedback))
            {
                return Forbid();
            }

            // Remove all related images
            if (userFeedback.Images != null && userFeedback.Images.Any())
            {
                _context.UserFeedbackImages.RemoveRange(userFeedback.Images);
            }

            // Remove the feedback itself
            _context.UserFeedback.Remove(userFeedback);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> GetImage(int imageId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var image = await _context.UserFeedbackImages
                .Include(i => i.UserFeedback)
                .FirstOrDefaultAsync(i => i.Id == imageId);

            if (image == null || image.Data == null || image.UserFeedback == null)
                return NotFound();

            if (!await CanAccessFeedbackAsync(user, image.UserFeedback))
            {
                return Forbid();
            }

            return File(image.Data, "image/png");
        }

        // POST: UserFeedbacks/AddReply
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddReply(int feedbackId, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return RedirectToAction(nameof(Details), new { id = feedbackId });

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var feedback = await _context.UserFeedback.FirstOrDefaultAsync(f => f.Id == feedbackId);
            if (feedback == null) return NotFound();

            if (!await CanAccessFeedbackAsync(user, feedback)) return Forbid();

            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");

            var reply = new UserFeedbackReply
            {
                UserFeedbackId = feedbackId,
                AuthorId = user.Id,
                Message = message.Trim(),
                CreatedAt = DateTime.UtcNow,
                IsAdminReply = isAdmin
            };

            // Auto-transition status when admin first replies
            if (isAdmin && feedback.Status == "New")
            {
                feedback.Status = "Open";
                _context.Update(feedback);
            }

            _context.UserFeedbackReplies.Add(reply);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = feedbackId });
        }

        // POST: UserFeedbacks/DeleteReply
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteReply(int replyId, int feedbackId)
        {
            var reply = await _context.UserFeedbackReplies.FirstOrDefaultAsync(r => r.Id == replyId);
            if (reply == null) return NotFound();

            _context.UserFeedbackReplies.Remove(reply);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = feedbackId });
        }

        // POST: UserFeedbacks/UpdateStatus
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int feedbackId, string status)
        {
            var feedback = await _context.UserFeedback.FirstOrDefaultAsync(f => f.Id == feedbackId);
            if (feedback == null) return NotFound();

            feedback.Status = status;
            _context.Update(feedback);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = feedbackId });
        }

        // POST: UserFeedbacks/Assign
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(int feedbackId, string? assignedTo)
        {
            var feedback = await _context.UserFeedback.FirstOrDefaultAsync(f => f.Id == feedbackId);
            if (feedback == null) return NotFound();

            feedback.AssignedTo = string.IsNullOrWhiteSpace(assignedTo) ? null : assignedTo;
            _context.Update(feedback);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = feedbackId });
        }

        private async Task<bool> CanAccessFeedbackAsync(IdentityUser user, UserFeedback feedback)
        {
            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                return true;
            }

            return feedback.UserId == user.Id;
        }

        private bool UserFeedbackExists(int id)
        {
            return _context.UserFeedback.Any(e => e.Id == id);
        }
    }
}
