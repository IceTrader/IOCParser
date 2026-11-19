using Microsoft.AspNetCore.Mvc;

namespace UI.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
    // мета-контроллер ошибок можно оставить
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}