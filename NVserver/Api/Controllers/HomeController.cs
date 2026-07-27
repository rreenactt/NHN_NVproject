using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return Ok("Hello World");
    }
}