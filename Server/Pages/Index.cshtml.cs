using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crayon.Box.Pages;

public class Index : PageModel
{
    public string? SessionId { get; private set; }

    public void OnGet()
    {
        SessionId = Request.Cookies["session_id"];
    }
}