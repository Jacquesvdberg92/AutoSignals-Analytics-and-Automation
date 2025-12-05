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
            var isAdmin = await _userManager.IsInRoleAsync(user, "ADMIN");
            ViewBag.IsAdmin = isAdmin;

            IQueryable<UserFeedback> feedbacks = _context.UserFeedback;

            if (!isAdmin)
            {
                feedbacks = feedbacks.Where(f => f.UserId == user.Id);
            }

            return View(await feedbacks.ToListAsync());
        }

        // GET: UserFeedbacks/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var userFeedback = await _context.UserFeedback
                .Include(f => f.Images)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (userFeedback == null)
            {
                return NotFound();
            }

            return View(userFeedback);
        }

        // GET: UserFeedbacks/Create
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
            userFeedback.UserId = user.Id;
            userFeedback.SubmittedAt = DateTime.UtcNow;
            userFeedback.Status = "New";

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
                        var image = new UserFeedbackImage
                        {
                            Data = ms.ToArray(),
                            FileName = file.FileName
                        };
                        userFeedback.Images.Add(image);
                    }
                }
            }

            _context.Add(userFeedback);
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
            var isAdmin = await _userManager.IsInRoleAsync(user, "ADMIN");
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
    [Bind("Id,UserId,Subject,Message,SubmittedAt,Status")] UserFeedback userFeedback,
    List<IFormFile> ScreenshotFiles)
        {
            if (id != userFeedback.Id)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "ADMIN");

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

            // Add new images (only admins can upload)
            if (isAdmin && ScreenshotFiles != null && ScreenshotFiles.Count > 0)
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
                    }
                    else
                    {
                        existingFeedback.Message = userFeedback.Message;
                        // Non-admins cannot change subject or status
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
        [HttpGet]
        public async Task<IActionResult> DeleteImage(int feedbackId, int imageId)
        {
            var user = await _userManager.GetUserAsync(User);
            var isAdmin = await _userManager.IsInRoleAsync(user, "ADMIN");

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

            var userFeedback = await _context.UserFeedback
                .FirstOrDefaultAsync(m => m.Id == id);
            if (userFeedback == null)
            {
                return NotFound();
            }

            return View(userFeedback);
        }

        // POST: UserFeedbacks/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userFeedback = await _context.UserFeedback
                .Include(f => f.Images)
                .FirstOrDefaultAsync(f => f.Id == id);

            if (userFeedback == null)
            {
                return NotFound();
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

        [AllowAnonymous]
        public async Task<IActionResult> GetImage(int imageId)
        {
            var image = await _context.UserFeedbackImages.FindAsync(imageId);
            if (image == null || image.Data == null)
                return NotFound();

            // Optionally, detect content type from file name or data
            return File(image.Data, "image/png");
        }

        private bool UserFeedbackExists(int id)
        {
            return _context.UserFeedback.Any(e => e.Id == id);
        }
    }
}
