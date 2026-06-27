using Microsoft.AspNetCore.Mvc;

namespace Blog.Web.Controllers;

/// <summary>Marketing landing page for the Inkwell platform — accessible at /landing.</summary>
[Route("landing")]
public class LandingController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
