using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crayon.Box.Pages;

public class Register : PageModel
{


    public string Organization => Config.Organization;
    public Version Ver => Assembly.GetEntryAssembly()!.GetName().Version!;
    public void OnGet()
    {
    }
}