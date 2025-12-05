using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using starterkit.Models;

// Controllers/FunController.cs
public class FunController : Controller
{
    [Route("jy-mag-nie-hier-wees-nie")]
    public ActionResult Wag()
    {
        // Set move-in date here
        DateTime moveInDate = new DateTime(2025, 8, 12); // Example
        ViewBag.MoveInDate = moveInDate;
        return View("~/Views/JustForGags/Wag.cshtml");
    }
}
