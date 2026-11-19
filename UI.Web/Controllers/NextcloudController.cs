using Microsoft.AspNetCore.Mvc;

namespace UI.Web.Controllers;

public class NextcloudController : Controller
{
    public IActionResult Index() => View();
}