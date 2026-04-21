using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.ObjectPool;

namespace Crayon.Box.Pages;

public class Profile : PageModel
{
    [BindProperty(SupportsGet = true)] public required string Id { get; set; }
    
    
    public bool UserExists { get; set; } = false;
    public bool ProviderConnected { get; set; } = false;


    public string Organization => Config.Organization;
    public Version Ver => Assembly.GetEntryAssembly()!.GetName().Version!;
    
    public void OnGet()
    {
        // Check if the user even exists.
        if (Server.Static != null)
        {
            try
            {
                var databaseId = Server.Static.UserExists(Id);
                UserExists = databaseId > 0;
                ProviderConnected = Server.Providers.ContainsKey(Id);

            } catch
            {
                Response.StatusCode = 404;
                UserExists = false;
                ProviderConnected = false;
            }
        }

    }
}