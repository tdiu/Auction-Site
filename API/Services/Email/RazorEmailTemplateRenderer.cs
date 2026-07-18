using API.Interfaces;
using Razor.Templating.Core;

namespace API.Services.Email;

public class RazorEmailTemplateRenderer : IEmailTemplateRenderer
{
    public Task<string> RenderAsync(string templateName, object model, CancellationToken ct)
        => RazorTemplateEngine.RenderAsync($"~/Views/Emails/{templateName}.cshtml", model);

}
